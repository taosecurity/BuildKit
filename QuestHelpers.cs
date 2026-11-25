// Quest helpers: a quest with its flags and priority, stages with their log entries, objectives with their targets,
// and the stage fragment script that links them (SetObjectiveDisplayed and the like live in the quest's fragment
// script, not in record fields). Field order and fixed values copy the Creation Kit's layout of vanilla quests
// (Starfield.esm MQ_TutorialQuest_Misc02, LC139 and BE_SC04_Misc; the format notes follow):
//   DNAM   flags u16 (RunOnce 0x100, StartGameEnabled 0x1), priority at byte 4
//   stage  INDX (index, flags 0x02 for the start-up stage, 0xAB), one log entry always: QSDT, NAM2 editor note and
//          CNAM log text (each only when given), QSRD [4] 0, QSRD empty
//   objective QOBJ, FNAM 0, NNAM, one QSTA (alias ID, 0, keyword 0) per target and none without targets
//   alias  ALST (reference) or ALLS (location) ID = max + 1, ALID, FNAM flags, ALFG 0, the fill (ALFR; CTDA HasKeyword;
//          ALCO + ALCA alias ID | 0x8000 for "in" + ALCL 0; ALFL), VTCK 0 on reference aliases only, ALED.
//          ANAM (next free alias ID) is computed by Mutagen 0.54.4 itself as max ID + 1 (checked by test).
//          Vanilla samples: FC_EvidenceSlateHolderQuest, SQ_TemplateRadiantPlanetQT, BE_SC04_Misc.
//   scene  SCEN in the quest's children: FNAM flags, per line a phase (NAM0 empty, WNAM 500), two actors (LNAM 8,
//          DNAM 0x11A), per line a dialogue action in its own phase (DMAX 10, DMIN 1), PNAM, INAM last action, VNAM
//          five 3s, SCQS only with an end stage, BOLV, XNAM scene index. Each line: a DIAL (PNAM 50, DATA 0/2/0x11,
//          TIFL/TIFC 1) with one INFO (ENAM 0, TRDA emotion -1, NAM1 text, INAM 1). Sample RyujinTower_RC_Floor3_RobertandEmployee.
//   VMAD   version 6, object format 2, ExtraBindDataVersion 3, fragment count, fragment script
//          Fragments:Quests:QF_<EditorID>_<8 hex> (Local, no properties), one fragment per stage (StageIndex 0, Unknown2 1, Fragment_Stage_<NNNN>_Item_00)
using System.Text;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Starfield;
using Noggog;

namespace BuildKit;

/// <summary>One line of a <see cref="Quests.Scene"/>: who says it (0 = the first actor, 1 = the second) and the text.</summary>
public record SceneLine(int Actor, string Text);

/// <summary>What finishing a stage does to the quest (the stage's log entry flags).</summary>
public enum StageEnd { None, CompleteQuest, FailQuest }

public static partial class Quests
{
    /// <summary>The Creation Kit's fixed last INDX byte, written on every vanilla stage (Mutagen QuestStage.Unknown).</summary>
    public const byte StageUnknown = 0xAB;

    /// <summary>A new quest (<see cref="New"/>) with a name (FULL) and DNAM: <paramref name="priority"/>, RunOnce
    /// (the usual vanilla value) and StartGameEnabled if asked. Add stages with <see cref="Stage"/>, objectives with
    /// Objective and the stage links with <see cref="Fragments"/>. key = a fixed FormID (otherwise the
    /// next free one); text = how to make the text (default: <see cref="Localized.For"/>); formVersion null = leave
    /// as is.</summary>
    public static Quest Basic(StarfieldMod mod, string editorId, string? name = null, byte priority = 50,
        bool startGameEnabled = false, bool runOnce = true,
        FormKey? key = null, Func<string, TranslatedString>? text = null, ushort? formVersion = Plugin.CkFormVersion)
    {
        var q = New(mod, editorId, key, formVersion);
        if (name != null) q.Name = Localized.For(mod, name, text);
        var flags = (Quest.Flag)0;
        if (runOnce) flags |= Quest.Flag.RunOnce;
        if (startGameEnabled) flags |= Quest.Flag.StartGameEnabled;
        q.Data = new QuestData { Flags = flags, Priority = priority };
        return q;
    }

