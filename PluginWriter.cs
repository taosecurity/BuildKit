// Writes a plugin the way a Starfield builder should: EFIF/MUID filled on every effect, ONAM filled like the Creation
// Kit (OverriddenFormsOption.Iterate; Mutagen 0.54.4 defaults to none, and the game then ignores edits of vanilla
// temporary references), master flags from the open masters, masters listed in load order, one-thread writing, and
// then (when the script is present) the Mutagen gap check on the written file.
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
    /// <summary>Run mutagen-gap-check.py on the written plugin when the script can be found (GapCheckScript, else
    /// BUILDKIT_TOOLS, else a "tools" folder above the builder). If it can't be found, one line says it was skipped.</summary>
    public bool GapCheck { get; init; } = true;
    /// <summary>Full path of mutagen-gap-check.py, if it isn't in BUILDKIT_TOOLS or a "tools" folder above.</summary>
    public string? GapCheckScript { get; init; }
    /// <summary>Extra folder the gap check looks in for masters before the Data folder (for example the build output).</summary>
    public string? GapCheckMastersDir { get; init; }
    /// <summary>Throw when the gap check reports an ERROR (its exit code 1).</summary>
    public bool FailOnGapErrors { get; init; } = true;
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
