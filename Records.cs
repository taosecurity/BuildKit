// Two record fixes every Creation Kit plugin has and a plain Mutagen 0.54.4 record lacks:
// - a quest made with AddNew has null condition and alias lists, so Mutagen writes no NEXT and no ANAM (the Creation
//   Kit always writes both). Quests.New gives empty lists instead.
// - a dialogue topic whose SubtypeName (SNAM) was never set is written with a blank SNAM; vanilla topics carry the
//   four-letter code that matches their Subtype. DialogTopics.FixSubtypeNames fills it, and PluginWriter.Write calls
//   it for you (WriteOptions.FixDialogSubtypes).
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog;

namespace BuildKit;

public static class Quests
{
    /// <summary>A new quest with empty (not null) UnusedConditions and Aliases, so Mutagen writes NEXT and ANAM as
    /// the Creation Kit does. Flags, stages and aliases are left to the caller.</summary>
    public static Quest New(StarfieldMod mod, string editorId, FormKey? key = null, ushort? formVersion = Plugin.CkFormVersion)
    {
        var q = key is { } k ? new Quest(k, StarfieldRelease.Starfield) { EditorID = editorId } : mod.Quests.AddNew(editorId);
        if (key != null) mod.Quests.Add(q);
        if (formVersion is { } fv) q.FormVersion = fv;
        q.UnusedConditions = new ExtendedList<Condition>();
        q.Aliases = new ExtendedList<AQuestAlias>();
        return q;
    }
}

public static class DialogTopics
{
    /// <summary>Sets SubtypeName (SNAM) from Subtype on every dialogue topic in the plugin whose SNAM is blank.
    /// Topics that already have one are left alone. Returns (fixed, unmatched): unmatched counts blank topics whose
    /// Subtype has no SNAM code of the same name (Mutagen 0.54.4: CustomCommand and ShootBow); they stay blank.</summary>
    public static (int Fixed, int Unmatched) FixSubtypeNames(StarfieldMod mod)
    {
        int fixedCount = 0, unmatched = 0;
        foreach (var topic in mod.EnumerateMajorRecords<DialogTopic>().Where(t => t.SubtypeName == 0))
        {
            if (Enum.TryParse<DialogTopic.SubtypeNameEnum>(topic.Subtype.ToString(), out var name))
            {
                topic.SubtypeName = name;
                fixedCount++;
            }
            else unmatched++;
        }
        return (fixedCount, unmatched);
    }
}
