using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Starfield;
using Xunit;

namespace BuildKit.Tests;

public class EffectsTests
{
    static readonly FormLink<IMagicEffectGetter> Mgef = new(new FormKey(ModKey.FromFileName("Base.esm"), 0x123));

    [Fact]
    public void FixFillsEfifAndUniqueMuids()
    {
        var list = new List<Effect> { Effects.New(Mgef, 1), Effects.New(Mgef, 2), Effects.New(Mgef, 3) };
        list[2].MUID = 1;  // an entry copied from a master that already has MUID 1
        Effects.Fix(list);
        Assert.All(list, e => Assert.Equal(0, e.EFIF));
        Assert.Equal(3, list.Select(e => e.MUID).Distinct().Count());
    }

    [Fact]
    public void BareEntriesGetPositionPlusOne()
    {
        var bare = new List<Effect> { new() { BaseEffect = new FormLinkNullable<IMagicEffectGetter>(Mgef.FormKey) }, new() };
        Assert.Equal(2, Effects.Fix(bare));
        Assert.Equal((0, 1), (bare[0].EFIF, bare[0].MUID));
        Assert.Equal(2, bare[1].MUID);
        Assert.Equal(0, Effects.Fix(bare));
    }
}

public class GameplayOptionsTests
{
    [Fact]
    public void DebugOptionIsOffAndGroupedAfterItsOption()
    {
        var mod = Plugin.New("Options.esm", PluginStyle.Light, "test", "test");
        var dbg = GameplayOptions.Debug(mod, "OPT_Option_Debug", "Options' log lines on screen");
        var grp = GameplayOptions.Group(mod, "OPT_Options", "Options", [dbg]);
        Assert.True(dbg.Data is BoolGameplayOptionData { Value1: false, Value2: false });
        Assert.Equal(GameplayOptions.DebugName, dbg.Name?.String);
        Assert.EndsWith(GameplayOptions.DefaultNote(false), dbg.Description?.String);
        Assert.True(dbg.FormKey.ID < grp.FormKey.ID);
        var leaf = Assert.IsType<GameplayOptionsGroupLeaf>(grp.Options);
        Assert.Equal(dbg.FormKey, leaf.GameplayOptions.Single().FormKey);
        Assert.Equal(Plugin.CkFormVersion, dbg.FormVersion);
        Assert.Equal(Plugin.CkFormVersion, grp.FormVersion);
    }

    [Fact]
    public void FixedKeyIsUsed()
    {
        var mod = Plugin.New("Options.esm", PluginStyle.Light, "test", "test");
        var key = new FormKey(mod.ModKey, 0x9AB);
        var o = GameplayOptions.Bool(mod, "OPT_Option_X", "X", "Does X.", true, key);
        Assert.Equal(key, o.FormKey);
        Assert.Same(o, mod.GameplayOptions.Single());
        Assert.True(o.Data is BoolGameplayOptionData { Value1: true });
    }
}

public class PluginTests
{
    [Theory]
    [InlineData(PluginStyle.Light, 0x800u, 0xFFFu)]
    [InlineData(PluginStyle.Medium, 0x800u, 0xFFFFu)]
    [InlineData(PluginStyle.Full, 0x800u, 0xFFFFFFu)]
    public void IdRanges(PluginStyle style, uint first, uint last) => Assert.Equal((first, last), Plugin.IdRange(style));

    [Theory]
    [InlineData(PluginStyle.Light)]
    [InlineData(PluginStyle.Medium)]
    [InlineData(PluginStyle.Full)]
    public void NewSetsMasterFlagAndStyle(PluginStyle style)
    {
        var mod = Plugin.New("P.esm", style, "me", "desc");
        Assert.True(mod.ModHeader.Flags.HasFlag(StarfieldModHeader.HeaderFlag.Master));
        Assert.Equal(style, Plugin.StyleOf(mod));
        Assert.Equal(Plugin.CkFormVersion, mod.ModHeader.FormVersion);
        Assert.Equal(("me", "desc"), (mod.ModHeader.Author, mod.ModHeader.Description));
    }

    [Fact]
    public void LocalizedTextHasEveryLanguage()
    {
        var t = Localized.Text("hello");
        Assert.All(Localized.Languages, l => Assert.True(t.TryLookup(l, out var s) && s == "hello"));
    }
}

