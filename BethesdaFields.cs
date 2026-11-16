// Fields Bethesda's own Starfield plugins always write and Mutagen 0.54.4 leaves out unless the builder sets them
// (tools\format-compare\FINDINGS.md, group A, 2026-11-15). Each fix touches only the plugin's own new records
// (overrides keep their master's data) and only fields that are empty, with the value Bethesda uses almost always.
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;

namespace BuildKit;

public static class BethesdaFields
{
    /// <summary>Mutagen 0.54.4's form version for a new Starfield record (GameConstants.Starfield.DefaultFormVersion).</summary>
    public const ushort MutagenDefaultFormVersion = 576;

    /// <summary>What <see cref="Fill"/> changed, by kind.</summary>
    public sealed record Report(int Messages, int FormLists, int Models, int Keywords, int StoryQuestNodes)
    {
        public int Total => Messages + FormLists + Models + Keywords + StoryQuestNodes;
        public override string ToString() =>
            $"{Messages} message back-out index, {FormLists} form list add-to-list, {Models} model light layer, " +
            $"{Keywords} keyword, {StoryQuestNodes} story-manager quest node";
    }

    static bool Own(StarfieldMod mod, IMajorRecordGetter r) => r.FormKey.ModKey == mod.ModKey;

    /// <summary>Fills empty fields on the plugin's own records with Bethesda's usual value:
    /// MESG BNAM (index chosen on back out) = -1 (2,759 of 2,761 Bethesda messages);
    /// FLST ANAM (add to list) = null (1,349 of 1,370);
    /// the model's FLLD (light layer) on Aid items and activators = 1 (364 of 365 Aid items);
    /// KYWD CNAM (color) = FF FF FF 00, TNAM (type) = 0, FNAM = 4 zero bytes (xEdit marks all three required);
    /// SMQN XNAM (max concurrent quests) = 0.</summary>
    public static Report Fill(StarfieldMod mod)
    {
        int messages = 0, lists = 0, models = 0, keywords = 0, nodes = 0;
        foreach (var m in mod.Messages.Where(x => Own(mod, x)))
            if (m.BNAM == null) { m.BNAM = 0xFFFFFFFF; messages++; }
        foreach (var f in mod.FormLists.Where(x => Own(mod, x)))
            if (f.AddToList.FormKeyNullable == null) { f.AddToList.SetTo(FormKey.Null); lists++; }
        foreach (var a in mod.Ingestibles.Where(x => Own(mod, x)))
            if (a.Model != null && a.Model.LightLayer == null) { a.Model.LightLayer = 1; models++; }
        foreach (var a in mod.Activators.Where(x => Own(mod, x)))
            if (a.Model != null && a.Model.LightLayer == null) { a.Model.LightLayer = 1; models++; }
        foreach (var k in mod.Keywords.Where(x => Own(mod, x)))
        {
            var changed = false;
            if (k.Color == null) { k.Color = System.Drawing.Color.FromArgb(0, 255, 255, 255); changed = true; }
            if (k.FNAM == null) { k.FNAM = new byte[4]; changed = true; }
            if (k.Type == null) { k.Type = (Keyword.TypeEnum)0; changed = true; }
            if (changed) keywords++;
        }
        foreach (var n in mod.StoryManagerQuestNodes.Where(x => Own(mod, x)))
            if (n.MaxConcurrentQuests == null) { n.MaxConcurrentQuests = 0; nodes++; }
        return new Report(messages, lists, models, keywords, nodes);
    }

    /// <summary>Number of interior cells in the plugin (own and overridden), for the header's INCC.</summary>
    public static int InteriorCells(StarfieldMod mod) =>
        mod.Cells.SelectMany(b => b.SubBlocks).SelectMany(s => s.Cells)
            .Count(c => c.Flags is { } f && f.HasFlag(Cell.Flag.IsInteriorCell));

    /// <summary>Raises the plugin's own records still at Mutagen's default form version (576) to the Creation Kit's
    /// (Plugin.CkFormVersion, 582). Records a builder set to another version, and overrides, are left alone.</summary>
    public static int RaiseFormVersions(StarfieldMod mod)
    {
        var n = 0;
        foreach (var r in mod.EnumerateMajorRecords().Where(x => Own(mod, x)))
            if (r is IStarfieldMajorRecordInternal s && s.FormVersion == MutagenDefaultFormVersion)
            {
                s.FormVersion = Plugin.CkFormVersion;
                n++;
            }
        return n;
    }

