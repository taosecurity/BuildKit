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
    /// <summary>Run mutagen-gap-check.py on the written plugin when the script can be found (GapCheckScript, else
    /// BUILDKIT_TOOLS, else a "tools" folder above the builder). If it can't be found, one line says it was skipped.</summary>
    public bool GapCheck { get; init; } = true;
    /// <summary>Full path of mutagen-gap-check.py, if it isn't in BUILDKIT_TOOLS or a "tools" folder above.</summary>
    public string? GapCheckScript { get; init; }
    /// <summary>Extra folder the gap check looks in for masters before the Data folder (for example the build output).</summary>
    public string? GapCheckMastersDir { get; init; }
    /// <summary>Throw when the gap check reports an ERROR (its exit code 1).</summary>
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
    /// <summary>The gap check's file name.</summary>
    public const string GapCheckScriptName = "mutagen-gap-check.py";

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
            if (options.GapCheckScript != null && !File.Exists(options.GapCheckScript))
                throw new FileNotFoundException($"WriteOptions.GapCheckScript {options.GapCheckScript} does not exist");
            var script = options.GapCheckScript ?? TryFindTool(GapCheckScriptName);
            if (script == null)
                Console.WriteLine($"  gap check: skipped ({GapCheckScriptName} not found; set BUILDKIT_TOOLS or WriteOptions.GapCheckScript)");
            else
            {
                var exit = GapCheck(outPath, game.DataDir, options.GapCheckMastersDir, script);
                if (exit != 0 && options.FailOnGapErrors)
                    throw new Exception($"mutagen-gap-check found errors in {outPath} (see above); fix them before handing the plugin over");
            }
        }
        return outPath;
    }

    /// <summary>Runs python -I mutagen-gap-check.py on a plugin; prints its report and returns its exit code.
    /// script = the script's path (null = find it as <see cref="FindTool"/> does).</summary>
    public static int GapCheck(string pluginPath, string dataDir, string? mastersDir = null, string? script = null)
    {
        script ??= FindTool(GapCheckScriptName);
        var psi = new ProcessStartInfo("python") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-I", script, Path.GetFullPath(pluginPath), "--data", dataDir }) psi.ArgumentList.Add(a);
        if (mastersDir != null) { psi.ArgumentList.Add("--masters-dir"); psi.ArgumentList.Add(mastersDir); }
        using var p = Process.Start(psi) ?? throw new Exception("could not start python");
        var err = p.StandardError.ReadToEndAsync();
        string? line;
        while ((line = p.StandardOutput.ReadLine()) != null) Console.WriteLine($"  gap check: {line}");
        p.WaitForExit();
        foreach (var l in err.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Console.Error.WriteLine($"  gap check: {l.TrimEnd()}");
        return p.ExitCode;
    }

    /// <summary>Finds a helper file: in BUILDKIT_TOOLS if set, else in the first "tools" folder holding it above the
    /// running builder or the current folder. Null if it's in neither.</summary>
    public static string? TryFindTool(string name)
    {
        var env = Environment.GetEnvironmentVariable("BUILDKIT_TOOLS");
        if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, name))) return Path.Combine(env, name);
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
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
