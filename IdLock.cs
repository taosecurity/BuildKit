// Stable FormIDs between builds, kept in an ids.lock.json file committed next to the builder.
// The lock file maps every own record's EditorID to its FormID (hex). Locked records get their locked ID back, new
// records keep the ID Mutagen gave them when it's free (otherwise the next free ID) and are added to the lock. The
// build stops if a locked record disappears (another plugin or a save may still link to it), if two records share an
// EditorID, or if an own record has none. Call it after every record exists and before writing.
using System.Text.Json;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;

namespace BuildKit;

public sealed record IdLockResult(int Locked, int Added, int Moved, string LockPath)
{
    public override string ToString() => $"ID lock: {Locked} records, {Added} new, {Moved} moved back to their locked IDs ({LockPath})";
}

public static class IdLock
{
    /// <summary>update = false only checks: it throws if the build would add or move anything, and writes nothing.</summary>
    public static IdLockResult Apply(StarfieldMod mod, string lockPath, bool update = true)
    {
        var (first, last) = Plugin.IdRange(Plugin.StyleOf(mod));
        var own = mod.EnumerateMajorRecords().Where(r => r.FormKey.ModKey == mod.ModKey).ToList();
        var noEdid = own.Where(r => string.IsNullOrEmpty(r.EditorID)).Select(r => r.FormKey.ToString()).ToList();
        if (noEdid.Count > 0) throw new Exception($"records without EditorID can't be locked: {string.Join(", ", noEdid)}");
        var dupEdids = own.GroupBy(r => r.EditorID!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupEdids.Count > 0) throw new Exception($"duplicate EditorIDs: {string.Join(", ", dupEdids)}");

        var locked = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(lockPath))
            foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(lockPath))!)
                locked[kv.Key] = Convert.ToUInt32(kv.Value, 16);
        var gone = locked.Keys.Where(k => !own.Any(r => string.Equals(r.EditorID, k, StringComparison.OrdinalIgnoreCase))).ToList();
        if (gone.Count > 0) throw new Exception($"locked records missing from the build: {string.Join(", ", gone)}");
        var taken = locked.Values.ToHashSet();
        if (taken.Count != locked.Count) throw new Exception($"{lockPath} gives two records the same FormID");
        var outside = locked.Where(kv => kv.Value < first || kv.Value > last).Select(kv => $"{kv.Key}={kv.Value:X}").ToList();
        if (outside.Count > 0) throw new Exception($"locked IDs outside {first:X}-{last:X}: {string.Join(", ", outside)}");

        var target = new Dictionary<FormKey, uint>();
        uint free = first;
        var added = 0;
        // A new record keeps the ID Mutagen gave it when no locked record owns that ID (the first run locks every ID as is).
        foreach (var r in own.Where(r => !locked.ContainsKey(r.EditorID!)).OrderBy(r => r.FormKey.ID))
            if (r.FormKey.ID >= first && r.FormKey.ID <= last && taken.Add(r.FormKey.ID))
            {
                locked[r.EditorID!] = r.FormKey.ID;
                added++;
            }
        foreach (var r in own.OrderBy(r => r.FormKey.ID))
        {
            if (!locked.TryGetValue(r.EditorID!, out var id))
            {
                while (taken.Contains(free)) free++;
                if (free > last) throw new Exception($"no free FormIDs left in {first:X}-{last:X}");
                id = free;
                taken.Add(id);
                locked[r.EditorID!] = id;
                added++;
            }
            target[r.FormKey] = id;
        }
        var moves = own.Where(r => target[r.FormKey] != r.FormKey.ID).ToList();
        if (!update)
        {
            if (added > 0 || moves.Count > 0)
                throw new Exception($"{lockPath}: the build would add {added} and move {moves.Count} records ({string.Join(", ", moves.Select(m => m.EditorID).Take(10))})");
            return new IdLockResult(locked.Count, 0, 0, lockPath);
        }
        if (moves.Count > 0)
        {
            var remap = moves.ToDictionary(r => r.FormKey, r => new FormKey(mod.ModKey, target[r.FormKey]));
            var copies = moves.Select(r => ((IMajorRecord)r.Duplicate(remap[r.FormKey]), r.Registration.GetterType)).ToList();
            foreach (var r in moves) mod.Remove(r.FormKey, r.Registration.GetterType);
            foreach (var (copy, type) in copies) mod.GetTopLevelGroup(type).AddUntyped(copy);
            mod.RemapLinks(remap);
        }
        if (taken.Count > 0 && mod.ModHeader.Stats.NextFormID <= taken.Max()) mod.ModHeader.Stats.NextFormID = taken.Max() + 1;
        var ordered = locked.OrderBy(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value.ToString("X3"));
        var json = JsonSerializer.Serialize(ordered, new JsonSerializerOptions { WriteIndented = true }) + "\n";
        if (!File.Exists(lockPath) || File.ReadAllText(lockPath) != json) File.WriteAllText(lockPath, json);
        return new IdLockResult(locked.Count, added, moves.Count, lockPath);
    }
}
