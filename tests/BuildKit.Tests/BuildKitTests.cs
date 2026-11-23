using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Strings;
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

    [Fact]
    public void OnOffOptionHasTheCreationKitValueRows()
    {
        // The field order and values the Creation Kit writes on an on/off option (no GPOD: that is for sliders).
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var mod = Plugin.New("Rows.esm", PluginStyle.Light, "test", "test");
        GameplayOptions.Debug(mod, "OPT_Option_Debug", "lines");
        var path = PluginWriter.Write(mod, t.Path, game, Fixtures.NoGapCheck);
        var subs = BethesdaFieldsTests.Records(path).Single(r => r.Sig == "GPOF").Subs;
        Assert.Equal("EDID NNAM DNAM TNAM VNAM WNAM VOVS VORV RESN VORN VOVS VORV RESN VORN",
            string.Join(" ", subs.Select(s => s.Item1)));
        string Str(int i) => System.Text.Encoding.ASCII.GetString(subs[i].Item2).TrimEnd('\0');
        float F(int i) => BitConverter.ToSingle(subs[i].Item2);
        Assert.Equal(new byte[] { 0 }, subs[3].Item2);                      // TNAM: on/off
        Assert.Equal(("", 0f, "DISABLE", 0f), (Str(6), F(7), Str(8), F(9)));
        Assert.Equal(("", 1f, "ENABLE", 0f), (Str(10), F(11), Str(12), F(13)));
    }

    [Fact]
    public void BoolOptionHasOffThenOnRows()
    {
        var mod = Plugin.New("Options.esm", PluginStyle.Light, "test", "test");
        var o = GameplayOptions.Bool(mod, "OPT_Option_X", "X", "Does X.", false);
        Assert.Equal(2, o.Rewards.Count);
        Assert.Equal([0f, 1f], o.Rewards.Select(r => r.SettingValue));
    }

    [Fact]
    public void TheValueRowsNeverGoThroughTheBuildersTextFunction()
    {
        // A translator that knows only the mod's own strings must not be asked for DISABLE, ENABLE or "".
        var known = new Dictionary<string, string> { ["  Show Debug Messages (PC only)"] = "Debug" };
        TranslatedString Translate(string s) =>
            known.TryGetValue(s, out var v) ? Localized.Text(v) : s.StartsWith("Shows ") ? Localized.Text(s) : throw new KeyNotFoundException(s);
        var mod = Plugin.New("LocRows.esm", PluginStyle.Light, "test", "test", localized: true);
        var o = GameplayOptions.Debug(mod, "OPT_Option_Debug", "lines", text: Translate);
        Assert.Equal("Debug", o.Name!.String);
        Assert.Equal(["DISABLE", "ENABLE"], o.Rewards.Select(r => r.Name!.String));
        Assert.All(o.Rewards, r => Assert.Equal(Localized.Languages.Length, r.Name!.NumLanguages));
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

    [Fact]
    public void LocalizedForFollowsThePluginAndTheTextFunction()
    {
        var plain = Plugin.New("Plain.esm", PluginStyle.Light, "test", "plain");
        var p = Localized.For(plain, "hello");
        Assert.Equal(1, p.NumLanguages);
        Assert.Equal("hello", p.String);

        var loc = Plugin.New("Loc.esm", PluginStyle.Light, "test", "localized", localized: true);
        var l = Localized.For(loc, "hello");
        Assert.Equal(Localized.Languages.Length, l.NumLanguages);
        Assert.All(Localized.Languages, lang => Assert.True(l.TryLookup(lang, out var s) && s == "hello"));

        // The builder's own text: function wins on both kinds of plugin.
        TranslatedString Shout(string s) => s.ToUpperInvariant();
        Assert.Equal("HELLO", Localized.For(plain, "hello", Shout).String);
        Assert.Equal("HELLO", Localized.For(loc, "hello", text: Shout).String);
        Assert.Equal(1, Localized.For(loc, "hello", Shout).NumLanguages);

        // A builder that holds its mod as the interface type compiles and gets the same answer.
        IStarfieldModGetter asInterface = loc;
        Assert.Equal(Localized.Languages.Length, Localized.For(asInterface, "hello").NumLanguages);
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

    // These two run on any system. On Windows the file system ignores case anyway; on Linux they exercise the
    // folder listing that matches names in any spelling.
    [Fact]
    public void PluginsAreFoundInAnySpelling()
    {
        using var t = new TempDir();
        Fixtures.WriteBase(t.Path);
        File.Move(Path.Combine(t.Path, "Base.esm"), Path.Combine(t.Path, "tmp.bin"));
        File.Move(Path.Combine(t.Path, "tmp.bin"), Path.Combine(t.Path, "base.ESM"));
        Assert.Contains("base.ESM", Directory.GetFiles(t.Path).Select(Path.GetFileName));
        using var game = new GameData(t.Path);
        var found = game.Locate("Base.esm");
        Assert.NotNull(found);
        Assert.Equal("base.esm", Path.GetFileName(found)!.ToLowerInvariant());
        Assert.NotNull(game.TryOpen("BASE.esm"));
        Assert.Same(game.Open("Base.esm"), game.Open("base.ESM"));
        Assert.Single(game.LoadOrder);
        Assert.Equal(1f, game.Get<IGlobalGetter>("Shared").Data);
    }

    [Fact]
    public void HeaderMasterSpelledDifferentlyFromItsFileIsOpened()
    {
        using var t = new TempDir();
        Fixtures.WriteBase(t.Path);
        Fixtures.WriteLight(t.Path);                       // Light.esm's header lists "Base.esm"
        File.Move(Path.Combine(t.Path, "Base.esm"), Path.Combine(t.Path, "tmp.bin"));
        File.Move(Path.Combine(t.Path, "tmp.bin"), Path.Combine(t.Path, "BASE.ESM"));
        Assert.Equal(["Base.esm"], GameData.HeaderMasters(Path.Combine(t.Path, "Light.esm")));
        using var game = new GameData(t.Path);
        Assert.NotNull(game.TryOpen("Light.esm"));
        Assert.Equal(2, game.LoadOrder.Count);
        Assert.Equal(2f, game.Get<IGlobalGetter>("Shared").Data);   // Light.esm's override still wins
        Assert.Empty(game.Warnings);
        // The master keeps the spelling it was asked for, whatever the disk calls it.
        Assert.Equal("Base.esm", game.LoadOrder[0].ModKey.FileName.String);
    }

    [Fact]
    public void ALocalizedPluginSpelledDifferentlyOnDiskKeepsItsStrings()
    {
        using var t = new TempDir();
        var data = t.Sub("Data");
        Fixtures.WriteBase(data);
        using (var writer = new GameData(data))
        {
            writer.Open("Base.esm");
            var mod = Plugin.New("Loc.esm", PluginStyle.Full, "test", "localized", localized: true);
            mod.Keywords.AddNew("L_Kw").Name = Localized.Text("Keyword text");
            PluginWriter.Write(mod, data, writer, Fixtures.NoGapCheck);
        }
        // The names GameData rebuilds under the requested spelling: Name_lang.EXT, language lower case, extension upper.
        var written = Directory.GetFiles(Path.Combine(data, "Strings")).Select(Path.GetFileName).ToList();
        Assert.NotEmpty(written);
        Assert.All(written, n => Assert.Matches(@"^Loc_[a-z]+\.[A-Z]+$", n));
        // Lower-case the plugin and its string tables, as a case-sensitive disk might hold them.
        foreach (var f in Directory.GetFiles(data, "*", SearchOption.AllDirectories))
            if (Path.GetFileName(f).StartsWith("Loc", StringComparison.Ordinal))
            {
                var lower = Path.Combine(Path.GetDirectoryName(f)!, Path.GetFileName(f).ToLowerInvariant());
                File.Move(f, f + ".tmp");
                File.Move(f + ".tmp", lower);
            }
        using var game = new GameData(data);
        var loc = game.TryOpen("Loc.esm");
        Assert.NotNull(loc);
        Assert.Equal("Loc.esm", loc!.ModKey.FileName.String);
        Assert.Equal("Keyword text", loc.Keywords.Single().Name?.String);
    }

    [Theory]
    [InlineData("loc.esm", "loc - Main.ba2")]    // plugin and archive both spelled differently from the request
    [InlineData("Loc.esm", "LOC - Main.ba2")]    // plugin exact, archive spelled differently
    public void ALocalizedPluginWithItsStringsInABa2KeepsThemInAnySpelling(string diskPlugin, string archive)
    {
        using var t = new TempDir();
        var data = t.Sub("Data");
        Fixtures.WriteBase(data);
        using (var writer = new GameData(data))
        {
            writer.Open("Base.esm");
            var mod = Plugin.New("Loc.esm", PluginStyle.Full, "test", "localized", localized: true);
            mod.Keywords.AddNew("L_Kw").Name = Localized.Text("Keyword text");
            PluginWriter.Write(mod, data, writer, Fixtures.NoGapCheck);
        }
        // Pack the loose string tables into a BA2, as a released mod ships them, under lower-case names, and remove
        // the loose copies so only the archive can supply the text.
        var strings = Path.Combine(data, "Strings");
        var tables = Directory.GetFiles(strings).ToDictionary(
            f => @"strings\" + Path.GetFileName(f).ToLowerInvariant(), File.ReadAllBytes);
        Assert.NotEmpty(tables);
        Directory.Delete(strings, true);
        Fixtures.WriteBa2(Path.Combine(data, archive), tables);
        if (diskPlugin != "Loc.esm")
        {
            File.Move(Path.Combine(data, "Loc.esm"), Path.Combine(data, "tmp.esm"));
            File.Move(Path.Combine(data, "tmp.esm"), Path.Combine(data, diskPlugin));
        }
        // Mutagen 0.54.4 reads Plugins.txt from %LocalAppData% to order archives, and throws when that variable is
        // unset or empty (Linux outside Proton). Always point it at an empty folder of the test's own, so the result
        // never depends on the account's real Plugins.txt, and put the original value back afterwards. The assembly
        // turns off test parallelization (BethesdaFormIdTests.cs), so no other test sees the change.
        var localAppData = Environment.GetEnvironmentVariable("LocalAppData");
        Environment.SetEnvironmentVariable("LocalAppData", t.Sub("LocalAppData"));
        try
        {
            using var game = new GameData(data);
            var loc = game.TryOpen("Loc.esm");
            Assert.NotNull(loc);
            Assert.Equal("Loc.esm", loc!.ModKey.FileName.String);
            Assert.Equal("Keyword text", loc.Keywords.Single().Name?.String);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LocalAppData", localAppData);   // an empty value comes back unset, which Mutagen treats the same
        }
    }

    [Fact]
    public void ALocalizedPluginWithALowerCaseStringsFolderKeepsItsStrings()
    {
        using var t = new TempDir();
        var data = t.Sub("Data");
        Fixtures.WriteBase(data);
        using (var writer = new GameData(data))
        {
            writer.Open("Base.esm");
            var mod = Plugin.New("Loc.esm", PluginStyle.Full, "test", "localized", localized: true);
            mod.Keywords.AddNew("L_Kw").Name = Localized.Text("Keyword text");
            PluginWriter.Write(mod, data, writer, Fixtures.NoGapCheck);
        }
        Directory.Move(Path.Combine(data, "Strings"), Path.Combine(data, "tmp"));
        Directory.Move(Path.Combine(data, "tmp"), Path.Combine(data, "strings"));
        File.WriteAllBytes(Path.Combine(data, "strings", "Loc_extra_en.STRINGS"), [1, 2, 3]);   // another plugin's table
        using var game = new GameData(data);
        var loc = game.TryOpen("Loc.esm");
        Assert.Equal("Keyword text", loc!.Keywords.Single().Name?.String);
    }

    [Fact]
    public void ADamagedPluginGivesTheSameErrorEveryTime()
    {
        using var t = new TempDir();
        File.WriteAllBytes(Path.Combine(t.Path, "Bad.esm"), "XXXX\0\0\0\0"u8.ToArray());
        using var game = new GameData(t.Path);
        Assert.Throws<InvalidDataException>(() => game.TryOpen("Bad.esm"));
        Assert.Throws<InvalidDataException>(() => game.TryOpen("Bad.esm"));   // not "is its own master"
    }

    [Fact]
    public void AnExactSpellingInDataWinsOverADifferentlySpelledExtraCopy()
    {
        using var t = new TempDir();
        var data = t.Sub("Data");
        var stage = t.Sub("Stage");
        Fixtures.WriteBase(data);
        Fixtures.WriteBase(stage);
        File.Move(Path.Combine(stage, "Base.esm"), Path.Combine(stage, "tmp.bin"));
        File.Move(Path.Combine(stage, "tmp.bin"), Path.Combine(stage, "base.esm"));
        using var game = new GameData(data, [stage]);
        var found = game.Locate("Base.esm")!;
        if (File.Exists(Path.Combine(stage, "Base.esm")))   // case-insensitive file system: the extra folder wins as before
            Assert.Equal(stage, Path.GetDirectoryName(found));
        else
            Assert.Equal(Path.Combine(data, "Base.esm"), found);
    }

    [Fact]
    public void AMissInAListingWithAnUnchangedStampListsTheFolderAgain()
    {
        using var t = new TempDir();
        Fixtures.WriteBase(t.Path);
        using var game = new GameData(t.Path);
        var stamp = Directory.GetLastWriteTimeUtc(t.Path);
        Assert.Null(game.Locate("Light.esm"));
        Fixtures.WriteLight(t.Path);
        File.Move(Path.Combine(t.Path, "Light.esm"), Path.Combine(t.Path, "tmp.bin"));
        File.Move(Path.Combine(t.Path, "tmp.bin"), Path.Combine(t.Path, "light.esm"));
        Directory.SetLastWriteTimeUtc(t.Path, stamp);      // as on a file system with a coarse folder time
        Assert.NotNull(game.Locate("Light.esm"));
    }

    [Fact]
    public void APluginWrittenAfterTheFirstLookupIsFound()
    {
        using var t = new TempDir();
        Fixtures.WriteBase(t.Path);
        using var game = new GameData(t.Path);
        Assert.Null(game.Locate("Light.esm"));             // the folder is listed here, before Light.esm exists
        Fixtures.WriteLight(t.Path);
        File.Move(Path.Combine(t.Path, "Light.esm"), Path.Combine(t.Path, "tmp.bin"));
        File.Move(Path.Combine(t.Path, "tmp.bin"), Path.Combine(t.Path, "light.esm"));
        Assert.NotNull(game.Locate("Light.esm"));
    }

    [Fact]
    public void PlainPluginNameRuleIsUnchanged()
    {
        Assert.True(GameData.IsPlainPluginName("Starfield.esm"));
        Assert.True(GameData.IsPlainPluginName("starfield.ESM"));
        Assert.False(GameData.IsPlainPluginName(@"\\server\share\x.esm"));
        Assert.False(GameData.IsPlainPluginName("../x.esm"));
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        Assert.Throws<ArgumentException>(() => game.Locate("Data/Starfield.esm"));
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
    public void TryFindToolGivesNullForAMissingFile() =>
        Assert.Null(PluginWriter.TryFindTool("no-such-buildkit-helper-7f3a.py"));
}

/// The gap check runs `sf-mod-check gap`. A stub script stands in for the exe. These tests change the process's
/// PATH and BUILDKIT_SFMODCHECK, so they share one collection and run one at a time.
[Collection("GapCheckEnv")]
public class GapCheckTests
{
    /// A stub "sf-mod-check" that records its arguments next to itself, prints one line and exits with exitCode.
    static string Stub(string dir, int exitCode)
    {
        Directory.CreateDirectory(dir);
        if (OperatingSystem.IsWindows())
        {
            var cmd = Path.Combine(dir, "sf-mod-check.cmd");
            File.WriteAllText(cmd, $"@echo off\r\necho %* > \"%~dp0args.txt\"\r\necho stub report\r\nexit /b {exitCode}\r\n");
            return cmd;
        }
        var sh = Path.Combine(dir, "sf-mod-check");
        File.WriteAllText(sh, $"#!/bin/sh\necho \"$@\" > \"$(dirname \"$0\")/args.txt\"\necho stub report\nexit {exitCode}\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    static string WriteWith(TempDir t, WriteOptions opts)
    {
        using var game = new GameData(t.Path);
        var mod = Plugin.New("Gap.esm", PluginStyle.Light, "test", "gap");
        return PluginWriter.Write(mod, t.Sub("out"), game, opts);
    }

    [Fact]
    public void RunsSfModCheckGapAndPassesThePluginAndDataFolder()
    {
        using var t = new TempDir();
        var stub = Stub(t.Sub("stub"), 0);
        var path = WriteWith(t, new WriteOptions { GapCheckExe = stub });
        var args = File.ReadAllText(Path.Combine(t.Path, "stub", "args.txt"));
        Assert.StartsWith("gap ", args);
        Assert.Contains("Gap.esm", args);
        Assert.Contains("--data", args);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void AFailingGapCheckThrowsAndNamesTheExitCode()
    {
        using var t = new TempDir();
        var stub = Stub(t.Sub("stub"), 1);
        var ex = Assert.Throws<Exception>(() => WriteWith(t, new WriteOptions { GapCheckExe = stub }));
        Assert.Contains("found errors", ex.Message);
    }

    [Fact]
    public void AGapCheckThatCouldNotCheckSaysSo()
    {
        using var t = new TempDir();
        var stub = Stub(t.Sub("stub"), 2);
        var ex = Assert.Throws<Exception>(() => WriteWith(t, new WriteOptions { GapCheckExe = stub }));
        Assert.Contains("could not check", ex.Message);
        Assert.Contains("exit 2", ex.Message);
    }

    [Fact]
    public void AFailingGapCheckIsOnlyReportedWhenFailOnGapErrorsIsOff()
    {
        using var t = new TempDir();
        var stub = Stub(t.Sub("stub"), 1);
        Assert.True(File.Exists(WriteWith(t, new WriteOptions { GapCheckExe = stub, FailOnGapErrors = false })));
    }

    [Fact]
    public void AMissingExplicitExeFailsClearly()
    {
        using var t = new TempDir();
        var opts = new WriteOptions { GapCheckExe = Path.Combine(t.Path, "nope.exe") };
        Assert.Throws<FileNotFoundException>(() => WriteWith(t, opts));
    }

    [Fact]
    public void TheVariableAndPathFindTheExe()
    {
        using var t = new TempDir();
        var stub = Stub(t.Sub("stub"), 0);
        var oldVar = Environment.GetEnvironmentVariable(PluginWriter.GapCheckExeVariable);
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable(PluginWriter.GapCheckExeVariable, stub);
            WriteWith(t, new WriteOptions());
            Assert.True(File.Exists(Path.Combine(t.Path, "stub", "args.txt")));

            // GapCheck called directly (no exe) reads the variable too, as the not-found message says.
            File.Delete(Path.Combine(t.Path, "stub", "args.txt"));
            Assert.Equal(0, PluginWriter.GapCheck(Path.Combine(t.Path, "out", "Gap.esm"), t.Path));
            Assert.True(File.Exists(Path.Combine(t.Path, "stub", "args.txt")));

            Environment.SetEnvironmentVariable(PluginWriter.GapCheckExeVariable, null);
            Environment.SetEnvironmentVariable("PATH", Path.Combine(t.Path, "stub"));
            if (OperatingSystem.IsWindows())
            {
                // The PATH search on Windows looks for sf-mod-check.exe; any file of that name is enough for the search.
                var exe = Path.Combine(t.Path, "stub", "sf-mod-check.exe");
                File.WriteAllText(exe, "");
                Environment.SetEnvironmentVariable("PATH", "\"" + Path.Combine(t.Path, "stub") + "\"");
                Assert.Equal(exe, PluginWriter.FindSfModCheck());
            }
            else Assert.Equal(stub, PluginWriter.FindSfModCheck());
        }
        finally
        {
            Environment.SetEnvironmentVariable(PluginWriter.GapCheckExeVariable, oldVar);
            Environment.SetEnvironmentVariable("PATH", oldPath);
        }
    }

    [Fact]
    public void ARelativeOrPythonPathIsRefused()
    {
        using var t = new TempDir();
        var oldVar = Environment.GetEnvironmentVariable(PluginWriter.GapCheckExeVariable);
        try
        {
            Environment.SetEnvironmentVariable(PluginWriter.GapCheckExeVariable, "sf-mod-check.exe");
            var ex = Assert.Throws<ArgumentException>(() => WriteWith(t, new WriteOptions()));
            Assert.Contains("full path", ex.Message);
            Assert.Contains(PluginWriter.GapCheckExeVariable, ex.Message);
        }
        finally { Environment.SetEnvironmentVariable(PluginWriter.GapCheckExeVariable, oldVar); }
        var py = Path.Combine(t.Sub("old"), "mutagen-gap-check.py");
        File.WriteAllText(py, "");
        var pyEx = Assert.Throws<ArgumentException>(() => WriteWith(t, new WriteOptions { GapCheckExe = py }));
        Assert.Contains("Python script", pyEx.Message);
        // A moved or deleted old script still gets the 0.5.0 hint, not a plain "does not exist".
        var gone = Path.Combine(t.Sub("old"), "gone", "mutagen-gap-check.py");
        Assert.Contains("Python script", Assert.Throws<ArgumentException>(() => PluginWriter.ResolveSfModCheck(gone)).Message);
        // A path pasted with quotes (Explorer's "Copy as path") in the option is read like the variable.
        var exe = Path.Combine(t.Sub("q x"), "sf-mod-check.exe");
        File.WriteAllText(exe, "");
        Assert.Equal(exe, PluginWriter.ResolveSfModCheck($" \"{exe}\" "));
    }

    [Fact]
    public void NotFoundSaysSoOnStderrAndTheBuildStillSucceeds()
    {
        using var t = new TempDir();
        var oldVar = Environment.GetEnvironmentVariable(PluginWriter.GapCheckExeVariable);
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        var oldErr = Console.Error;
        var err = new StringWriter();
        try
        {
            Environment.SetEnvironmentVariable(PluginWriter.GapCheckExeVariable, null);
            Environment.SetEnvironmentVariable("PATH", t.Sub("empty"));
            Console.SetError(err);
            Assert.True(File.Exists(WriteWith(t, new WriteOptions())));
        }
        finally
        {
            Console.SetError(oldErr);
            Environment.SetEnvironmentVariable(PluginWriter.GapCheckExeVariable, oldVar);
            Environment.SetEnvironmentVariable("PATH", oldPath);
        }
        var text = err.ToString();
        Assert.Contains("NOT RUN", text);
        Assert.Contains(PluginWriter.GapCheckExeVariable, text);
        Assert.Contains("dotnet tool install", text);
    }
}

public class MasterNameTests
{
    /// A minimal TES4 header with the given MAST names (enough for HeaderMasters).
    internal static void WriteHeader(string path, params string[] masters)
    {
        var body = new MemoryStream();
        var bw = new BinaryWriter(body);
        foreach (var m in masters)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(m + "\0");
            bw.Write("MAST"u8.ToArray()); bw.Write((ushort)bytes.Length); bw.Write(bytes);
        }
        using var f = new BinaryWriter(File.Create(path));
        f.Write("TES4"u8.ToArray()); f.Write((uint)body.Length); f.Write(new byte[16]); f.Write(body.ToArray());
    }

    [Theory]
    [InlineData("Starfield.esm", true)]
    [InlineData("My Mod-Patch.ESP", true)]
    [InlineData("Light.esl", true)]
    [InlineData(@"\\attacker.example\s\x.esm", false)]
    [InlineData("//attacker.example/s/x.esm", false)]
    [InlineData(@"..\..\x.esm", false)]
    [InlineData(@"C:\Games\x.esm", false)]
    [InlineData("C:x.esm", false)]
    [InlineData("x.txt", false)]
    [InlineData("", false)]
    [InlineData("bad\u001b[2Jname.esm", false)]
    public void PlainNames(string name, bool ok) => Assert.Equal(ok, GameData.IsPlainPluginName(name));

    [Fact]
    public void HeaderMastersRefusesANetworkPath()
    {
        using var t = new TempDir();
        var p = Path.Combine(t.Path, "Evil.esm");
        WriteHeader(p, "Starfield.esm", @"\\attacker.example\s\x.esm");
        var e = Assert.Throws<InvalidDataException>(() => GameData.HeaderMasters(p));
        Assert.Contains("not a plain plugin file name", e.Message);
    }

    [Fact]
    public void HeaderMastersKeepsPlainNames()
    {
        using var t = new TempDir();
        var p = Path.Combine(t.Path, "Ok.esm");
        WriteHeader(p, "Starfield.esm", "Base.esm");
        Assert.Equal(["Starfield.esm", "Base.esm"], GameData.HeaderMasters(p));
    }

    [Fact]
    public void LocateRefusesAPath()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        Assert.Throws<ArgumentException>(() => game.Locate(@"..\x.esm"));
        Assert.Null(game.Locate("Missing.esm"));
    }

    [Fact]
    public void OpenRefusesAPluginWithANetworkMaster()
    {
        using var t = new TempDir();
        WriteHeader(Path.Combine(t.Path, "Evil.esm"), @"\\attacker.example\s\x.esm");
        using var game = new GameData(t.Path);
        Assert.Throws<InvalidDataException>(() => game.TryOpen("Evil.esm"));
    }
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
