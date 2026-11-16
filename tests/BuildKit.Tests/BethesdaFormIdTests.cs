// WriteOptions.BethesdaFormIds: light and medium plugins store their own new records the way Bethesda's own Starfield
// plugins do (FE / FD form), checked on the raw bytes, plus read-back through Mutagen and the link cache.
using System.Buffers.Binary;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Xunit;

// Mutagen keeps the list BethesdaFormIds adds to in one static set, which Mutagen reads while writing; tests run one
// at a time so no write ever sees that set change under it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace BuildKit.Tests;

public class BethesdaFormIdTests
{
    /// <summary>Every record (not group) in the file: signature and the FormID exactly as stored.</summary>
    static List<(string Sig, uint FormId)> RawRecords(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var list = new List<(string, uint)>();
        void Walk(int pos, int end)
        {
            while (pos + 24 <= end)
            {
                var sig = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
                var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 4));
                if (sig == "GRUP") { Walk(pos + 24, pos + size); pos += size; continue; }
                if (sig != "TES4") list.Add((sig, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 12))));
                pos += 24 + size;
            }
        }
        Walk(0, bytes.Length);
        return list;
    }

    /// <summary>Every 4-byte value in the file equal to formId (to find references to it in record data).</summary>
    static int Occurrences(string path, uint formId)
    {
        var bytes = File.ReadAllBytes(path);
        var n = 0;
        for (var i = 0; i + 4 <= bytes.Length; i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) == formId) n++;
        return n;
    }

    static string WriteWithList(string dir, GameData game, string name, PluginStyle style, WriteOptions options)
    {
        var mod = Plugin.New(name, style, "test", "Bethesda FormID test");
        var g = mod.Globals.AddNew("Own");
        g.Data = 1;
        var list = mod.FormLists.AddNew("OwnList");
        list.Items.Add(g.ToLink<IStarfieldMajorRecordGetter>());
        return PluginWriter.Write(mod, dir, game, options);
    }

    [Fact]
    public void LightPluginRecordsAndReferencesUseTheFeForm()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var path = WriteWithList(t.Path, game, "FeLight.esm", PluginStyle.Light, Fixtures.NoGapCheck);
        Assert.All(RawRecords(path), r => Assert.Equal(0xFEu, r.FormId >> 24));
        Assert.Equal([0xFE000800u, 0xFE000801u], RawRecords(path).Select(r => r.FormId).Order());
        Assert.Equal(2, Occurrences(path, 0xFE000800));   // the global's header, and the list's reference to it
        using var back = game.OpenPlugin(path);
        Assert.Equal(new FormKey(ModKey.FromFileName("FeLight.esm"), 0x800), back.Globals.Single().FormKey);
        Assert.Equal(back.Globals.Single().FormKey, back.FormLists.Single().Items.Single().FormKey);
    }

    [Fact]
    public void TurnedOffGivesMutagensFullMasterForm()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        // Written once with the option on first, so turning it off must really undo it.
        WriteWithList(t.Sub("on"), game, "FeOff.esm", PluginStyle.Light, Fixtures.NoGapCheck);
        var path = WriteWithList(t.Sub("off"), game, "FeOff.esm", PluginStyle.Light,
            new WriteOptions { GapCheck = false, BethesdaFormIds = false });
        Assert.Equal([0x00000800u, 0x00000801u], RawRecords(path).Select(r => r.FormId).Order());   // no masters: index 0
        Assert.False(BethesdaFormIds.IsEnabled(ModKey.FromFileName("FeOff.esm")));
    }

    [Fact]
    public void MediumPluginUsesTheFdForm()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var path = WriteWithList(t.Path, game, "FdMedium.esm", PluginStyle.Medium, Fixtures.NoGapCheck);
        Assert.Equal([0xFD000800u, 0xFD000801u], RawRecords(path).Select(r => r.FormId).Order());
        using var back = game.OpenPlugin(path);
        Assert.Equal(0x800u, back.Globals.Single().FormKey.ID);
    }

    [Fact]
    public void FullPluginIsUnchanged()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var on = WriteWithList(t.Sub("on"), game, "FullSame.esm", PluginStyle.Full, Fixtures.NoGapCheck);
        var off = WriteWithList(t.Sub("off"), game, "FullSame.esm", PluginStyle.Full,
            new WriteOptions { GapCheck = false, BethesdaFormIds = false });
        Assert.Equal(File.ReadAllBytes(off), File.ReadAllBytes(on));
    }

    [Fact]
    public void AddOnOfALightMasterCountsThatMaster()
    {
        // Base.esm (full) <- Light.esm (light) <- AddOn.esm (light). AddOn's own records are FE001xxx (one light master
        // before it); its reference to Light.esm's LightOnly is FE000800; its override of Base.esm's Shared stays 00.
        using var t = new TempDir();
        Fixtures.WriteBase(t.Path);
        Fixtures.WriteLight(t.Path);
        Assert.Contains(RawRecords(Path.Combine(t.Path, "Light.esm")), r => r.FormId == 0xFE000800);   // LightOnly
        using var game = new GameData(t.Path);
        var light = game.Open("Light.esm");
        var baseMod = game.Open("Base.esm");
        var mod = Plugin.New("AddOn.esm", PluginStyle.Light, "test", "add-on");
        var own = mod.Globals.AddNew("AddOnOnly");
        var list = mod.FormLists.AddNew("AddOnList");
        list.Items.Add(own.ToLink<IStarfieldMajorRecordGetter>());
        list.Items.Add(light.Globals.Single(g => g.EditorID == "LightOnly").ToLink<IStarfieldMajorRecordGetter>());
        mod.Globals.GetOrAddAsOverride(baseMod.Globals.Single(g => g.EditorID == "Shared")).Data = 3;
        var path = PluginWriter.Write(mod, t.Sub("out"), game, Fixtures.NoGapCheck);

        var raw = RawRecords(path);
        Assert.Contains(raw, r => r.Sig == "GLOB" && r.FormId == 0x00000800);   // the override of Base.esm's Shared
        Assert.Contains(raw, r => r.Sig == "GLOB" && r.FormId == 0xFE001800);   // AddOnOnly
        Assert.Contains(raw, r => r.Sig == "FLST" && r.FormId == 0xFE001801);
        Assert.Equal(1, Occurrences(path, 0xFE000800));   // the list's reference to Light.esm's LightOnly

        using var game2 = new GameData(t.Path, [t.Sub("out")]);
        var back = game2.Open("AddOn.esm");
        var items = back.FormLists.Single().Items.Select(i => i.FormKey).ToList();
        Assert.Equal(new FormKey(ModKey.FromFileName("AddOn.esm"), 0x800), items[0]);
        Assert.Equal(new FormKey(ModKey.FromFileName("Light.esm"), 0x800), items[1]);
        Assert.True(game2.LinkCache.TryResolve<IGlobalGetter>(items[1], out var lightOnly) && lightOnly.EditorID == "LightOnly");
    }

    [Fact]
    public void FeFormReadsBackWithTheSwitchOff()
    {
        // Another tool (or a fresh process) reads our plugins without BethesdaFormIds enabled: plain Mutagen must still
        // give the FE-form records and references to the right plugins (checked by hand in a separate process too,
        // 2026-11-16, and on FQC Ryujin 1.0.2).
        using var t = new TempDir();
        Fixtures.WriteBase(t.Path);
        Fixtures.WriteLight(t.Path);
        using (var g = new GameData(t.Path))
        {
            var light = g.Open("Light.esm");
            var mod = Plugin.New("AddOn.esm", PluginStyle.Light, "test", "add-on");
            var own = mod.Globals.AddNew("AddOnOnly");   // first, so it gets 0x800
            var list = mod.FormLists.AddNew("AddOnList");
            list.Items.Add(own.ToLink<IStarfieldMajorRecordGetter>());
            list.Items.Add(light.Globals.Single(x => x.EditorID == "LightOnly").ToLink<IStarfieldMajorRecordGetter>());
            PluginWriter.Write(mod, t.Path, g, Fixtures.NoGapCheck);
        }
        Assert.Contains(RawRecords(Path.Combine(t.Path, "AddOn.esm")), r => r.FormId == 0xFE001800);

        BethesdaFormIds.Disable(ModKey.FromFileName("Light.esm"));
        BethesdaFormIds.Disable(ModKey.FromFileName("AddOn.esm"));
        using var game = new GameData(t.Path);
        Assert.Contains(game.Open("Light.esm").Globals, x => x.EditorID == "LightOnly" && x.FormKey == new FormKey(ModKey.FromFileName("Light.esm"), 0x800));
        var items = game.Open("AddOn.esm").FormLists.Single().Items.Select(i => i.FormKey).ToList();
        Assert.Equal(new FormKey(ModKey.FromFileName("AddOn.esm"), 0x800), items[0]);
        Assert.Equal(new FormKey(ModKey.FromFileName("Light.esm"), 0x800), items[1]);
    }
}
