// Writes a plugin the way a Starfield builder should: EFIF/MUID filled on every effect, SNAM on every dialogue topic, ONAM filled like the Creation
// Kit (OverriddenFormsOption.Iterate; Mutagen 0.54.4 defaults to none, and the game then ignores edits of vanilla
// temporary references), master flags from the open masters, masters listed in load order, one-thread writing,
// light and medium plugins' own records in Bethesda's FE/FD FormID form, and then (when the script is present) the
// Mutagen gap check on the written file.
using System.Diagnostics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;

namespace BuildKit;

public sealed class WriteOptions
{
    /// <summary>Master order in the header. Null = the order GameData opened them (Starfield.esm first).</summary>
    public IEnumerable<ModKey>? MasterOrder { get; init; }
    /// <summary>Null = Mutagen's default (the masters the records point to). NoCheck = write the header's
    /// MasterReferences exactly as the builder filled them, for patches that list extra masters only to force their
    /// place in the load order.</summary>
    public MastersListContentOption? MastersListContent { get; init; }
    /// <summary>An explicit master list, in this order. When set, the header's MasterReferences are replaced with
    /// exactly these masters and written as they are (MasterOrder = this list, MastersListContent = NoCheck), even
    /// if no record points into some of them.</summary>
    public IEnumerable<ModKey>? Masters { get; init; }
    /// <summary>Null = Mutagen's default (Iterate: the header's Next Object ID is set past the highest own FormID).
    /// NoCheck = write the Next Object ID the builder (or IdLock) put in the header.</summary>
    public NextFormIDOption? NextFormId { get; init; }
    /// <summary>Iterate = ONAM like the Creation Kit. Change only with a reason written next to it.</summary>
    public OverriddenFormsOption OverriddenForms { get; init; } = OverriddenFormsOption.Iterate;
    /// <summary>Write on one thread so the same build gives the same bytes (string IDs and tables included).</summary>
    public bool Deterministic { get; init; } = true;
    /// <summary>Fill EFIF/MUID on every effect entry before writing (Effects.FixAll).</summary>
    public bool FixEffects { get; init; } = true;
    /// <summary>Fill a blank SubtypeName (SNAM) on every dialogue topic from its Subtype before writing
    /// (DialogTopics.FixSubtypeNames).</summary>
    public bool FixDialogSubtypes { get; init; } = true;
    /// <summary>Run `sf-mod-check gap` on the written plugin (the exe named by GapCheckExe, else by the
    /// BUILDKIT_SFMODCHECK variable, else the first sf-mod-check on PATH). If it can't be found, one line on stderr
    /// says so and names the fix; the build still succeeds.</summary>
    public bool GapCheck { get; init; } = true;
    /// <summary>Full path of the sf-mod-check exe, if it isn't on PATH and BUILDKIT_SFMODCHECK isn't set.</summary>
    public string? GapCheckExe { get; init; }
    /// <summary>Extra folder the gap check looks in for masters before the Data folder (for example the build output).</summary>
    public string? GapCheckMastersDir { get; init; }
    /// <summary>Throw when the gap check exits nonzero: 1 = it found an ERROR, any other code = it could not check
    /// (a missing master, or an sf-mod-check older than 0.15.0 with no gap command).</summary>
    public bool FailOnGapErrors { get; init; } = true;
    /// <summary>Write the plugin's own new records the way Bethesda's own Starfield plugins store them: a light
    /// plugin's as FE + its index among light masters (FE000800 when it has no light masters), a medium plugin's as
    /// FD + its index among medium masters. Off = Mutagen 0.54.4's form for non-Bethesda plugins (the count of full
    /// masters, e.g. 01000800), which xEdit 4.1.5r reports as a HITME. Full plugins are written the same either way,
    /// and the game loads both forms, so in-game FormIDs and saves don't change.</summary>
    public bool BethesdaFormIds { get; init; } = true;
    /// <summary>Set the header's INCC (Interior Cell Count) to the plugin's interior cells, as every Bethesda plugin
    /// has it (xEdit marks it required).</summary>
    public bool InteriorCellCount { get; init; } = true;
    /// <summary>Fill empty fields Bethesda always writes on the plugin's own records (BethesdaFields.Fill: message
    /// back-out index, form list add-to-list, model light layer on Aid items and activators, keyword color/FNAM,
    /// story-manager quest node limit).</summary>
    public bool FillBethesdaFields { get; init; } = true;
    /// <summary>Raise own records still at Mutagen's default form version (576) to the Creation Kit's 582.</summary>
    public bool CkFormVersion { get; init; } = true;
    /// <summary>Compress the plugin's own NPC records, as Bethesda's are.</summary>
    public bool CompressNpcs { get; init; } = true;
    /// <summary>Zero the version-control words in every group header, as in Bethesda's own plugins
    /// (BethesdaFields.ZeroGroupStamps). Matters for rebuilds that copy another author's Creation Kit groups.</summary>
    public bool ZeroGroupStamps { get; init; } = true;
}