public class IdLockTests
{
    static StarfieldMod Synthetic(PluginStyle style, params string[] edids)
    {
        var m = Plugin.New("Lock.esm", style, "test", "test");
        foreach (var e in edids) m.Globals.AddNew(e);
        return m;
    }

    static uint Id(StarfieldMod m, string e) => m.Globals.Single(g => g.EditorID == e).FormKey.ID;

    [Fact]
    public void LockKeepsIdsAcrossReorderAndAddsNewRecords()
    {
        using var t = new TempDir();
        var lockPath = Path.Combine(t.Path, "ids.lock.json");
        var first = Synthetic(PluginStyle.Light, "A", "B", "C");
        var r1 = IdLock.Apply(first, lockPath);
        Assert.Equal((3, 3, 0), (r1.Locked, r1.Added, r1.Moved));

        var reordered = Synthetic(PluginStyle.Light, "C", "NEW", "A", "B");
        var r2 = IdLock.Apply(reordered, lockPath);
        foreach (var e in new[] { "A", "B", "C" }) Assert.Equal(Id(first, e), Id(reordered, e));
        Assert.Equal(0x803u, Id(reordered, "NEW"));
        Assert.Equal(1, r2.Added);
        Assert.True(reordered.ModHeader.Stats.NextFormID > 0x803);
    }

    [Fact]
    public void MissingLockedRecordStopsTheBuild()
    {
        using var t = new TempDir();
        var lockPath = Path.Combine(t.Path, "ids.lock.json");
        IdLock.Apply(Synthetic(PluginStyle.Light, "A", "B", "C"), lockPath);
        Assert.Throws<Exception>(() => IdLock.Apply(Synthetic(PluginStyle.Light, "A", "B"), lockPath));
    }

    [Fact]
    public void CheckOnlyRefusesChangesAndWritesNothing()
    {
        using var t = new TempDir();
        var lockPath = Path.Combine(t.Path, "ids.lock.json");
        IdLock.Apply(Synthetic(PluginStyle.Light, "A", "B"), lockPath);
        var before = File.ReadAllText(lockPath);
        Assert.Throws<Exception>(() => IdLock.Apply(Synthetic(PluginStyle.Light, "A", "B", "NEW"), lockPath, update: false));
        var same = IdLock.Apply(Synthetic(PluginStyle.Light, "A", "B"), lockPath, update: false);
        Assert.Equal(2, same.Locked);
        Assert.Equal(before, File.ReadAllText(lockPath));
    }

    [Fact]
    public void DuplicateOrMissingEditorIdsAreRefused()
    {
        using var t = new TempDir();
        var lockPath = Path.Combine(t.Path, "ids.lock.json");
        Assert.Throws<Exception>(() => IdLock.Apply(Synthetic(PluginStyle.Light, "A", "a"), lockPath));
        var noEdid = Synthetic(PluginStyle.Light, "A");
        noEdid.Globals.AddNew();
        Assert.Throws<Exception>(() => IdLock.Apply(noEdid, lockPath));
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public void LockedIdOutsideTheRangeIsRefused()
    {
        using var t = new TempDir();
        var lockPath = Path.Combine(t.Path, "ids.lock.json");
        File.WriteAllText(lockPath, "{ \"A\": \"1000\" }");
        Assert.Throws<Exception>(() => IdLock.Apply(Synthetic(PluginStyle.Light, "A"), lockPath));
        var medium = Synthetic(PluginStyle.Medium, "A");   // 0x1000 is inside the medium range
        IdLock.Apply(medium, lockPath);
        Assert.Equal(0x1000u, Id(medium, "A"));
    }
}

public class GameDataTests
{
    [Fact]
    public void OpeningAPluginOpensItsMastersFirst()
    {
        using var t = new TempDir();
        Fixtures.WriteBase(t.Path);
        Fixtures.WriteLight(t.Path);
        using var game = new GameData(t.Path);
        var light = game.Open("Light.esm");
        Assert.Equal(["Base.esm", "Light.esm"], game.LoadOrder.Select(m => m.ModKey.FileName.String));
        Assert.Same(light, game.Open("Light.esm"));
        Assert.Equal(["Base.esm"], GameData.HeaderMasters(Path.Combine(t.Path, "Light.esm")));
    }