    /// <summary>Adds stage <paramref name="index"/> with exactly one log entry, as the Creation Kit writes it.
    /// log = the quest log text the player sees (CNAM; none: no log text, as on most vanilla misc stages);
    /// startUp = run this stage when the quest starts; end = complete or fail the quest when the stage is set;
    /// note = an editor-only note (NAM2, plain text, never shown in game). Stages stay sorted by index.
    /// A repeated index throws.</summary>
    public static QuestStage Stage(StarfieldMod mod, Quest quest, ushort index, string? log = null, bool startUp = false,
        StageEnd end = StageEnd.None, string? note = null, Func<string, TranslatedString>? text = null)
    {
        if (quest.Stages.Any(s => s.Index == index))
            throw new ArgumentException($"{quest.EditorID} already has stage {index}.", nameof(index));
        var entry = new QuestLogEntry
        {
            Flags = end switch
            {
                StageEnd.CompleteQuest => QuestLogEntry.Flag.CompleteQuest,
                StageEnd.FailQuest => QuestLogEntry.Flag.FailQuest,
                _ => (QuestLogEntry.Flag)0,
            },
            StageCompleteDatas = new ExtendedList<QuestStageCompleteData>
            {
                new() { QSRD = new byte[4] },
                new() { QSRD = Array.Empty<byte>() },
            },
        };
        if (log != null) entry.Entry = Localized.For(mod, log, text);
        if (note != null) entry.Note = note;
        var stage = new QuestStage
        {
            Index = index,
            Flags = startUp ? QuestStage.Flag.RunOnStart : 0,
            Unknown = StageUnknown,
            LogEntries = new ExtendedList<QuestLogEntry> { entry },
        };
        var at = quest.Stages.TakeWhile(s => s.Index < index).Count();
        quest.Stages.Insert(at, stage);
        return stage;
    }

    /// <summary>Adds objective <paramref name="index"/> with its display text and one target per alias in
    /// <paramref name="targets"/> (the quest marker points at the alias). Vanilla objectives use the index of the
    /// stage that shows them. Show and complete it from a stage with <see cref="Fragments"/>. A repeated index
    /// throws.</summary>
    public static QuestObjective Objective(StarfieldMod mod, Quest quest, ushort index, string displayText,
        IEnumerable<AQuestAlias>? targets = null, Func<string, TranslatedString>? text = null)
    {
        var list = (targets ?? []).ToList();
        if (list.FirstOrDefault(t => !HasAlias(quest, t)) is { } other)
            throw new ArgumentException($"Alias \"{AliasName(other)}\" is not on {quest.EditorID}: add it first.", nameof(targets));
        return Objective(mod, quest, index, displayText, list.Select(AliasId), text);
    }

    /// <summary><see cref="Objective(StarfieldMod, Quest, ushort, string, IEnumerable{AQuestAlias}?, Func{string, TranslatedString}?)"/>
    /// with the targets given by alias ID.</summary>
    public static QuestObjective Objective(StarfieldMod mod, Quest quest, ushort index, string displayText,
        IEnumerable<int> targetIds, Func<string, TranslatedString>? text = null)
    {
        if (quest.Objectives.Any(o => o.Index == index))
            throw new ArgumentException($"{quest.EditorID} already has objective {index}.", nameof(index));
        var o = new QuestObjective
        {
            Index = index,
            Flags = 0,
            DisplayText = Localized.For(mod, displayText, text),
        };
        foreach (var id in targetIds)
            o.Targets.Add(new QuestObjectiveTarget { AliasID = id, Flags = 0, Keyword = new FormLink<IKeywordGetter>(FormKey.Null) });
        quest.Objectives.Add(o);
        return o;
    }

    /// <summary>The stage links of <paramref name="quest"/>: what each stage does when it is set. Add them, then call
    /// <see cref="QuestFragments.Apply"/> to write the fragment entries and compile <see cref="QuestFragments.Source"/>
    /// with your own Papyrus scripts (to <see cref="QuestFragments.SourcePath"/>).</summary>
    public static QuestFragments Fragments(Quest quest) => new(quest);

    /// <summary>The next free alias ID of <paramref name="quest"/>: one more than the highest ID over all its aliases
    /// (0 for none), as the Creation Kit numbers them. Mutagen writes it as ANAM.</summary>
    public static uint NextAliasId(Quest quest)
    {
        var ids = quest.Aliases?.SelectMany(UsedAliasIds).ToList() ?? [];
        return ids.Count > 0 ? ids.Max() + 1 : 0;
    }

