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

    /// <summary>Where a plugin file is: the first extra folder that has it, else the Data folder; null if neither.</summary>
    public string? Locate(string fileName) =>
        ExtraDirs.Append(DataDir).Select(d => Path.Combine(d, fileName)).FirstOrDefault(File.Exists);

    /// <summary>Opens a plugin from the Data folder (read-only overlay). Its own masters are opened first.</summary>
    public IStarfieldModGetter Open(string fileName) =>
        TryOpen(fileName) ?? throw new FileNotFoundException($"{fileName} is not in {DataDir}");

    /// <summary>As <see cref="Open"/>, but returns null when the plugin (or one of its masters) isn't installed.</summary>
    public IStarfieldModGetter? TryOpen(string fileName)
    {
        if (opened.TryGetValue(fileName, out var m)) return m;
        var path = Locate(fileName);
        if (path == null) return null;
        if (!opening.Add(fileName)) throw new InvalidOperationException($"{fileName} is its own master");
        foreach (var master in HeaderMasters(path))
            if (TryOpen(master) == null) { opening.Remove(fileName); return null; }
        var mod = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, ReadParams());
        opening.Remove(fileName);
        order.Add(mod);
        opened[fileName] = mod;
        linkCache = null;
        editorIds = null;
        return mod;
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

    /// <summary>The master file names (MAST) listed in a plugin's header, read directly from the file.</summary>
    public static List<string> HeaderMasters(string path)
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
    }
}