    [Fact]
    public void LaterMasterWinsEditorIdLookups()
    {
        using var t = new TempDir();
        Fixtures.WriteBase(t.Path);
        Fixtures.WriteLight(t.Path);
        using var game = new GameData(t.Path);
        game.Open("Light.esm");
        Assert.Equal(2f, game.Get<IGlobalGetter>("Shared").Data);   // Light.esm's override, not Base.esm's 1
        Assert.Equal(ModKey.FromFileName("Base.esm"), game.Get<IGlobalGetter>("BaseOnly").FormKey.ModKey);
        Assert.Equal(ModKey.FromFileName("Light.esm"), game.Get<IGlobalGetter>("LightOnly").FormKey.ModKey);
        Assert.Throws<Exception>(() => game.Get<ICellGetter>("Shared"));
        Assert.Throws<Exception>(() => game.Get<IGlobalGetter>("Nothing"));
    }

    [Fact]
    public void LightMasterFormIdsResolveThroughTheLinkCache()
    {
        using var t = new TempDir();
        Fixtures.WriteBase(t.Path);
        Fixtures.WriteLight(t.Path);
        using var game = new GameData(t.Path);
        var light = game.Open("Light.esm");
        Assert.True(Plugin.StyleOf(light) == PluginStyle.Light);
        var own = light.Globals.Single(g => g.EditorID == "LightOnly");
        Assert.Equal(0x800u, own.FormKey.ID);
        Assert.True(game.LinkCache.TryResolve<IGlobalGetter>(own.FormKey, out var resolved) && resolved.EditorID == "LightOnly");
    }

    [Fact]
    public void ExtraFoldersAreSearchedFirstAndMissingPluginsGiveNull()
    {
        using var t = new TempDir();
        var data = t.Sub("Data");
        var stage = t.Sub("Stage");
        Fixtures.WriteBase(data);
        Fixtures.WriteLight(stage, data);
        using var game = new GameData(data, [stage, Path.Combine(t.Path, "no-such-folder")]);
        Assert.Equal([stage], game.ExtraDirs);
        Assert.Equal(Path.Combine(stage, "Light.esm"), game.Locate("Light.esm"));
        Assert.Equal(Path.Combine(data, "Base.esm"), game.Locate("Base.esm"));
        Assert.NotNull(game.TryOpen("Light.esm"));
        Assert.Null(game.TryOpen("Missing.esm"));
        Assert.Throws<FileNotFoundException>(() => game.Open("Missing.esm"));
        Assert.Throws<DirectoryNotFoundException>(() => new GameData(Path.Combine(t.Path, "nowhere")));
    }
}

public class PluginWriterTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverrideOfATemporaryReferenceIsListedInOnam(bool onam)
    {
        using var t = new TempDir();
        var (cellKey, placedKey) = Fixtures.WriteBase(t.Path);
        using var game = new GameData(t.Path);
        game.Open("Base.esm");
        var mod = Plugin.New("Patch.esm", PluginStyle.Light, "test", "patch");
        var cell = new Cell(cellKey, StarfieldRelease.Starfield) { EditorID = "BaseCell" };
        cell.Temporary.Add(new PlacedObject(placedKey, StarfieldRelease.Starfield) { EditorID = "BasePlaced" });
        Fixtures.AddInteriorCell(mod, cell);
        var options = onam ? Fixtures.NoGapCheck
            : new WriteOptions { GapCheck = false, OverriddenForms = OverriddenFormsOption.NoCheck };
        var path = PluginWriter.Write(mod, t.Sub("out"), game, options);
        using var back = game.OpenPlugin(path);
        var listed = back.ModHeader.OverriddenForms?.Select(f => f.FormKey).ToList() ?? [];
        Assert.Equal(onam, listed.Contains(placedKey));   // the default writes ONAM; switched off, nothing is listed
    }