/// <summary>Turns on Bethesda's own-record FormID form for one plugin (see WriteOptions.BethesdaFormIds).
/// Mutagen 0.54.4 does this only for a fixed list of Bethesda's plugins (SeparatedMasterPackage._starfieldMasters,
/// marked "Hack until this is better understood"); every other plugin gets the full-master form. Adding the plugin
/// to that list makes Mutagen use the plugin's own light or medium style for its new records and for references
/// to them. It is a private field, so a Mutagen version that moves it stops here with a clear message.</summary>
public static class BethesdaFormIds
{
    const string FieldName = "_starfieldMasters";
    static readonly object Gate = new();
    static readonly HashSet<ModKey> AddedByUs = new();

    /// <summary>Use Bethesda's form for modKey in this process (writing and reading it back). Safe to call twice.</summary>
    public static void Enable(ModKey modKey)
    {
        lock (Gate) { if (Set().Add(modKey)) AddedByUs.Add(modKey); }
    }

    /// <summary>Go back to Mutagen's own form for modKey. Bethesda's own plugins, which Mutagen lists itself, stay.</summary>
    public static void Disable(ModKey modKey)
    {
        lock (Gate) { if (AddedByUs.Remove(modKey)) Set().Remove(modKey); }
    }

    /// <summary>True when modKey is written in Bethesda's form (Bethesda's own plugins always are).</summary>
    public static bool IsEnabled(ModKey modKey)
    {
        lock (Gate) return Set().Contains(modKey);
    }

    static HashSet<ModKey> Set()
    {
        var field = typeof(Mutagen.Bethesda.Plugins.Masters.SeparatedMasterPackage)
            .GetField(FieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (field?.GetValue(null) is HashSet<ModKey> set) return set;
        throw new InvalidOperationException(
            $"BuildKit: Mutagen's SeparatedMasterPackage.{FieldName} was not found, so Bethesda-form FormIDs can't be " +
            "turned on with this Mutagen version (BuildKit is built for Mutagen 0.54.4). Use the Mutagen version BuildKit " +
            "pins, or set WriteOptions.BethesdaFormIds = false.");
    }
}

public static class PluginWriter
{
    /// <summary>The gap check's program name (sf-mod-check, its "gap" command).</summary>
    public const string GapCheckExeName = "sf-mod-check";
    /// <summary>Environment variable that names the sf-mod-check exe.</summary>
    public const string GapCheckExeVariable = "BUILDKIT_SFMODCHECK";

