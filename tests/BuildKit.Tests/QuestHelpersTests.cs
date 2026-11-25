// Quest helpers: the raw QUST bytes are compared with the Creation Kit layout of vanilla quests (the format notes at
// the top of QuestHelpers.cs; no game files are read here).
using System.Text;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Xunit;

namespace BuildKit.Tests;

public class QuestHelpersTests
{
    /// <summary>The subrecords of the first QUST record, in file order.</summary>
    static List<(string Sig, byte[] Data)> QuestFields(byte[] plugin) => Records(plugin, "QUST").First().Fields;

    /// <summary>Every record of type <paramref name="type"/> (uncompressed), in file order: its FormID and its
    /// subrecords in file order.</summary>
    static List<(uint FormId, List<(string Sig, byte[] Data)> Fields)> Records(byte[] plugin, string type)
    {
        var found = new List<(uint, List<(string, byte[])>)>();
        for (var p = 0; p + 24 <= plugin.Length;)
        {
            var sig = Encoding.ASCII.GetString(plugin, p, 4);
            var size = BitConverter.ToInt32(plugin, p + 4);
            if (sig == "GRUP") { p += 24; continue; }
            if (sig == type)
            {
                var fields = new List<(string, byte[])>();
                for (int q = p + 24, end = p + 24 + size; q + 6 <= end;)
                {
                    var len = BitConverter.ToUInt16(plugin, q + 4);
                    fields.Add((Encoding.ASCII.GetString(plugin, q, 4), plugin[(q + 6)..(q + 6 + len)]));
                    q += 6 + len;
                }
                found.Add((BitConverter.ToUInt32(plugin, p + 12), fields));
            }
            p += 24 + size;
        }
        if (found.Count == 0) throw new InvalidOperationException($"no {type} record");
        return found;
    }

    /// <summary>The made-up scene: a quest with a start stage, an end stage, two actors and three lines.</summary>
    static (StarfieldMod Mod, Quest Quest, Scene Scene) SceneSample(ushort? endStage = 100, bool extraFirst = false)
    {
        var mod = Plugin.New("QuestTest.esm", PluginStyle.Light, "test", "quests");
        if (extraFirst) mod.Keywords.AddNew("QT_AddedLater");   // a later build adds a record first: every ID shifts
        var kw = mod.Keywords.AddNew("QT_TalkerKeyword").ToLink();
        var q = Quests.Basic(mod, "QT_Talk", "Talk");
        Quests.Stage(mod, q, 10, startUp: true);
        Quests.Stage(mod, q, 100, end: StageEnd.CompleteQuest);
        var a = Quests.FindAlias(q, "Clerk", kw, actorsOnly: true);
        var b = Quests.FindAlias(q, "Buyer", kw, actorsOnly: true);
        var s = Quests.Scene(mod, q, "QT_Talk_Scene", a, b,
            [new(0, "Morning. Need anything?"), new(1, "Just a box."), new(0, "Third shelf.")], endStage: endStage);
        return (mod, q, s);
    }

    [Fact]
    public void SceneRecordsCanBeLocked()
    {
        // IdLock (the README quick start) needs an EditorID on every own record, so the scene's topics and lines
        // get one too, and a second build keeps every FormID.
        using var t = new TempDir();
        var lockPath = Path.Combine(t.Path, "ids.lock.json");
        var (mod, _, _) = SceneSample();
        var r1 = IdLock.Apply(mod, lockPath);
        Assert.Equal(0, r1.Moved);
        Assert.Contains(mod.EnumerateMajorRecords(), r => r.EditorID == "QT_Talk_Scene_Line01_Info");
        var ids = mod.EnumerateMajorRecords().ToDictionary(r => r.EditorID!, r => r.FormKey);
        var (again, _, _) = SceneSample();
        IdLock.Apply(again, lockPath, update: false);   // throws if anything would move or be added
        Assert.Equal(ids, again.EnumerateMajorRecords().ToDictionary(r => r.EditorID!, r => r.FormKey));

        // A later build with a new record first: the quest, scene, topics and lines move back to their locked IDs,
        // stay in the quest's children, and every link still points at the right record.
        var (moved, _, _) = SceneSample(extraFirst: true);
        Assert.True(IdLock.Apply(moved, lockPath).Moved > 0);
        var game = new GameData(t.Path);
        var back = StarfieldMod.CreateFromBinaryOverlay(Write(moved, t, game), StarfieldRelease.Starfield);
        foreach (var r in back.EnumerateMajorRecords().Where(r => ids.ContainsKey(r.EditorID!)))
            Assert.Equal(ids[r.EditorID!].ID, r.FormKey.ID);
        var bq = back.Quests.Single();
        Assert.Equal(3, bq.DialogTopics.Count);
        var bs = bq.Scenes.Single();
        Assert.Equal(bq.FormKey, bs.Quest.FormKey);
        var topics = bq.DialogTopics.Select(d => d.FormKey).ToList();
        Assert.Equal(topics, bs.Actions!.OfType<IDialogueSceneActionGetter>().Select(a => a.Topic.FormKey));
        Assert.All(bq.DialogTopics, d => Assert.Equal(bq.FormKey, d.Quest.FormKey));
        Assert.All(bq.DialogTopics, d => Assert.Equal(d.Responses.Single().FormKey, d.TopicInfoList!.Single().FormKey));
    }