    [Fact]
    public void SameBuildGivesTheSameBytesIncludingStrings()
    {
        using var t = new TempDir();
        Fixtures.WriteBase(t.Path);
        using var game = new GameData(t.Path);
        game.Open("Base.esm");
        byte[][] Build(string outDir)
        {
            var mod = Plugin.New("Strings.esm", PluginStyle.Light, "test", "localized", localized: true);
            for (var i = 0; i < 40; i++)
            {
                var o = GameplayOptions.Bool(mod, $"S_Option_{i}", $"Option {i}", $"Text {i}", i % 2 == 0, text: Localized.Text);
                Assert.NotNull(o);
            }
            for (var i = 0; i < 40; i++) mod.Keywords.AddNew($"S_Kw_{i}").Name = Localized.Text($"Keyword {i}");
            PluginWriter.Write(mod, outDir, game, Fixtures.NoGapCheck);
            return Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(File.ReadAllBytes).ToArray();
        }
        var a = Build(t.Sub("a"));
        var b = Build(t.Sub("b"));
        Assert.True(a.Length > 1);  // the plugin plus its string tables
        Assert.Equal(a.Length, b.Length);
        for (var i = 0; i < a.Length; i++) Assert.Equal(a[i], b[i]);
    }

    [Fact]
    public void EffectsAreFixedOnWrite()
    {
        using var t = new TempDir();
        Fixtures.WriteBase(t.Path);
        using var game = new GameData(t.Path);
        game.Open("Base.esm");
        var mod = Plugin.New("Spells.esm", PluginStyle.Light, "test", "spells");
        var spell = mod.Spells.AddNew("SP_Test");
        spell.Effects.Add(new Effect());
        spell.Effects.Add(new Effect());
        var path = PluginWriter.Write(mod, t.Sub("out"), game, Fixtures.NoGapCheck);
        using var back = game.OpenPlugin(path);
        var effects = back.Spells.Single().Effects;
        Assert.Equal([1, 2], effects.Select(e => e.MUID ?? -1));
        Assert.All(effects, e => Assert.Equal(0, e.EFIF));
    }

    [Fact]
    public void ExplicitMastersAreWrittenExactlyInOrder()
    {
        using var t = new TempDir();
        Fixtures.WriteBase(t.Path);
        Fixtures.WriteLight(t.Path);
        using var game = new GameData(t.Path);
        var b = game.Open("Base.esm");
        game.Open("Light.esm");
        var mod = Plugin.New("Order.esm", PluginStyle.Light, "test", "order patch");
        mod.Globals.GetOrAddAsOverride(b.Globals.First(g => g.EditorID == "BaseOnly"));
        var masters = new[] { ModKey.FromFileName("Base.esm"), ModKey.FromFileName("Light.esm") };

        var plain = PluginWriter.Write(mod, t.Sub("plain"), game, Fixtures.NoGapCheck);
        Assert.Equal(["Base.esm"], GameData.HeaderMasters(plain));   // Mutagen's default: only masters records use

        var path = PluginWriter.Write(mod, t.Sub("explicit"), game, new WriteOptions { GapCheck = false, Masters = masters });
        Assert.Equal(["Base.esm", "Light.esm"], GameData.HeaderMasters(path));
    }

    [Fact]
    public void NextFormIdNoCheckKeepsTheHeaderValue()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        StarfieldMod Make()
        {
            var mod = Plugin.New("Next.esm", PluginStyle.Light, "test", "next id");
            mod.Globals.AddNew("G");
            mod.ModHeader.Stats.NextFormID = 0x900;
            return mod;
        }
        using (var kept = game.OpenPlugin(PluginWriter.Write(Make(), t.Sub("kept"), game,
                   new WriteOptions { GapCheck = false, NextFormId = NextFormIDOption.NoCheck })))
            Assert.Equal(0x900u, kept.ModHeader.Stats.NextFormID);
        using (var iterated = game.OpenPlugin(PluginWriter.Write(Make(), t.Sub("iter"), game, Fixtures.NoGapCheck)))
            Assert.Equal(0x801u, iterated.ModHeader.Stats.NextFormID);
    }

    [Fact]
    public void MissingExplicitGapCheckScriptFailsClearly()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var mod = Plugin.New("Gap.esm", PluginStyle.Light, "test", "gap");
        var opts = new WriteOptions { GapCheckScript = Path.Combine(t.Path, "nope.py") };
        Assert.Throws<FileNotFoundException>(() => PluginWriter.Write(mod, t.Sub("out"), game, opts));
    }

    [Fact]
    public void TryFindToolGivesNullForAMissingFile() =>
        Assert.Null(PluginWriter.TryFindTool("no-such-buildkit-helper-7f3a.py"));
}

public class RecordsTests
{
    static byte[] WriteAndRead(StarfieldMod mod, TempDir tmp)
    {
        using var game = new GameData(tmp.Path);
        return File.ReadAllBytes(PluginWriter.Write(mod, tmp.Sub("out"), game, Fixtures.NoGapCheck));
    }