    /// <summary>Writes mod to outDir\FileName and returns the path.</summary>
    public static string Write(StarfieldMod mod, string outDir, GameData game, WriteOptions? options = null)
    {
        options ??= new WriteOptions();
        if (options.FixEffects)
        {
            var fixedCount = Effects.FixAll(mod);
            if (fixedCount > 0) Console.WriteLine($"  BuildKit: EFIF/MUID filled on {fixedCount} effect entries");
        }
        if (options.FixDialogSubtypes)
        {
            var (fixedTopics, unmatched) = DialogTopics.FixSubtypeNames(mod);
            if (fixedTopics > 0) Console.WriteLine($"  BuildKit: SNAM filled on {fixedTopics} dialogue topics");
            if (unmatched > 0) Console.WriteLine($"  BuildKit: {unmatched} dialogue topics keep a blank SNAM (no code matches their subtype)");
        }
        if (options.FillBethesdaFields)
        {
            var filled = BethesdaFields.Fill(mod);
            if (filled.Total > 0) Console.WriteLine($"  BuildKit: Bethesda fields filled: {filled}");
        }
        if (options.CkFormVersion)
        {
            var raised = BethesdaFields.RaiseFormVersions(mod);
            if (raised > 0) Console.WriteLine($"  BuildKit: form version {Plugin.CkFormVersion} set on {raised} own records left at Mutagen's {BethesdaFields.MutagenDefaultFormVersion}");
        }
        if (options.CompressNpcs)
        {
            var compressed = BethesdaFields.CompressNpcs(mod);
            if (compressed > 0) Console.WriteLine($"  BuildKit: {compressed} own NPC records marked compressed");
        }
        if (options.ZeroGroupStamps)
        {
            var zeroed = BethesdaFields.ZeroGroupStamps(mod);
            if (zeroed > 0) Console.WriteLine($"  BuildKit: version-control words zeroed on {zeroed} group headers");
        }
        if (options.InteriorCellCount)
        {
            var before = mod.ModHeader.InteriorCellCount;
            mod.ModHeader.InteriorCellCount = BethesdaFields.InteriorCells(mod);
            if (before != mod.ModHeader.InteriorCellCount)
                Console.WriteLine($"  BuildKit: header interior cell count (INCC) set to {mod.ModHeader.InteriorCellCount} (was {before?.ToString() ?? "unset"})");
        }
        var masterOrder = options.MasterOrder;
        var mastersContent = options.MastersListContent;
        if (options.Masters != null)
        {
            var masters = options.Masters.ToList();
            mod.ModHeader.MasterReferences.Clear();
            foreach (var m in masters) mod.ModHeader.MasterReferences.Add(new MasterReference { Master = m });
            masterOrder = masters;
            mastersContent = MastersListContentOption.NoCheck;
        }
        var style = Plugin.StyleOf(mod);
        if (options.BethesdaFormIds)
        {
            BethesdaFormIds.Enable(mod.ModKey);
            if (style != PluginStyle.Full)
                Console.WriteLine($"  BuildKit: own records written in Bethesda's {(style == PluginStyle.Light ? "FE" : "FD")} FormID form ({style.ToString().ToLowerInvariant()} plugin)");
        }
        else
        {
            BethesdaFormIds.Disable(mod.ModKey);
            if (style != PluginStyle.Full)
                Console.WriteLine($"  BuildKit: own records written in Mutagen's full-master FormID form (BethesdaFormIds off)");
        }
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, mod.ModKey.FileName);
        var defaults = new BinaryWriteParameters();
        mod.WriteToBinary(outPath, new BinaryWriteParameters
        {
            MasterFlagsLookup = game.MasterStyles,
            MastersListContent = mastersContent ?? defaults.MastersListContent,
            MastersListOrdering = new MastersListOrderingByLoadOrder(masterOrder ?? game.LoadOrder.Select(m => m.ModKey)),
            NextFormID = options.NextFormId ?? defaults.NextFormID,
            OverriddenFormsOption = options.OverriddenForms,
            // One thread: Mutagen 0.54.4 writes groups in parallel and numbers localized strings as threads reach
            // them, so the same builder gave different string IDs and string-table layouts from run to run (a
            // localized plugin: 2 plugin and 4 string-table variants in 6 runs). One thread = the same bytes every time.
            Parallel = options.Deterministic ? new ParallelWriteParameters { MaxDegreeOfParallelism = 1 } : new ParallelWriteParameters(),
        });
        if (options.GapCheck)
        {
            var exe = ResolveSfModCheck(options.GapCheckExe, "WriteOptions.GapCheckExe");
            if (exe == null)
                Console.Error.WriteLine($"  gap check: NOT RUN ({GapCheckExeName} was not found). Install it (dotnet tool install -g {GapCheckExeName}, or the release from github.com/taosecurity/sf-mod-check) so it is on PATH, or set {GapCheckExeVariable} or WriteOptions.GapCheckExe to its path.");
            else
            {
                var exit = GapCheck(outPath, game.DataDir, options.GapCheckMastersDir, exe);
                if (exit != 0 && options.FailOnGapErrors)
                    throw new Exception(exit == 1
                        ? $"{GapCheckExeName} gap found errors in {outPath} (see above); fix them before handing the plugin over"
                        : $"{GapCheckExeName} gap could not check {outPath} (exit {exit}, see above): a missing master, or an {GapCheckExeName} older than 0.15.0 with no gap command?");
            }
        }
        return outPath;
    }