    /// <summary>A reference alias filled with a fixed placed reference (ALFR). optional = the quest still starts if the
    /// reference can't be filled.</summary>
    public static QuestReferenceAlias ForcedAlias(Quest quest, string name, IFormLinkGetter<IPlacedGetter> placedRef, bool optional = false)
    {
        var a = RefAlias(quest, name, 0, optional);
        a.ForcedReference = new FormLinkNullable<IPlacedGetter>(placedRef.FormKey);
        return Add(quest, a);
    }

    /// <summary>A reference alias filled with a reference in the loaded area that has <paramref name="keyword"/> (one
    /// HasKeyword == 1 condition on the subject). actorsOnly = match actors only; closest = the closest match.</summary>
    public static QuestReferenceAlias FindAlias(Quest quest, string name, IFormLinkGetter<IKeywordGetter> keyword,
        bool actorsOnly = false, bool closest = false, bool optional = false)
    {
        var flags = AQuestAlias.Flag.MatchingRefInLoadedArea;
        if (actorsOnly) flags |= AQuestAlias.Flag.MatchingRefForActorsOnly;
        if (closest) flags |= AQuestAlias.Flag.MatchingRefClosest;
        var a = RefAlias(quest, name, flags, optional);
        var data = new HasKeywordConditionData { RunOnType = Condition.RunOnType.Subject };
        data.FirstParameter.Link.SetTo(keyword.FormKey);
        a.Conditions.Add(new ConditionFloat { CompareOperator = CompareOperator.EqualTo, ComparisonValue = 1f, Data = data });
        return Add(quest, a);
    }

    /// <summary>A reference alias filled with a new temporary reference of <paramref name="baseObject"/>, created at
    /// (or, with inside, in the inventory of) the reference in <paramref name="atAlias"/>, an alias of the same
    /// quest.</summary>
    public static QuestReferenceAlias CreatedAlias(Quest quest, string name, IFormLinkGetter<IStarfieldMajorRecordGetter> baseObject,
        QuestReferenceAlias atAlias, bool inside = false, bool optional = false)
    {
        if (!HasAlias(quest, atAlias))
            throw new ArgumentException($"Alias \"{atAlias.Name}\" is not on {quest.EditorID}: add it first.", nameof(atAlias));
        var a = RefAlias(quest, name, AQuestAlias.Flag.CreateRefTemp, optional);
        a.CreateReferenceToObject = new CreateReferenceToObject
        {
            Object = new FormLink<IStarfieldMajorRecordGetter>(baseObject.FormKey),
            AliasID = checked((short)atAlias.ID),
            Create = inside ? CreateReferenceToObject.CreateEnum.In : CreateReferenceToObject.CreateEnum.At,
            Level = Level.Easy,
        };
        return Add(quest, a);
    }

    /// <summary>A location alias filled with a fixed location (ALFL).</summary>
    public static QuestLocationAlias LocationAlias(Quest quest, string name, IFormLinkGetter<ILocationGetter> location, bool optional = false)
    {
        CheckName(quest, name);
        var a = new QuestLocationAlias
        {
            ID = NextAliasId(quest),
            Name = name,
            Flags = optional ? AQuestAlias.Flag.Optional : 0,
            ALFG = 0,
            SpecificLocation = new FormLinkNullable<ILocationGetter>(location.FormKey),
        };
        return Add(quest, a);
    }

