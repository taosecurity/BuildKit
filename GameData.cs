// Opens Starfield.esm and other masters from the game's Data folder, each with the master flags (light / medium /
// full) of the masters opened before it, so FormIDs in medium and light masters resolve correctly.
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Strings;

namespace BuildKit;

public sealed class GameData : IDisposable
{
    readonly List<IStarfieldModDisposableGetter> order = new();
    readonly Dictionary<string, IStarfieldModDisposableGetter> opened = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> opening = new(StringComparer.OrdinalIgnoreCase);
    ILinkCache? linkCache;
    Dictionary<string, IStarfieldMajorRecordGetter>? editorIds;

    public string DataDir { get; }

    /// <summary>The masters opened so far, in load order (a master's own masters come before it).</summary>
    public IReadOnlyList<IStarfieldModGetter> LoadOrder => order;

    /// <summary>Folders searched for masters before the Data folder (in this order), for example a staging folder.</summary>
    public IReadOnlyList<string> ExtraDirs { get; }

    public GameData(string dataDir, IEnumerable<string>? extraDirs = null)
    {
        if (!Directory.Exists(dataDir)) throw new DirectoryNotFoundException($"no Data folder {dataDir}");
        DataDir = dataDir;
        ExtraDirs = (extraDirs ?? []).Where(Directory.Exists).ToList();
    }

    readonly List<string> warnings = [];
    readonly HashSet<string> warned = new(StringComparer.Ordinal);

    /// <summary>Notes about the folders, such as two files whose names differ only in case (possible on Linux). Each
    /// note is also written once to standard error.</summary>
    public IReadOnlyList<string> Warnings => warnings;

    /// <summary>Where a plugin file is: the first extra folder that has it, else the Data folder; null if neither.
    /// The exact spelling is looked for in every folder first (so Windows behaves exactly as before, and an exact
    /// match in the Data folder wins over a differently spelled copy in an extra folder). Then the name is matched in
    /// any spelling, because on Linux and Proton the file system is case-sensitive while a load order or a plugin's
    /// master list may spell a name differently from the file. When a folder holds the exact spelling, that file is
    /// used even if another file there differs from it only in case.</summary>
    /// <exception cref="ArgumentException">The name isn't a plain plugin file name (see <see cref="IsPlainPluginName"/>).</exception>
    public string? Locate(string fileName)
    {
        if (!IsPlainPluginName(fileName))
            throw new ArgumentException($"\"{Printable(fileName)}\" is not a plain plugin file name (Name.esm, .esp or .esl, no folder)", nameof(fileName));
        var dirs = ExtraDirs.Append(DataDir).ToList();
        foreach (var dir in dirs)
        {
            var exact = Path.Combine(dir, fileName);
            if (File.Exists(exact)) return exact;
        }
        foreach (var dir in dirs)
            if (AnySpelling(dir, fileName) is string path) return path;
        return null;
    }

    // Folder listings for the any-spelling match, one per folder. A listing is made again when the folder's
    // modification time changes (adding, removing or renaming a file changes it). Some file systems (FAT, exFAT,
    // network shares) keep that time too coarsely, or cache it, so a miss also lists the folder again: at once while
    // the folder's time is recent (RecentChange), and otherwise at most once per RelistGap, so a long-unchanged folder
    // such as a big Data folder isn't listed again for every missing optional plugin in a loop.
    sealed record Listing(DateTime Stamp, long ListedAt, Dictionary<string, string> Files,
        Dictionary<string, List<string>> CaseTwins);
    readonly Dictionary<string, Listing> listings = new(StringComparer.Ordinal);

    /// <summary>How recent a folder's modification time must be for every miss to list it again. Wider than any file
    /// system's time resolution, with room for a network share's clock being a little off.</summary>
    static readonly TimeSpan RecentChange = TimeSpan.FromSeconds(60);

    /// <summary>For an older folder, the least time between two listings made for a miss.</summary>
    static readonly TimeSpan RelistGap = TimeSpan.FromSeconds(1);