    /// <summary>Runs `sf-mod-check gap` on a plugin; prints its report and returns its exit code (0 ok, 1 errors,
    /// 2 could not check). exe = the full path of the sf-mod-check program (null = the BUILDKIT_SFMODCHECK variable, else
    /// PATH, as <see cref="ResolveSfModCheck"/>; throws when none is found).</summary>
    public static int GapCheck(string pluginPath, string dataDir, string? mastersDir = null, string? exe = null)
    {
        exe = ResolveSfModCheck(exe, "exe") ?? throw new FileNotFoundException(
            $"{GapCheckExeName} was not found on PATH. Install it (dotnet tool install -g {GapCheckExeName}), or set {GapCheckExeVariable} to its full path");
        Console.WriteLine($"  gap check: running {exe} gap");
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "gap", Path.GetFullPath(pluginPath), "--data", dataDir }) psi.ArgumentList.Add(a);
        if (mastersDir != null) { psi.ArgumentList.Add("--masters-dir"); psi.ArgumentList.Add(mastersDir); }
        using var p = Process.Start(psi) ?? throw new Exception($"could not start {exe}");
        var err = p.StandardError.ReadToEndAsync();
        string? line;
        while ((line = p.StandardOutput.ReadLine()) != null) Console.WriteLine($"  gap check: {line}");
        p.WaitForExit();
        foreach (var l in err.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Console.Error.WriteLine($"  gap check: {l.TrimEnd()}");
        return p.ExitCode;
    }

    /// <summary>The sf-mod-check program the gap check runs: exe if given, else the BUILDKIT_SFMODCHECK variable, else
    /// <see cref="FindSfModCheck"/> (null when PATH has none). A named path must be a full path to an existing program
    /// (not a Python script, which BuildKit no longer runs), or this throws naming the fix. optionName is what the
    /// message calls exe.</summary>
    public static string? ResolveSfModCheck(string? exe, string optionName = "exe")
    {
        var source = optionName;
        if (exe == null)
        {
            var env = Environment.GetEnvironmentVariable(GapCheckExeVariable);
            if (string.IsNullOrWhiteSpace(env)) return FindSfModCheck();
            (exe, source) = (env, GapCheckExeVariable);
        }
        exe = exe.Trim().Trim('"').Trim();
        if (Path.GetExtension(exe).Equals(".py", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{source} names the Python script {exe}: BuildKit 0.5.0 runs {GapCheckExeName} instead; name its exe");
        if (!Path.IsPathFullyQualified(exe))
            throw new ArgumentException($"{source} is {exe}: give the full path of {GapCheckExeName} (a relative path would run whatever is in the current folder)");
        if (!File.Exists(exe))
            throw new FileNotFoundException($"{source} names {exe}, which does not exist");
        return exe;
    }

    /// <summary>The full path of the first sf-mod-check (sf-mod-check.exe on Windows) on PATH, or null. Searching PATH
    /// ourselves, and skipping relative entries, keeps an sf-mod-check in the current folder or the builder's folder
    /// from being run, as Windows would for a bare name.</summary>
    public static string? FindSfModCheck()
    {
        var name = OperatingSystem.IsWindows() ? GapCheckExeName + ".exe" : GapCheckExeName;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var d = dir.Trim().Trim('"');
            if (d.Length == 0 || !Path.IsPathFullyQualified(d)) continue;
            var p = Path.Combine(d, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>Finds a helper file: in BUILDKIT_TOOLS if set, else in the first "tools" folder holding it above the
    /// running builder or the current folder, stopping below the drive root (any user can create C:\tools, so a
    /// drive-root tools folder is never trusted). Null if it's in neither.</summary>
    public static string? TryFindTool(string name)
    {
        var env = Environment.GetEnvironmentVariable("BUILDKIT_TOOLS");
        if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, name))) return Path.Combine(env, name);
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var d = new DirectoryInfo(start); d?.Parent != null; d = d.Parent)
            {
                var candidate = Path.Combine(d.FullName, "tools", name);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }

    /// <summary>As <see cref="TryFindTool"/>, but throws when the file isn't found.</summary>
    public static string FindTool(string name) =>
        TryFindTool(name) ?? throw new FileNotFoundException($"tools\\{name} not found above {AppContext.BaseDirectory}; set BUILDKIT_TOOLS");
}