    /// <summary>A simple scene of <paramref name="quest"/>: two actors (reference aliases of the quest) take turns
    /// saying <paramref name="lines"/>, one line per phase, in the given order (Actor 0 = first, 1 = second). Each line
    /// gets its own scene topic (DIAL, category Scene, subtype CustomScene) with one response (INFO) holding the text.
    /// beginOnQuestStart = the scene starts when the quest does (otherwise start it from a fragment with
    /// <c>Scene.Start()</c>); it always stops when the quest stops. endStage = the quest stage set when the scene ends
    /// (it must exist on the quest). key = a fixed FormID for the scene (the topics and responses get the next free
    /// ones, with the EditorIDs <c>&lt;editorId&gt;_Line01</c> and <c>&lt;editorId&gt;_Line01_Info</c> and so on, so
    /// <see cref="IdLock"/> can lock them); text = how to make the text (default: <see cref="Localized.For"/>).</summary>
    public static Scene Scene(StarfieldMod mod, Quest quest, string editorId, QuestReferenceAlias first, QuestReferenceAlias second,
        IEnumerable<SceneLine> lines, ushort? endStage = null, bool beginOnQuestStart = true,
        FormKey? key = null, Func<string, TranslatedString>? text = null, ushort? formVersion = Plugin.CkFormVersion)
    {
        if (string.IsNullOrWhiteSpace(editorId)) throw new ArgumentException("A scene needs an EditorID.", nameof(editorId));
        foreach (var (a, n) in new[] { (first, nameof(first)), (second, nameof(second)) })
            if (!HasAlias(quest, a))
                throw new ArgumentException($"Alias \"{a.Name}\" is not on {quest.EditorID}: add it first.", n);
        if (first == second) throw new ArgumentException("The two actors must be different aliases.", nameof(second));
        var list = lines.ToList();
        if (list.Count == 0) throw new ArgumentException("A scene needs at least one line.", nameof(lines));
        if (list.FirstOrDefault(l => l.Actor is not (0 or 1)) is { } bad)
            throw new ArgumentException($"Line \"{bad.Text}\" has actor {bad.Actor}: use 0 (first) or 1 (second).", nameof(lines));
        if (endStage is { } es && !quest.Stages.Any(s => s.Index == es))
            throw new ArgumentException($"{quest.EditorID} has no stage {es}: add it with Quests.Stage first.", nameof(endStage));
        if (quest.Scenes.Any(s => s.EditorID == editorId))
            throw new ArgumentException($"{quest.EditorID} already has a scene named {editorId}.", nameof(editorId));

        var scene = new Scene(key ?? mod.GetNextFormKey(), StarfieldRelease.Starfield) { EditorID = editorId };
        if (formVersion is { } fv) scene.FormVersion = fv;
        scene.Flags = (beginOnQuestStart ? Mutagen.Bethesda.Starfield.Scene.Flag.BeginOnQuestStart : 0)
            | Mutagen.Bethesda.Starfield.Scene.Flag.StopOnQuestEnd;
        scene.Actions = new ExtendedList<ASceneAction>();   // null in a new Scene (Mutagen 0.54.4)
        uint[] actorIds = [first.ID, second.ID];
        foreach (var id in actorIds)
            scene.Actors.Add(new SceneActor { ID = id, Flags = SceneActor.Flag.NoCommandState, BehaviorFlags = SceneActorBehavior });
        for (var i = 0; i < list.Count; i++)
        {
            scene.Phases.Add(new ScenePhase { Name = "", EditorWidth = 500 });
            // EditorIDs (vanilla scene topics and lines have none) so IdLock can keep their FormIDs between builds.
            var lineId = $"{editorId}_Line{i + 1:D2}";
            var topic = new DialogTopic(mod.GetNextFormKey(), StarfieldRelease.Starfield)
            {
                EditorID = lineId,
                Priority = 50f,
                Quest = new FormLink<IQuestGetter>(quest.FormKey),
                Category = DialogTopic.CategoryEnum.Scene,
                Subtype = DialogTopic.SubtypeEnum.CustomScene,
                SubtypeName = DialogTopic.SubtypeNameEnum.CustomScene,
            };
            if (formVersion is { } tv) topic.FormVersion = tv;
            var info = new DialogResponses(mod.GetNextFormKey(), StarfieldRelease.Starfield)
            {
                EditorID = lineId + "_Info",
                Flags = new DialogResponseFlags(),                              // ENAM 0
                SubtitlePriority = DialogResponses.SubtitlePriorityLevel.Low,   // INAM 1, as on every vanilla scene line
            };
            if (formVersion is { } iv) info.FormVersion = iv;
            info.Responses.Add(new DialogResponse
            {
                Emotion = new FormLink<IKeywordGetter>(FormKey.None),           // TRDA starts FF FF FF FF in vanilla
                ResponseText = Localized.For(mod, list[i].Text, text),          // NAM1
            });
            topic.Responses.Add(info);
            topic.TopicInfoList = [new FormLink<IDialogResponsesGetter>(info.FormKey)];   // TIFL, null in a new topic
            quest.DialogTopics.Add(topic);
            scene.Actions.Add(new DialogueSceneAction
            {
                Name = "",
                AliasID = checked((int)actorIds[list[i].Actor]),
                Index = (uint)i + 1,
                Flags = 0,
                StartPhase = (uint)i,
                EndPhase = (uint)i,
                Topic = new FormLinkNullable<IDialogTopicGetter>(topic.FormKey),
                LoopingMax = 10f,
                LoopingMin = 1f,
            });
        }
        scene.Quest = new FormLinkNullable<IQuestGetter>(quest.FormKey);
        scene.LastActionIndex = (uint)list.Count;
        scene.VNAM = SceneVnam;
        if (endStage is { } end) scene.SetParentQuestStage = new SceneSetParentQuestStage { OnBegin = -1, OnEnd = checked((short)end) };
        scene.BOLV = Array.Empty<byte>();
        scene.Index = (uint)quest.Scenes.Count;
        quest.Scenes.Add(scene);
        return scene;
    }

