using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Xunit;

namespace BuildKit.Tests;

public class MessagesTests
{
    static IStarfieldModDisposableGetter WriteAndOpen(StarfieldMod mod, TempDir t, GameData game) =>
        game.OpenPlugin(PluginWriter.Write(mod, t.Sub("out"), game, Fixtures.NoGapCheck));

    [Fact]
    public void HudLineHasTheVanillaShape()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var mod = Plugin.New("Msg.esm", PluginStyle.Light, "test", "messages");
        var hud = Messages.Hud(mod, "MT_Hud", "%.0f items sent to your ship", 3);
        Assert.Equal(Plugin.CkFormVersion, hud.FormVersion);
        using var back = WriteAndOpen(mod, t, game);
        var m = back.Messages.Single();
        Assert.Equal("MT_Hud", m.EditorID);
        Assert.Equal("%.0f items sent to your ship", m.Description?.String);
        Assert.Equal((Message.Flag)0, m.Flags);
        Assert.Equal(3u, m.DisplayTime);
        Assert.True(m.INAM == 0);
        Assert.Equal(0xFFFFFFFFu, m.BNAM);
        Assert.Empty(m.MenuButtons);
        Assert.True(string.IsNullOrEmpty(m.Name?.String));
    }

    [Fact]
    public void BoxWithoutButtonsLeavesTheOkButtonToTheGame()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var mod = Plugin.New("Msg.esm", PluginStyle.Light, "test", "messages");
        Messages.Box(mod, "MT_Box", "The hunt is stopped.");
        using var back = WriteAndOpen(mod, t, game);
        var m = back.Messages.Single();
        Assert.Equal(Message.Flag.MessageBox, m.Flags);
        Assert.Null(m.DisplayTime);
        Assert.True(m.INAM == 0);
        Assert.Equal(0xFFFFFFFFu, m.BNAM);
        Assert.Empty(m.MenuButtons);
    }

    [Fact]
    public void BoxKeepsTitleButtonsAndOwnerQuest()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var mod = Plugin.New("Msg.esm", PluginStyle.Light, "test", "messages");
        var q = Quests.New(mod, "MT_Quest");
        Messages.Box(mod, "MT_Menu", "Where to?", "MY MOD", ["Home", "Cancel"], q);
        using var back = WriteAndOpen(mod, t, game);
        var m = back.Messages.Single();
        Assert.Equal("MY MOD", m.Name?.String);
        Assert.Equal(["Home", "Cancel"], m.MenuButtons.Select(b => b.Text?.String));
        Assert.Equal(q.FormKey, m.OwnerQuest.FormKey);
    }

    // Subrecord order as in the vanilla records named in Messages.cs (read raw with SFSearch, 2026-11-18).
    static List<string> Order(byte[] plugin)
    {
        var tags = new List<string>();
        for (var p = 0; p + 6 <= plugin.Length;)
        {
            var sig = System.Text.Encoding.ASCII.GetString(plugin, p, 4);
            if (sig == "MESG") { p += 24; continue; }      // record header
            if (sig == "GRUP") { p += 24; continue; }
            if (sig == "TES4") { p += 24 + BitConverter.ToInt32(plugin, p + 4); continue; }
            var len = BitConverter.ToUInt16(plugin, p + 4);
            tags.Add(sig);
            p += 6 + len;
        }
        return tags;
    }

    [Theory]
    [InlineData(false, "EDID,DESC,INAM,DNAM,TNAM,BNAM")]
    [InlineData(true, "EDID,DESC,FULL,INAM,DNAM,BNAM,ITXT")]
    public void SubrecordsComeInTheVanillaOrder(bool box, string expected)
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var mod = Plugin.New("Msg.esm", PluginStyle.Light, "test", "messages");
        if (box) Messages.Box(mod, "MT_Order", "Text", "Title", ["OK"]);
        else Messages.Hud(mod, "MT_Order", "Text", 2);
        var path = PluginWriter.Write(mod, t.Sub("out"), game, Fixtures.NoGapCheck);
        Assert.Equal(expected, string.Join(",", Order(File.ReadAllBytes(path))));
    }

    [Fact]
    public void FixedKeyAndLocalizedTextAreUsed()
    {
        var mod = Plugin.New("Msg.esm", PluginStyle.Light, "test", "messages", localized: true);
        var key = new FormKey(mod.ModKey, 0x900);
        var m = Messages.Hud(mod, "MT_Fixed", "Hello", key: key, text: Localized.Text);
        Assert.Same(m, mod.Messages.Single());
        Assert.Equal(key, m.FormKey);
        Assert.True(m.Description!.NumLanguages > 1);
    }
}
