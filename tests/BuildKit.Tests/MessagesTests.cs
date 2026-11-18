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

    // Subrecord order of the MESG records only, as in the vanilla records named in Messages.cs (read raw with
    // SFSearch, 2026-11-18). Vanilla orders with an owner quest, e.g. MissionBoardAcceptMessage:
    // EDID,DESC,FULL,INAM,QNAM,DNAM,BNAM,ITXT... (tools\format-compare\mesg_order.py over Starfield.esm).
    static List<string> Order(byte[] plugin)
    {
        var tags = new List<string>();
        for (var p = 0; p + 24 <= plugin.Length;)
        {
            var sig = System.Text.Encoding.ASCII.GetString(plugin, p, 4);
            var size = BitConverter.ToInt32(plugin, p + 4);
            if (sig == "GRUP") { p += 24; continue; }        // step into the group
            if (sig == "MESG")
                for (int q = p + 24, end = p + 24 + size; q + 6 <= end; q += 6 + BitConverter.ToUInt16(plugin, q + 4))
                    tags.Add(System.Text.Encoding.ASCII.GetString(plugin, q, 4));
            p += 24 + size;                                    // any record: skip its body
        }
        return tags;
    }

    [Theory]
    [InlineData("hud", "EDID,DESC,INAM,DNAM,TNAM,BNAM")]
    [InlineData("box", "EDID,DESC,FULL,INAM,DNAM,BNAM,ITXT")]
    [InlineData("menu", "EDID,DESC,FULL,INAM,QNAM,DNAM,BNAM,ITXT,ITXT")]
    public void SubrecordsComeInTheVanillaOrder(string kind, string expected)
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var mod = Plugin.New("Msg.esm", PluginStyle.Light, "test", "messages");
        if (kind == "box") Messages.Box(mod, "MT_Order", "Text", "Title", ["OK"]);
        else if (kind == "menu") Messages.Box(mod, "MT_Order", "Text", "Title", ["Home", "Cancel"], Quests.New(mod, "MT_Q"));
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

    [Fact]
    public void LocalizedPluginsGetAllLanguagesByDefault()
    {
        var loc = Plugin.New("Loc.esm", PluginStyle.Light, "test", "messages", localized: true);
        var hud = Messages.Hud(loc, "MT_Hud", "Hello");
        var box = Messages.Box(loc, "MT_Box", "Hello", "Title", ["OK"]);
        var opt = GameplayOptions.Debug(loc, "MT_Debug", "lines");
        var grp = GameplayOptions.Group(loc, "MT_Group", "Mod", [opt]);
        foreach (var s in new[] { hud.Description, box.Description, box.Name, box.MenuButtons[0].Text, opt.Name, opt.Description, grp.Name })
            Assert.Equal(Localized.Languages.Length, s!.NumLanguages);

        var plain = Plugin.New("Plain.esm", PluginStyle.Light, "test", "messages");
        Assert.Equal(1, Messages.Hud(plain, "MT_Hud", "Hello").Description!.NumLanguages);
        Assert.Equal(1, GameplayOptions.Debug(plain, "MT_Debug", "lines").Name!.NumLanguages);
    }
}