    /// <summary>The scene actor behavior flags of the vanilla two-actor scenes (DNAM 0x11A: death end, combat end,
    /// dialogue pause and a bit above Mutagen's named flags).</summary>
    const SceneActor.BehaviorFlag SceneActorBehavior = (SceneActor.BehaviorFlag)0x11A;

    /// <summary>Scene VNAM as the Creation Kit writes it: five u32 3s.</summary>
    static byte[] SceneVnam => [3, 0, 0, 0, 3, 0, 0, 0, 3, 0, 0, 0, 3, 0, 0, 0, 3, 0, 0, 0];

    static QuestReferenceAlias RefAlias(Quest quest, string name, AQuestAlias.Flag flags, bool optional)
    {
        CheckName(quest, name);
        return new QuestReferenceAlias
        {
            ID = NextAliasId(quest),
            Name = name,
            Flags = optional ? flags | AQuestAlias.Flag.Optional : flags,
            ALFG = 0,
            VoiceTypes = new FormLinkNullable<IAliasVoiceTypeGetter>(FormKey.Null),   // VTCK 0 on every vanilla reference alias
        };
    }

    static void CheckName(Quest quest, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("An alias needs a name.", nameof(name));
        if (quest.Aliases?.Any(x => AliasName(x) == name) == true)
            throw new ArgumentException($"{quest.EditorID} already has an alias named \"{name}\".", nameof(name));
    }

    // By reference: Mutagen's Equals compares values, so an equal alias of another quest would pass.
    static bool HasAlias(Quest quest, AQuestAlias alias) => quest.Aliases?.Any(x => ReferenceEquals(x, alias)) == true;

    static T Add<T>(Quest quest, T alias) where T : AQuestAlias
    {
        (quest.Aliases ??= new ExtendedList<AQuestAlias>()).Add(alias);
        return alias;
    }

    static string? AliasName(AQuestAlias alias) => alias switch
    {
        QuestReferenceAlias r => r.Name,
        QuestLocationAlias l => l.Name,
        _ => null,
    };

    internal static int AliasId(AQuestAlias alias) => alias switch
    {
        QuestReferenceAlias r => checked((int)r.ID),
        QuestLocationAlias l => checked((int)l.ID),
        _ => throw new ArgumentException($"{alias.GetType().Name} has no alias ID BuildKit can read.", nameof(alias)),
    };

    /// <summary>Every alias ID an alias uses: its own, or for a collection alias each entry's ID and its reference
    /// alias's ID.</summary>
    static IEnumerable<uint> UsedAliasIds(AQuestAlias alias) => alias switch
    {
        QuestReferenceAlias r => [r.ID],
        QuestLocationAlias l => [l.ID],
        QuestCollectionAlias c => c.Collection.SelectMany(x => new[] { x.ID, x.ReferenceAlias.ID }).OfType<uint>(),
        _ => [],
    };
}

/// <summary>A quest's stage fragment script, built from one Papyrus line per call. Made by
/// <see cref="Quests.Fragments"/>.</summary>
public sealed class QuestFragments
{
    readonly Quest quest;
    readonly SortedDictionary<ushort, List<string>> lines = new();