    [Fact]
    public void SceneFieldsComeInTheVanillaOrder()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var (mod, _, _) = SceneSample();
        var bytes = File.ReadAllBytes(Write(mod, t, game));
        var scen = Records(bytes, "SCEN").Single().Fields;
        const string phase = "HNAM,NAM0,NEXT,NEXT,WNAM,HNAM,";
        const string actor = "ALID,LNAM,DNAM,";
        const string action = "ANAM,NAM0,ALID,INAM,FNAM,SNAM,ENAM,DATA,DMAX,DMIN,HNAM,HNAM,ANAM,";
        Assert.Equal("EDID,FNAM," + phase + phase + phase + actor + actor + action + action + action + "PNAM,INAM,VNAM,SCQS,BOLV,XNAM",
            string.Join(",", scen.Select(f => f.Sig)));
        // Vanilla scene topics and lines have no EDID; BuildKit adds one (for IdLock), then the vanilla order.
        var dials = Records(bytes, "DIAL");
        for (var i = 0; i < dials.Count; i++)
        {
            Assert.Equal("EDID,PNAM,QNAM,DATA,SNAM,TIFL,TIFC", string.Join(",", dials[i].Fields.Select(f => f.Sig)));
            Assert.Equal(Encoding.ASCII.GetBytes($"QT_Talk_Scene_Line{i + 1:D2}\0"), dials[i].Fields[0].Data);
        }
        var infos = Records(bytes, "INFO");
        Assert.Equal(3, infos.Count);
        // Vanilla scene lines: ENAM,TRDA,TROT,NAM1,NAM2,NAM3,NAM4,NAM9,NAM0,INAM. TROT (voice types) and NAM9 (a CK
        // hash) only come from the Creation Kit's voice and edit data, so a built line has neither.
        foreach (var info in infos)
        {
            Assert.Equal("EDID,ENAM,TRDA,NAM1,NAM2,NAM3,NAM4,NAM0,INAM", string.Join(",", info.Fields.Select(f => f.Sig)));
            Assert.Equal(new byte[4], info.Fields[1].Data);                                        // ENAM flags 0
            Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, info.Fields[2].Data[..4]);         // no emotion
            Assert.Equal(BitConverter.GetBytes(1u), info.Fields[^1].Data);                         // INAM 1
        }
    }

    [Fact]
    public void SceneFixedValuesMatchTheCreationKit()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var (mod, q, s) = SceneSample();
        var bytes = File.ReadAllBytes(Write(mod, t, game));
        var scen = Records(bytes, "SCEN").Single().Fields;
        byte[] Get(string sig, int n = 0) => scen.Where(f => f.Sig == sig).ElementAt(n).Data;
        byte[] U32(uint v) => BitConverter.GetBytes(v);

        Assert.Equal(U32(0x3), Get("FNAM", 0));                                    // BeginOnQuestStart, StopOnQuestEnd
        Assert.Equal(new byte[] { 0 }, Get("NAM0", 0));                            // empty phase name
        Assert.All(scen.Where(f => f.Sig == "WNAM"), f => Assert.Equal(U32(500), f.Data));
        Assert.All(scen.Where(f => f.Sig == "LNAM"), f => Assert.Equal(U32(8), f.Data));
        Assert.All(scen.Where(f => f.Sig == "DNAM"), f => Assert.Equal(U32(0x11A), f.Data));
        Assert.Equal(U32(0), Get("ALID", 0));                                      // the actors: alias IDs 0 and 1
        Assert.Equal(U32(1), Get("ALID", 1));
        // Actions: type 0 (dialogue), the speaker's alias, index 1..3, flags 0, start and end phase i.
        int[] speakers = [0, 1, 0];
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(new byte[] { 0, 0 }, scen.Where(f => f.Sig == "ANAM" && f.Data.Length == 2).ElementAt(i).Data);
            Assert.Equal(U32((uint)speakers[i]), Get("ALID", 2 + i));
            Assert.Equal(U32((uint)i + 1), Get("INAM", i));
            Assert.Equal(U32(0), Get("FNAM", 1 + i));
            Assert.Equal(U32((uint)i), Get("SNAM", i));
            Assert.Equal(U32((uint)i), Get("ENAM", i));
            Assert.Equal(BitConverter.GetBytes(10f), Get("DMAX", i));
            Assert.Equal(BitConverter.GetBytes(1f), Get("DMIN", i));
        }
        Assert.Equal(Id(q.FormKey), Low(Get("PNAM")));
        Assert.Equal(U32(3), Get("INAM", 3));                                      // last action index
        Assert.Equal(Enumerable.Repeat(U32(3), 5).SelectMany(x => x).ToArray(), Get("VNAM"));
        Assert.Equal(new byte[] { 0xFF, 0xFF, 100, 0 }, Get("SCQS"));               // on begin -1, on end stage 100
        Assert.Empty(Get("BOLV"));
        Assert.Equal(U32(0), Get("XNAM"));

        // Each action's topic: priority 50, the quest, category Scene, subtype CustomScene, one INFO (its own).
        var dials = Records(bytes, "DIAL");
        var infos = Records(bytes, "INFO");
        for (var i = 0; i < 3; i++)
        {
            var d = dials[i].Fields;
            Assert.Equal(Low(Get("DATA", i)), BitConverter.GetBytes(dials[i].FormId)[..3]);
            Assert.Equal(BitConverter.GetBytes(50f), d.Single(f => f.Sig == "PNAM").Data);
            Assert.Equal(Id(q.FormKey), Low(d.Single(f => f.Sig == "QNAM").Data));
            Assert.Equal(new byte[] { 0, 2, 0x11, 0 }, d.Single(f => f.Sig == "DATA").Data);
            Assert.Equal(U32((uint)DialogTopic.SubtypeNameEnum.CustomScene), d.Single(f => f.Sig == "SNAM").Data);
            Assert.Equal(BitConverter.GetBytes(infos[i].FormId)[..3], Low(d.Single(f => f.Sig == "TIFL").Data));
            Assert.Equal(U32(1), d.Single(f => f.Sig == "TIFC").Data);
        }
        Assert.Equal(3, s.Actions.Count);
    }

    [Fact]
    public void SceneWithoutAnEndStageHasNoScqs()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var (mod, _, _) = SceneSample(endStage: null);
        var scen = Records(File.ReadAllBytes(Write(mod, t, game)), "SCEN").Single().Fields;
        Assert.DoesNotContain(scen, f => f.Sig == "SCQS");                         // as in the vanilla two-actor sample
    }

    [Fact]
    public void SceneReadsBack()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var (mod, _, _) = SceneSample();
        using var back = game.OpenPlugin(Write(mod, t, game));
        var q = back.Quests.Single();
        var s = q.Scenes.Single();
        Assert.Equal("QT_Talk_Scene", s.EditorID);
        Assert.Equal(q.FormKey, s.Quest.FormKey);
        Assert.Equal(new uint[] { 0, 1 }, s.Actors.Select(a => a.ID));
        Assert.Equal(3, s.Phases.Count);
        Assert.Equal((short)100, s.SetParentQuestStage!.OnEnd);
        var actions = s.Actions.Cast<IDialogueSceneActionGetter>().ToList();
        Assert.Equal(new[] { 0, 1, 0 }, actions.Select(a => a.AliasID!.Value));
        Assert.Equal(3, q.DialogTopics.Count);
        string[] said = ["Morning. Need anything?", "Just a box.", "Third shelf."];
        for (var i = 0; i < 3; i++)
        {
            var topic = q.DialogTopics.Single(d => d.FormKey == actions[i].Topic.FormKey);
            Assert.Equal(DialogTopic.CategoryEnum.Scene, topic.Category);
            Assert.Equal(DialogTopic.SubtypeEnum.CustomScene, topic.Subtype);
            Assert.Equal(said[i], topic.Responses.Single().Responses.Single().ResponseText.String);
        }
    }

    [Fact]
    public void SceneMistakesThrow()
    {
        var mod = Plugin.New("QuestTest.esm", PluginStyle.Light, "test", "quests");
        var kw = mod.Keywords.AddNew("QT_Kw").ToLink();
        var q = Quests.Basic(mod, "QT_SceneThrow");
        var other = Quests.Basic(mod, "QT_Other");
        var a = Quests.FindAlias(q, "A", kw);
        var b = Quests.FindAlias(q, "B", kw);
        var x = Quests.FindAlias(other, "X", kw);
        SceneLine[] one = [new(0, "Hi.")];
        Assert.Throws<ArgumentException>(() => Quests.Scene(mod, q, "S", a, x, one));                 // alias of another quest
        Assert.Throws<ArgumentException>(() => Quests.Scene(mod, q, "S", a, a, one));                 // same actor twice
        Assert.Throws<ArgumentException>(() => Quests.Scene(mod, q, "S", a, b, []));                  // no lines
        Assert.Throws<ArgumentException>(() => Quests.Scene(mod, q, "S", a, b, [new(2, "Who?")]));    // no third actor
        Assert.Throws<ArgumentException>(() => Quests.Scene(mod, q, "S", a, b, one, endStage: 99));   // no such stage
        Quests.Scene(mod, q, "S", a, b, one);
        Assert.Throws<ArgumentException>(() => Quests.Scene(mod, q, "S", a, b, one));                 // same EditorID
        Assert.Equal(1u, Quests.Scene(mod, q, "S2", b, a, one).Index);                                // second scene: XNAM 1
        Assert.Equal(2, q.Scenes.Count);                                                              // scenes live under the quest
    }

    /// <summary>The made-up quest of the step 2 test: a start stage with a log entry, a middle stage, an end stage
    /// that completes the quest, and one objective.</summary>
    static (StarfieldMod Mod, Quest Quest, QuestFragments Fragments) Sample(IEnumerable<int>? targets = null)
    {
        var mod = Plugin.New("QuestTest.esm", PluginStyle.Light, "test", "quests");
        var q = Quests.Basic(mod, "QT_FindTheBox", "Find the Box");
        var start = Quests.Stage(mod, q, 10, "I was asked to find a box.", startUp: true, note: "Quest starts");
        var end = Quests.Stage(mod, q, 100, "I found the box.", end: StageEnd.CompleteQuest);
        var mid = Quests.Stage(mod, q, 50);                       // added out of order on purpose
        var obj = Quests.Objective(mod, q, 10, "Find the box", targets ?? []);
        var f = Quests.Fragments(q).ShowObjective(start, obj).SetStage(mid, end).CompleteObjective(end, obj).CompleteQuest(end);
        f.Apply();
        return (mod, q, f);
    }

    static string Write(StarfieldMod mod, TempDir t, GameData game) => PluginWriter.Write(mod, t.Sub("out"), game, Fixtures.NoGapCheck);

    [Fact]
    public void QuestFieldsComeInTheVanillaOrder()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var (mod, _, _) = Sample();
        var fields = QuestFields(File.ReadAllBytes(Write(mod, t, game)));
        Assert.Equal(
            "EDID,VMAD,FULL,DNAM,NEXT," +
            "INDX,QSDT,NAM2,CNAM,QSRD,QSRD," +              // note before log text, as in vanilla RAD04
            "INDX,QSDT,QSRD,QSRD," +
            "INDX,QSDT,CNAM,QSRD,QSRD," +
            "QOBJ,FNAM,NNAM,ANAM",
            string.Join(",", fields.Select(f => f.Sig)));
    }

    [Fact]
    public void FixedValuesMatchTheCreationKit()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var (mod, _, _) = Sample();
        var fields = QuestFields(File.ReadAllBytes(Write(mod, t, game)));
        byte[] Get(string sig, int n = 0) => fields.Where(f => f.Sig == sig).ElementAt(n).Data;

        Assert.Equal(new byte[] { 0x00, 0x01, 0, 0, 50, 0, 0, 0, 0, 0, 0, 0 }, Get("DNAM"));   // RunOnce, priority 50
        Assert.Equal(new byte[] { 10, 0, 0x02, 0xAB }, Get("INDX", 0));                        // start-up stage
        Assert.Equal(new byte[] { 50, 0, 0x00, 0xAB }, Get("INDX", 1));
        Assert.Equal(new byte[] { 100, 0, 0x00, 0xAB }, Get("INDX", 2));
        Assert.Equal(new byte[] { 0x00 }, Get("QSDT", 0));
        Assert.Equal(new byte[] { 0x00 }, Get("QSDT", 1));
        Assert.Equal(new byte[] { 0x01 }, Get("QSDT", 2));                                     // CompleteQuest
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(new byte[4], Get("QSRD", 2 * i));
            Assert.Empty(Get("QSRD", 2 * i + 1));
        }
        Assert.Equal("Quest starts\0", Encoding.UTF8.GetString(Get("NAM2", 0)));
        Assert.Equal("I was asked to find a box.\0", Encoding.UTF8.GetString(Get("CNAM", 0)));
        Assert.Equal("I found the box.\0", Encoding.UTF8.GetString(Get("CNAM", 1)));
        Assert.Equal(new byte[] { 10, 0 }, Get("QOBJ"));
        Assert.Equal(new byte[4], Get("FNAM"));
        Assert.Equal("Find the box\0", Encoding.UTF8.GetString(Get("NNAM")));
        Assert.Equal(new byte[4], Get("ANAM"));                                               // no aliases: next free ID 0
    }

    [Fact]
    public void ObjectiveTargetsAreTwelveBytesEach()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var (mod, _, _) = Sample([0, 3]);
        var fields = QuestFields(File.ReadAllBytes(Write(mod, t, game)));
        var qsta = fields.Where(f => f.Sig == "QSTA").Select(f => f.Data).ToList();
        Assert.Equal(2, qsta.Count);
        Assert.Equal(new byte[12], qsta[0]);
        Assert.Equal(new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, qsta[1]);
        var order = string.Join(",", fields.Select(f => f.Sig));
        Assert.Contains("QOBJ,FNAM,NNAM,QSTA,QSTA,ANAM", order);
    }

    [Fact]
    public void VmadHasTheVanillaFragmentShape()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var (mod, q, f) = Sample();
        var vmad = QuestFields(File.ReadAllBytes(Write(mod, t, game))).Single(x => x.Sig == "VMAD").Data;

        var name = f.ScriptName;
        Assert.Equal($"Fragments:Quests:QF_QT_FindTheBox_{q.FormKey.ID:X8}", name);
        Assert.StartsWith("Fragments:Quests:QF_QT_FindTheBox_00", name);
        var want = new MemoryStream();
        var w = new BinaryWriter(want);
        void WStr(string s) { var b = Encoding.ASCII.GetBytes(s); w.Write((ushort)b.Length); w.Write(b); }
        // The layout read from vanilla MQ_TutorialQuest_Misc02 (all 252 bytes matched).
        w.Write((short)6); w.Write((ushort)2); w.Write((ushort)0);          // version, object format, no scripts
        w.Write((byte)3); w.Write((ushort)3);                              // extra bind version, one fragment per stage
        WStr(name); w.Write((byte)0); w.Write((ushort)0);                  // fragment script: Local, no properties
        foreach (ushort s in new ushort[] { 10, 50, 100 })
        {
            w.Write(s); w.Write((short)0); w.Write(0); w.Write((sbyte)1);
            WStr(name); WStr($"Fragment_Stage_{s:D4}_Item_00");
        }
        w.Write((ushort)0);                                                // no alias scripts
        Assert.Equal(want.ToArray(), vmad);
    }

    [Fact]
    public void RecordsReadBackWithStagesAndObjective()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var (mod, _, _) = Sample();
        using var back = game.OpenPlugin(Write(mod, t, game));
        var q = back.Quests.Single();
        Assert.Equal("Find the Box", q.Name?.String);
        Assert.Equal(Quest.Flag.RunOnce, q.Data!.Flags);
        Assert.Equal(new ushort[] { 10, 50, 100 }, q.Stages.Select(s => s.Index));
        Assert.All(q.Stages, s => Assert.Equal(Quests.StageUnknown, s.Unknown));
        Assert.All(q.Stages, s => Assert.Equal(2, s.LogEntries.Single().StageCompleteDatas.Count));
        Assert.Equal(QuestStage.Flag.RunOnStart, q.Stages[0].Flags);
        Assert.Equal(QuestLogEntry.Flag.CompleteQuest, q.Stages[2].LogEntries[0].Flags);
        Assert.Null(q.Stages[1].LogEntries[0].Entry);
        Assert.Equal("I was asked to find a box.", q.Stages[0].LogEntries[0].Entry?.String);
        Assert.Equal("Quest starts", q.Stages[0].LogEntries[0].Note);
        Assert.Equal("Find the box", q.Objectives.Single().DisplayText?.String);
        Assert.Equal(["Fragment_Stage_0010_Item_00", "Fragment_Stage_0050_Item_00", "Fragment_Stage_0100_Item_00"],
            q.VirtualMachineAdapter!.Fragments.Select(x => x.FragmentName));
    }

    [Fact]
    public void BasicFlagsAndFixedKey()
    {
        var mod = Plugin.New("QuestTest.esm", PluginStyle.Light, "test", "quests", localized: true);
        var key = new FormKey(mod.ModKey, 0x900);
        var q = Quests.Basic(mod, "QT_Start", "Start", priority: 80, startGameEnabled: true, runOnce: false, key: key);
        Assert.Same(q, mod.Quests.Single());
        Assert.Equal(key, q.FormKey);
        Assert.Equal(Quest.Flag.StartGameEnabled, q.Data!.Flags);
        Assert.Equal(80, q.Data.Priority);
        Assert.Equal(Localized.Languages.Length, q.Name!.NumLanguages);
        Assert.Null(Quests.Basic(mod, "QT_NoName").Name);
        Assert.Equal(QuestLogEntry.Flag.FailQuest, Quests.Stage(mod, q, 5, "x", end: StageEnd.FailQuest).LogEntries[0].Flags);
    }

    [Fact]
    public void RepeatedIndexesAndMissingStagesThrow()
    {
        var mod = Plugin.New("QuestTest.esm", PluginStyle.Light, "test", "quests");
        var q = Quests.Basic(mod, "QT_Throw");
        Quests.Stage(mod, q, 10);
        Assert.Throws<ArgumentException>(() => Quests.Stage(mod, q, 10));
        Quests.Objective(mod, q, 10, "Do it");
        Assert.Throws<ArgumentException>(() => Quests.Objective(mod, q, 10, "Again"));
        var f = Quests.Fragments(q).ShowObjective(20, 10);
        Assert.Throws<InvalidOperationException>(() => f.Apply());
        Assert.Throws<ArgumentException>(() => Quests.Fragments(Quests.New(mod, "QT Bad Name")));
    }

    [Fact]
    public void SourceHasOneFunctionPerStage()
    {
        var (_, _, f) = Sample();
        var src = f.Source();
        Assert.Contains($"Scriptname {f.ScriptName} Extends Quest Hidden Const", src);
        Assert.Contains("Function Fragment_Stage_0010_Item_00()\r\n;BEGIN CODE\r\nSetObjectiveDisplayed(10)\r\n;END CODE", src);
        Assert.Contains("Function Fragment_Stage_0050_Item_00()\r\n;BEGIN CODE\r\nSetStage(100)\r\n", src);
        Assert.Contains("SetObjectiveCompleted(10)\r\nCompleteQuest()\r\n", src);
        Assert.Equal(3, src.Split("EndFunction").Length - 1);
        Assert.Equal(Path.Combine("Fragments", "Quests", f.ScriptName.Split(':')[2] + ".psc"), f.SourcePath);
    }

    [Fact]
    public void ApplyKeepsAttachedScripts()
    {
        var mod = Plugin.New("QuestTest.esm", PluginStyle.Light, "test", "quests");
        var q = Quests.Basic(mod, "QT_Keep");
        Quests.Stage(mod, q, 10);
        q.VirtualMachineAdapter = new QuestAdapter();
        q.VirtualMachineAdapter.Scripts.Add(new ScriptEntry { Name = "MyQuestScript" });
        Quests.Fragments(q).Line(10, "Debug.Trace(\"QT: stage 10\")").Apply();
        Assert.Equal("MyQuestScript", q.VirtualMachineAdapter.Scripts.Single().Name);
        Assert.Single(q.VirtualMachineAdapter.Fragments);
    }

    /// <summary>A made-up quest with one alias of each kind, in the order location (0), forced (1), find (2),
    /// created in the forced one (3), and an objective that targets the forced alias. Returns the FormKeys the
    /// aliases point at.</summary>
    static (StarfieldMod Mod, Quest Quest, FormKey Location, FormKey Placed, FormKey Keyword, FormKey Misc) AliasSample()
    {
        var mod = Plugin.New("QuestTest.esm", PluginStyle.Light, "test", "quests");
        var kw = mod.Keywords.AddNew("QT_BoxKeyword");
        var loc = mod.Locations.AddNew("QT_BoxLocation");
        var misc = mod.MiscItems.AddNew("QT_BoxNote");
        var cell = new Cell(mod) { EditorID = "QT_BoxCell" };
        var placed = new PlacedObject(mod) { EditorID = "QT_BoxRef" };
        cell.Persistent.Add(placed);
        Fixtures.AddInteriorCell(mod, cell);

        var q = Quests.Basic(mod, "QT_Aliases", "Aliases");
        Quests.Stage(mod, q, 10, startUp: true);
        var where = Quests.LocationAlias(q, "Where", loc.ToLink());
        var box = Quests.ForcedAlias(q, "Box", placed.ToLink(), optional: true);
        var guard = Quests.FindAlias(q, "Guard", kw.ToLink(), actorsOnly: true, closest: true);
        var note = Quests.CreatedAlias(q, "Note", misc.ToLink(), box, inside: true);
        Quests.Objective(mod, q, 10, "Open the box", [box]);
        Assert.Equal([0u, 1u, 2u, 3u], new[] { where.ID, box.ID, guard.ID, note.ID });
        return (mod, q, loc.FormKey, placed.FormKey, kw.FormKey, misc.FormKey);
    }

    /// <summary>The low three bytes of a FormID as written in the file (the high byte is the plugin's own index,
    /// which depends on how the plugin is flagged).</summary>
    static byte[] Id(FormKey k) => BitConverter.GetBytes(k.ID)[..3];
    static byte[] Low(byte[] formId) => formId[..3];

    [Fact]
    public void AliasFieldsComeInTheVanillaOrder()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var (mod, _, _, _, _, _) = AliasSample();
        var order = string.Join(",", QuestFields(File.ReadAllBytes(Write(mod, t, game))).Select(f => f.Sig));
        Assert.EndsWith(
            "QOBJ,FNAM,NNAM,QSTA,ANAM," +
            "ALLS,ALID,FNAM,ALFG,ALFL,ALED," +                       // location: no VTCK
            "ALST,ALID,FNAM,ALFG,ALFR,VTCK,ALED," +                  // forced
            "ALST,ALID,FNAM,ALFG,CTDA,VTCK,ALED," +                  // find by keyword
            "ALST,ALID,FNAM,ALFG,ALCO,ALCA,ALCL,VTCK,ALED",          // created
            order);
    }

    [Fact]
    public void AliasValuesMatchTheCreationKit()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var (mod, _, loc, placed, kw, misc) = AliasSample();
        var fields = QuestFields(File.ReadAllBytes(Write(mod, t, game)));
        byte[] Get(string sig, int n = 0) => fields.Where(f => f.Sig == sig).ElementAt(n).Data;
        byte[] U32(uint v) => BitConverter.GetBytes(v);

        Assert.Equal(U32(4), Get("ANAM"));                                          // next free ID = max + 1
        Assert.Equal(U32(0), Get("ALLS"));
        Assert.Equal(new[] { U32(1), U32(2), U32(3) }, fields.Where(f => f.Sig == "ALST").Select(f => f.Data));
        Assert.Equal(["Where\0", "Box\0", "Guard\0", "Note\0"], fields.Where(f => f.Sig == "ALID").Select(f => Encoding.UTF8.GetString(f.Data)));
        Assert.All(fields.Where(f => f.Sig == "ALFG"), f => Assert.Equal(U32(0), f.Data));
        Assert.All(fields.Where(f => f.Sig == "VTCK"), f => Assert.Equal(U32(0), f.Data));
        Assert.Equal(3, fields.Count(f => f.Sig == "VTCK"));
        // FNAM 0 is the objective's; then one per alias.
        Assert.Equal(new[] { U32(0), U32(0), U32(0x2), U32(0x20 | 0x2000 | 0x40000), U32(0x80000) },
            fields.Where(f => f.Sig == "FNAM").Select(f => f.Data));
        Assert.Equal(Id(loc), Low(Get("ALFL")));
        Assert.Equal(Id(placed), Low(Get("ALFR")));
        Assert.Equal(Id(misc), Low(Get("ALCO")));
        Assert.Equal(new byte[] { 1, 0, 0x00, 0x80 }, Get("ALCA"));                 // in alias 1 (FC_Evidence: 02 00 00 80)
        Assert.Equal(U32(0), Get("ALCL"));
        Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, Get("QSTA")); // objective marker on alias 1

        // HasKeyword (0x230) == 1.0 on the subject, keyword in parameter 1; the CK's padding bytes are written as 0.
        var ctda = Get("CTDA");
        Assert.Equal(32, ctda.Length);
        Assert.Equal(0, ctda[0]);                                                  // EqualTo, no flags
        Assert.Equal(1f, BitConverter.ToSingle(ctda, 4));
        Assert.Equal(0x230, BitConverter.ToUInt16(ctda, 8));
        Assert.Equal(Id(kw), ctda[12..15]);
        Assert.Equal(new byte[4], ctda[16..20]);                                   // parameter 2
        Assert.Equal(new byte[4], ctda[20..24]);                                   // run on Subject
        Assert.Equal(new byte[4], ctda[24..28]);                                   // no reference
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, ctda[28..32]);         // as in every vanilla sample
    }

    [Fact]
    public void AliasesReadBack()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var (mod, _, loc, placed, kw, misc) = AliasSample();
        using var back = game.OpenPlugin(Write(mod, t, game));
        var q = back.Quests.Single();
        Assert.Equal(4, q.Aliases.Count);
        var where = Assert.IsAssignableFrom<IQuestLocationAliasGetter>(q.Aliases[0]);
        Assert.Equal(loc, where.SpecificLocation.FormKey);
        var box = Assert.IsAssignableFrom<IQuestReferenceAliasGetter>(q.Aliases[1]);
        Assert.Equal(placed, box.ForcedReference.FormKey);
        Assert.Equal(AQuestAlias.Flag.Optional, box.Flags);
        var guard = Assert.IsAssignableFrom<IQuestReferenceAliasGetter>(q.Aliases[2]);
        var cond = Assert.IsAssignableFrom<IHasKeywordConditionDataGetter>(guard.Conditions.Single().Data);
        Assert.Equal(kw, cond.FirstParameter.Link.FormKey);
        var note = Assert.IsAssignableFrom<IQuestReferenceAliasGetter>(q.Aliases[3]);
        Assert.Equal(misc, note.CreateReferenceToObject!.Object.FormKey);
        Assert.Equal(CreateReferenceToObject.CreateEnum.In, note.CreateReferenceToObject.Create);
        Assert.Equal(1, note.CreateReferenceToObject.AliasID);
        Assert.Equal(1, q.Objectives.Single().Targets.Single().AliasID);
    }

    [Fact]
    public void AliasMistakesThrow()
    {
        var mod = Plugin.New("QuestTest.esm", PluginStyle.Light, "test", "quests");
        var q = Quests.Basic(mod, "QT_AliasThrow");
        var other = Quests.Basic(mod, "QT_Other");
        var kw = mod.Keywords.AddNew("QT_Kw").ToLink();
        var a = Quests.FindAlias(q, "Guard", kw);
        Assert.Throws<ArgumentException>(() => Quests.FindAlias(q, "Guard", kw));          // same name
        Assert.Throws<ArgumentException>(() => Quests.FindAlias(q, " ", kw));              // no name
        var b = Quests.FindAlias(other, "Elsewhere", kw);
        Assert.Throws<ArgumentException>(() => Quests.CreatedAlias(q, "Note", kw, b));     // alias of another quest
        Assert.Equal(1u, Quests.NextAliasId(q));
        Assert.Equal(0u, a.ID);
        // Default flags: loaded area only; not optional.
        Assert.Equal(AQuestAlias.Flag.MatchingRefInLoadedArea, a.Flags);
    }

    [Fact]
    public void NextAliasIdSkipsGaps()
    {
        var mod = Plugin.New("QuestTest.esm", PluginStyle.Light, "test", "quests");
        var q = Quests.Basic(mod, "QT_Gaps");
        q.Aliases!.Add(new QuestReferenceAlias { ID = 7, Name = "Old" });
        Assert.Equal(8u, Quests.LocationAlias(q, "Next", mod.Locations.AddNew("QT_Loc").ToLink()).ID);
        Assert.Equal(9u, Quests.NextAliasId(q));
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var anam = QuestFields(File.ReadAllBytes(Write(mod, t, game))).Single(f => f.Sig == "ANAM").Data;
        Assert.Equal(BitConverter.GetBytes(9u), anam);                            // max ID + 1, not the alias count
    }
}