    // Starfield keeps dialogue topics inside their quest.
    static DialogTopic Topic(StarfieldMod mod, string editorId, DialogTopic.SubtypeEnum subtype = DialogTopic.SubtypeEnum.Custom)
    {
        var q = mod.Quests.FirstOrDefault() ?? Quests.New(mod, "DT_Quest");
        var t = new DialogTopic(mod.GetNextFormKey(), StarfieldRelease.Starfield) { EditorID = editorId, Subtype = subtype };
        q.DialogTopics.Add(t);
        return t;
    }

    static bool Has(byte[] bytes, string tag) => bytes.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(tag)) >= 0;

    [Fact]
    public void NewQuestIsWrittenWithNextAndAnam()
    {
        using var tmp = new TempDir();
        var mod = Plugin.New("Quests.esm", PluginStyle.Light, "test", "test");
        var q = Quests.New(mod, "QT_Quest");
        Assert.Equal(Plugin.CkFormVersion, q.FormVersion);
        var bytes = WriteAndRead(mod, tmp);
        Assert.True(Has(bytes, "NEXT"));
        Assert.True(Has(bytes, "ANAM"));
    }

    [Fact]
    public void PlainAddNewQuestLacksNextAndAnam()  // the Mutagen 0.54.4 behaviour Quests.New works around
    {
        using var tmp = new TempDir();
        var mod = Plugin.New("Quests.esm", PluginStyle.Light, "test", "test");
        mod.Quests.AddNew("QT_Quest");
        var bytes = WriteAndRead(mod, tmp);
        Assert.False(Has(bytes, "NEXT"));
        Assert.False(Has(bytes, "ANAM"));
    }

    [Fact]
    public void FixedKeyQuestUsesTheKey()
    {
        var mod = Plugin.New("Quests.esm", PluginStyle.Light, "test", "test");
        var key = new FormKey(mod.ModKey, 0x9AB);
        var q = Quests.New(mod, "QT_Quest", key);
        Assert.Equal(key, q.FormKey);
        Assert.Same(q, mod.Quests.Single());
        Assert.Equal("QT_Quest", q.EditorID);
    }

    [Fact]
    public void BlankSnamIsFilledAndASetOneIsKept()
    {
        var mod = Plugin.New("Topics.esm", PluginStyle.Light, "test", "test");
        var blank = Topic(mod, "DT_Blank", DialogTopic.SubtypeEnum.Greeting);
        var set = Topic(mod, "DT_Set", DialogTopic.SubtypeEnum.Greeting);
        set.SubtypeName = DialogTopic.SubtypeNameEnum.Hello;   // deliberately different: must stay as the builder set it
        var odd = Topic(mod, "DT_Odd", DialogTopic.SubtypeEnum.ShootBow);
        Assert.Equal((1, 1), DialogTopics.FixSubtypeNames(mod));
        Assert.Equal(DialogTopic.SubtypeNameEnum.Greeting, blank.SubtypeName);
        Assert.Equal(DialogTopic.SubtypeNameEnum.Hello, set.SubtypeName);
        Assert.Equal((DialogTopic.SubtypeNameEnum)0, odd.SubtypeName);
        Assert.Equal((0, 1), DialogTopics.FixSubtypeNames(mod));
    }

    [Fact]
    public void WriterFillsSnamUnlessTurnedOff()
    {
        using var tmp = new TempDir();
        using var game = new GameData(tmp.Path);
        var mod = Plugin.New("Topics.esm", PluginStyle.Light, "test", "test");
        var t = Topic(mod, "DT_Custom");   // Subtype Custom = 0, SNAM code CUST
        PluginWriter.Write(mod, tmp.Sub("off"), game, new WriteOptions { GapCheck = false, FixDialogSubtypes = false });
        Assert.Equal((DialogTopic.SubtypeNameEnum)0, t.SubtypeName);
        var path = PluginWriter.Write(mod, tmp.Sub("on"), game, Fixtures.NoGapCheck);
        Assert.Equal(DialogTopic.SubtypeNameEnum.Custom, t.SubtypeName);
        using var back = game.OpenPlugin(path);
        Assert.Equal(DialogTopic.SubtypeNameEnum.Custom, back.Quests.Single().DialogTopics.Single().SubtypeName);
    }
}