    internal QuestFragments(Quest quest)
    {
        if (string.IsNullOrEmpty(quest.EditorID) || !quest.EditorID.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            || char.IsAsciiDigit(quest.EditorID[0]))
            throw new ArgumentException($"Quest EditorID \"{quest.EditorID}\" can't name a Papyrus script: use letters, digits and _ only, not starting with a digit.", nameof(quest));
        this.quest = quest;
    }

    /// <summary>The fragment script's name, as the Creation Kit names it: the quest's FormID with a 00 high byte
    /// (the record and the script only have to agree).</summary>
    public string ScriptName => $"Fragments:Quests:QF_{quest.EditorID}_{quest.FormKey.ID & 0xFFFFFF:X8}";

    /// <summary>Where the source goes under your Papyrus source folder (Scripts\Source\...).</summary>
    public string SourcePath => Path.Combine(ScriptName.Split(':')) + ".psc";

    public QuestFragments ShowObjective(ushort stage, ushort objective) => Line(stage, $"SetObjectiveDisplayed({objective})");
    public QuestFragments ShowObjective(QuestStage stage, QuestObjective objective) => ShowObjective(stage.Index, objective.Index);
    public QuestFragments CompleteObjective(ushort stage, ushort objective) => Line(stage, $"SetObjectiveCompleted({objective})");
    public QuestFragments CompleteObjective(QuestStage stage, QuestObjective objective) => CompleteObjective(stage.Index, objective.Index);
    public QuestFragments SetStage(ushort stage, ushort nextStage) => Line(stage, $"SetStage({nextStage})");
    public QuestFragments SetStage(QuestStage stage, QuestStage nextStage) => SetStage(stage.Index, nextStage.Index);
    public QuestFragments CompleteQuest(ushort stage) => Line(stage, "CompleteQuest()");
    public QuestFragments CompleteQuest(QuestStage stage) => CompleteQuest(stage.Index);
    public QuestFragments Line(QuestStage stage, string papyrusLine) => Line(stage.Index, papyrusLine);

    /// <summary>Any Papyrus line, run in the quest's own script (Self is the quest) when the stage is set.</summary>
    public QuestFragments Line(ushort stage, string papyrusLine)
    {
        if (!lines.TryGetValue(stage, out var l)) lines[stage] = l = [];
        l.Add(papyrusLine);
        return this;
    }

    static string FunctionName(ushort stage) => $"Fragment_Stage_{stage:D4}_Item_00";

    /// <summary>Writes the fragment entries into the quest's VMAD (made if missing; scripts already attached are
    /// kept). Every stage with lines must exist on the quest, otherwise this throws.</summary>
    public Quest Apply()
    {
        var missing = lines.Keys.Where(s => !quest.Stages.Any(q => q.Index == s)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"{quest.EditorID} has no stage {string.Join(", ", missing)}: add it with Quests.Stage first.");
        var a = quest.VirtualMachineAdapter ??= new QuestAdapter();
        a.Version = 6;
        a.ObjectFormat = 2;
        a.ExtraBindDataVersion = 3;
        a.Script = new ScriptEntry { Name = ScriptName, Flags = ScriptEntry.Flag.Local };
        a.Fragments.Clear();
        foreach (var stage in lines.Keys)
            a.Fragments.Add(new QuestScriptFragment
            {
                Stage = stage, Unknown = 0, StageIndex = 0, Unknown2 = 1, ScriptName = ScriptName, FragmentName = FunctionName(stage),
            });
        return quest;
    }

    /// <summary>The Papyrus source of the fragment script, one function per stage, to compile with your own
    /// scripts.</summary>
    public string Source()
    {
        var sb = new StringBuilder();
        sb.Append(";BEGIN FRAGMENT CODE - Made by BuildKit Quests.Fragments\r\n");
        sb.Append($"Scriptname {ScriptName} Extends Quest Hidden Const\r\n");
        foreach (var (stage, body) in lines)
        {
            var f = FunctionName(stage);
            sb.Append($"\r\n;BEGIN FRAGMENT {f}\r\nFunction {f}()\r\n;BEGIN CODE\r\n");
            foreach (var l in body) sb.Append(l).Append("\r\n");
            sb.Append(";END CODE\r\nEndFunction\r\n;END FRAGMENT\r\n");
        }
        sb.Append("\r\n;END FRAGMENT CODE - Made by BuildKit Quests.Fragments\r\n");
        return sb.ToString();
    }
}