    /// <summary>Sets the two words after a group header's type (the version-control stamp and the word after it) to
    /// zero on every group the plugin writes, as on all 138,389 groups in Bethesda's own plugins. A Creation Kit save
    /// stamps them, and Mutagen keeps the stamps when a builder copies records or groups from such a plugin (so a
    /// rebuild of another author's mod carries that author's stamps). The game doesn't use them. Covers top-level
    /// groups, cell blocks and sub-blocks, world, cell (persistent, temporary), topic and quest children groups.
    /// These are group headers, not record data, so overridden cells' and quests' groups are zeroed too.
    /// Returns the number of groups that had a nonzero word.</summary>
    public static int ZeroGroupStamps(StarfieldMod mod)
    {
        var n = 0;
        bool Zero(int a, int b) { if (a != 0 || b != 0) { n++; return true; } return false; }

        // Top-level groups (StarfieldGroup<T>, and StarfieldListGroup<CellBlock> for CELL): public LastModified and
        // Unknown on each group property of the mod. Reflection, because the groups share no non-generic interface.
        foreach (var p in mod.GetType().GetProperties())
        {
            if (p.GetIndexParameters().Length > 0) continue;
            var t = p.PropertyType;
            var lm = t.GetProperty("LastModified");
            var un = t.GetProperty("Unknown");
            if (lm?.PropertyType != typeof(int) || un?.PropertyType != typeof(int) || !lm.CanWrite || !un.CanWrite) continue;
            if (p.GetValue(mod) is not { } g) continue;
            if (Zero((int)lm.GetValue(g)!, (int)un.GetValue(g)!)) { lm.SetValue(g, 0); un.SetValue(g, 0); }
        }
        foreach (var b in mod.Cells)
        {
            if (Zero(b.LastModified, b.Unknown)) { b.LastModified = 0; b.Unknown = 0; }
            foreach (var s in b.SubBlocks)
                if (Zero(s.LastModified, s.Unknown)) { s.LastModified = 0; s.Unknown = 0; }
        }
        foreach (var w in mod.Worldspaces)
        {
            if (Zero(w.SubCellsTimestamp, w.SubCellsUnknown)) { w.SubCellsTimestamp = 0; w.SubCellsUnknown = 0; }
            foreach (var b in w.SubCells)
            {
                if (Zero(b.LastModified, b.Unknown)) { b.LastModified = 0; b.Unknown = 0; }
                foreach (var s in b.Items)
                    if (Zero(s.LastModified, s.Unknown)) { s.LastModified = 0; s.Unknown = 0; }
            }
        }
        foreach (var c in mod.EnumerateMajorRecords<ICell>())
        {
            if (Zero(c.Timestamp, c.UnknownGroupData)) { c.Timestamp = 0; c.UnknownGroupData = 0; }
            if (Zero(c.PersistentTimestamp, c.PersistentUnknownGroupData)) { c.PersistentTimestamp = 0; c.PersistentUnknownGroupData = 0; }
            if (Zero(c.TemporaryTimestamp, c.TemporaryUnknownGroupData)) { c.TemporaryTimestamp = 0; c.TemporaryUnknownGroupData = 0; }
        }
        foreach (var q in mod.EnumerateMajorRecords<IQuest>())
            if (Zero(q.Timestamp, q.Unknown)) { q.Timestamp = 0; q.Unknown = 0; }
        foreach (var d in mod.EnumerateMajorRecords<IDialogTopic>())
            if (Zero(d.Timestamp, d.Unknown)) { d.Timestamp = 0; d.Unknown = 0; }
        return n;
    }

    /// <summary>Marks the plugin's own NPC records compressed, as every Bethesda NPC_ is.</summary>
    public static int CompressNpcs(StarfieldMod mod)
    {
        var n = 0;
        foreach (var npc in mod.Npcs.Where(x => Own(mod, x)))
            if (!npc.IsCompressed) { npc.IsCompressed = true; n++; }
        return n;
    }
}