    /// <summary>The path of a file in the folder whose name matches in any spelling; null if there is none.</summary>
    string? AnySpelling(string dir, string name)
    {
        DateTime stamp;
        try { stamp = Directory.GetLastWriteTimeUtc(dir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        var l = listings.GetValueOrDefault(dir);
        if (l != null && l.Stamp == stamp)
        {
            if (l.Files.TryGetValue(name, out var cached) && File.Exists(cached)) return Found(dir, name, l, cached);
            var cachedMiss = !l.Files.ContainsKey(name);
            if (cachedMiss && DateTime.UtcNow - stamp > RecentChange
                && System.Diagnostics.Stopwatch.GetElapsedTime(l.ListedAt) < RelistGap)
                return null;                                    // old folder listed just now: the miss stands
        }
        l = List(dir, stamp);                                   // new, changed, gone, or a miss in a recent folder
        return l != null && l.Files.TryGetValue(name, out var path) ? Found(dir, name, l, path) : null;
    }

    /// <summary>The found path, with a warning when another file in the folder differs from it only in case.</summary>
    string Found(string dir, string name, Listing l, string path)
    {
        if (l.CaseTwins.TryGetValue(name, out var twins))
            Warn($"{dir}: {Path.GetFileName(path)} and {string.Join(" and ", twins)} differ only in case; using {Path.GetFileName(path)}");
        return path;
    }

    /// <summary>Lists the folder and keeps the listing; null (and nothing kept, so the next lookup tries again) when
    /// the folder can't be listed.</summary>
    Listing? List(string dir, DateTime stamp)
    {
        Listing? l = null;
        try
        {
            if (Directory.Exists(dir))
            {
                l = new(stamp, System.Diagnostics.Stopwatch.GetTimestamp(), new(StringComparer.OrdinalIgnoreCase),
                    new(StringComparer.OrdinalIgnoreCase));
                // Sorted, so when two files differ only in case (possible on Linux) the same one wins on every run.
                foreach (var f in Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.Ordinal))
                {
                    var n = Path.GetFileName(f);
                    if (l.Files.TryAdd(n, f)) continue;
                    if (!l.CaseTwins.TryGetValue(n, out var twins)) l.CaseTwins[n] = twins = [];
                    twins.Add(n);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Warn($"{dir}: can't list the folder ({e.Message}); plugins are found only by their exact name");
            l = null;
        }
        if (l == null) listings.Remove(dir);
        else listings[dir] = l;
        return l;
    }

    void Warn(string w)
    {
        if (!warned.Add(w)) return;
        warnings.Add(w);
        Console.Error.WriteLine($"BuildKit: {w}");
    }

    /// <summary>True for a bare plugin file name such as <c>Starfield.esm</c>: no folder, drive, network path or "..",
    /// no characters Windows forbids in file names, and the extension .esm, .esp or .esl. Master names come from the
    /// plugin file itself, so a name like <c>\\server\share\x.esm</c> would otherwise make Windows contact that server.</summary>
    public static bool IsPlainPluginName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255) return false;
        if (name.IndexOfAny(['/', '\\', ':']) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        if (name.Any(char.IsControl) || name.StartsWith('.') || name.EndsWith(' ') || name.EndsWith('.')) return false;
        var ext = Path.GetExtension(name);
        return ext.Equals(".esm", StringComparison.OrdinalIgnoreCase) || ext.Equals(".esp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".esl", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A name with control characters shown as \xNN, safe to print to a terminal.</summary>
    public static string Printable(string s) =>
        string.Concat(s.Select(c => char.IsControl(c) ? $"\\x{(int)c:X2}" : c.ToString()));

    /// <summary>Opens a plugin from the Data folder (read-only overlay). Its own masters are opened first.</summary>
    public IStarfieldModGetter Open(string fileName) =>
        TryOpen(fileName) ?? throw new FileNotFoundException($"{fileName} is not in {DataDir}");

    /// <summary>As <see cref="Open"/>, but returns null when the plugin (or one of its masters) isn't installed.
    /// Masters opened before a missing or failing one stay open and in <see cref="LoadOrder"/>.</summary>
    public IStarfieldModGetter? TryOpen(string fileName)
    {
        if (opened.TryGetValue(fileName, out var m)) return m;
        var path = Locate(fileName);
        if (path == null) return null;
        if (!opening.Add(fileName)) throw new InvalidOperationException($"{fileName} is its own master");
        IStarfieldModDisposableGetter mod;
        try
        {
            foreach (var master in HeaderMasters(path))
                if (TryOpen(master) == null) return null;
            // The ModKey takes the requested spelling, not the file's, as it did when Windows opened the file by that
            // name, so master lists in built plugins don't depend on how a case-sensitive disk spells the file.
            mod = StarfieldMod.CreateFromBinaryOverlay(new ModPath(ModKey.FromFileName(fileName), path),
                StarfieldRelease.Starfield, ReadParams(StringsUnderRequestedName(path, fileName)));
        }
        finally { opening.Remove(fileName); }
        order.Add(mod);
        opened[fileName] = mod;
        linkCache = null;
        editorIds = null;
        return mod;
    }

    string? stringsCopies;

    int stringsCalls;

    static readonly string[] TableExtensions = [".strings", ".dlstrings", ".ilstrings"];

    /// <summary>For a plugin whose name or Strings folder is spelled differently on disk (only possible on a
    /// case-sensitive disk): Mutagen looks for loose string tables as <c>Strings\&lt;ModKey name&gt;_lang.EXT</c>,
    /// with the requested spelling, so they'd be missed. This copies the plugin's own tables (<c>name_lang.EXT</c>,
    /// any spelling, from a Strings folder of any spelling) into a fresh private temporary folder under the requested
    /// name and returns that folder; null when the exact names are in place or there are no loose tables. The
    /// folders are deleted on Dispose. Tables inside a BA2 archive need nothing here: Mutagen matches the archive's
    /// name and the table names inside it without regard to case (tested on Linux). Known limit: when the plugin and
    /// the Strings folder are spelled exactly, differently spelled table files aren't looked for.</summary>
    string? StringsUnderRequestedName(string path, string fileName)
    {
        var diskStem = Path.GetFileNameWithoutExtension(path);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var pluginDir = Path.GetDirectoryName(path)!;
        if (diskStem == stem && Directory.Exists(Path.Combine(pluginDir, "Strings"))) return null;
        try
        {
            var strings = Directory.EnumerateDirectories(pluginDir)
                .Where(d => string.Equals(Path.GetFileName(d), "Strings", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal).FirstOrDefault();
            if (strings == null) return null;
            // Only this plugin's own tables: name_lang.EXT with no further "_" part, so another plugin's tables
            // (name_extra_lang.EXT, for a plugin called name_extra) aren't copied.
            var tables = Directory.EnumerateFiles(strings)
                .Select(f => (File: f, Tail: Path.GetFileName(f).StartsWith(diskStem + "_", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(f)[(diskStem.Length + 1)..] : null))
                .Where(t => t.Tail != null && TableExtensions.Contains(Path.GetExtension(t.Tail), StringComparer.OrdinalIgnoreCase)
                    && Path.GetFileNameWithoutExtension(t.Tail) is { Length: > 0 } lang && !lang.Contains('_') && !lang.Contains('.'))
                .OrderBy(t => t.File, StringComparer.Ordinal).ToList();
            if (tables.Count == 0) return null;
            stringsCopies ??= Directory.CreateTempSubdirectory("buildkit-strings-").FullName;
            var dir = Path.Combine(stringsCopies, (stringsCalls++).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dir);
            foreach (var (f, rest) in tables)
            {
                // Name_lang.EXT, with the language in lower case and the extension in upper case, as Mutagen writes them.
                var target = Path.Combine(dir,
                    $"{stem}_{Path.GetFileNameWithoutExtension(rest)!.ToLowerInvariant()}{Path.GetExtension(rest)!.ToUpperInvariant()}");
                if (!File.Exists(target)) File.Copy(f, target);   // first in sorted order wins (the folder is new)
            }
            return dir;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Warn($"{path}: can't copy its string tables under the name {fileName} ({e.Message}); its text may read as empty");
            return null;
        }
    }

    /// <summary>Read settings for a plugin whose masters are all open: their master flags, and optionally a Strings
    /// folder (for reading back a localized plugin we just wrote).</summary>
    public BinaryReadParameters ReadParams(string? stringsDir = null) => new()
    {
        MasterFlagsLookup = new LoadOrder<IModMasterStyledGetter>(order),
        StringsParam = stringsDir == null ? null : new StringsReadParameters { StringsFolderOverride = stringsDir },
    };

    /// <summary>Opens another plugin (for example the one just built) against the open masters. Not added to the
    /// load order; the caller disposes it.</summary>
    public IStarfieldModDisposableGetter OpenPlugin(string path, string? stringsDir = null) =>
        StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, ReadParams(stringsDir));

    /// <summary>Master flags for writing (same information the read side uses).</summary>
    public LoadOrder<IModMasterStyledGetter> MasterStyles => new(order);

    /// <summary>Link cache over the open masters.</summary>
    public ILinkCache LinkCache => linkCache ??= order.ToImmutableLinkCache();

    /// <summary>EditorID → record over the open masters; a later master's record wins over an earlier one's.</summary>
    public IReadOnlyDictionary<string, IStarfieldMajorRecordGetter> EditorIds
    {
        get
        {
            if (editorIds != null) return editorIds;
            editorIds = new(StringComparer.OrdinalIgnoreCase);
            for (var i = order.Count - 1; i >= 0; i--)
                foreach (var r in order[i].EnumerateMajorRecords<IStarfieldMajorRecordGetter>())
                    if (r.EditorID != null) editorIds.TryAdd(r.EditorID, r);
            return editorIds;
        }
    }

    /// <summary>The master record with this EditorID, which must be a T.</summary>
    public T Get<T>(string editorId) where T : class, IStarfieldMajorRecordGetter =>
        EditorIds.TryGetValue(editorId, out var r)
            ? r as T ?? throw new Exception($"{editorId} is a {r.GetType().Name}, not {typeof(T).Name}")
            : throw new Exception($"no record {editorId} in {string.Join(" or ", order.Select(m => m.ModKey.FileName))}");

    /// <summary>The master file names (MAST) listed in a plugin's header, read directly from the file. Throws
    /// InvalidDataException when a master name isn't a plain plugin file name (<see cref="IsPlainPluginName"/>).</summary>
    public static List<string> HeaderMasters(string path)
    {
        var list = HeaderMastersRaw(path);
        foreach (var m in list)
            if (!IsPlainPluginName(m))
                throw new InvalidDataException($"{path} lists a master \"{Printable(m)}\" that is not a plain plugin file name " +
                    "(Name.esm, .esp or .esl with no folder, drive or network path). The plugin is damaged or unsafe; don't load it.");
        return list;
    }

    static List<string> HeaderMastersRaw(string path)
    {
        using var s = File.OpenRead(path);
        var br = new BinaryReader(s);
        if (new string(br.ReadChars(4)) != "TES4") throw new InvalidDataException($"{path} is not a plugin");
        var size = br.ReadUInt32();
        br.ReadBytes(16);
        var end = s.Position + size;
        var list = new List<string>();
        while (s.Position < end)
        {
            var type = new string(br.ReadChars(4));
            var len = br.ReadUInt16();
            var data = br.ReadBytes(len);
            if (type == "MAST") list.Add(System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0'));
        }
        return list;
    }

    public void Dispose()
    {
        foreach (var m in order) m.Dispose();
        order.Clear();
        opened.Clear();
        if (stringsCopies != null)
        {
            try { Directory.Delete(stringsCopies, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            stringsCopies = null;
        }
    }
}
