// BethesdaFields (BuildKit 0.3.0): fields Bethesda always writes, filled on own records only; checked on raw bytes.
using System.Buffers.Binary;
using System.IO.Compression;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Xunit;

namespace BuildKit.Tests;

public class BethesdaFieldsTests
{
    /// <summary>Records of the file: (sig, flags, form version, subrecords as (type, data)), decompressed.</summary>
    static List<(string Sig, uint Flags, ushort FormVersion, List<(string, byte[])> Subs)> Records(string path, bool header = false)
    {
        var b = File.ReadAllBytes(path);
        var list = new List<(string, uint, ushort, List<(string, byte[])>)>();
        void Walk(int pos, int end)
        {
            while (pos + 24 <= end)
            {
                var sig = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos + 4));
                if (sig == "GRUP") { Walk(pos + 24, pos + size); pos += size; continue; }
                var flags = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos + 8));
                var fv = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pos + 20));
                var body = b.AsSpan(pos + 24, size).ToArray();
                if ((flags & 0x40000) != 0)
                {
                    using var z = new ZLibStream(new MemoryStream(body, 4, body.Length - 4), CompressionMode.Decompress);
                    using var ms = new MemoryStream(); z.CopyTo(ms); body = ms.ToArray();
                }
                var subs = new List<(string, byte[])>();
                for (var i = 0; i + 6 <= body.Length;)
                {
                    var t = System.Text.Encoding.ASCII.GetString(body, i, 4);
                    var n = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(i + 4));
                    subs.Add((t, body.AsSpan(i + 6, n).ToArray()));
                    i += 6 + n;
                }
                if (header || sig != "TES4") list.Add((sig, flags, fv, subs));
                pos += 24 + size;
            }
        }
        Walk(0, b.Length);
        return list;
    }

    static byte[]? Sub(List<(string T, byte[] D)> subs, string t) => subs.Where(s => s.T == t).Select(s => s.D).FirstOrDefault();

    static StarfieldMod Sample(string name)
    {
        var mod = Plugin.New(name, PluginStyle.Light, "test", "Bethesda fields test");
        mod.Messages.AddNew("Msg").Description = "hello";
        mod.FormLists.AddNew("List");
        mod.Ingestibles.AddNew("Aid").Model = new Model { File = "Meshes\\x.nif" };
        mod.Keywords.AddNew("Kw");
        mod.StoryManagerQuestNodes.AddNew("Node");
        mod.Npcs.AddNew("Npc");
        var cell = new Cell(mod) { EditorID = "InCell", Flags = Cell.Flag.IsInteriorCell };
        Fixtures.AddInteriorCell(mod, cell);
        return mod;
    }

    [Fact]
    public void OwnRecordsGetBethesdasValues()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var path = PluginWriter.Write(Sample("Fields.esm"), t.Path, game, Fixtures.NoGapCheck);
        var recs = Records(path);
        var rec = (string sig) => recs.Single(r => r.Sig == sig);

        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, Sub(rec("MESG").Subs, "BNAM"));
        Assert.Equal(new byte[4], Sub(rec("FLST").Subs, "ANAM"));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(Sub(rec("ALCH").Subs, "FLLD")));
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0x00 }, Sub(rec("KYWD").Subs, "CNAM"));
        Assert.Equal(new byte[4], Sub(rec("KYWD").Subs, "FNAM"));
        Assert.NotNull(Sub(rec("KYWD").Subs, "TNAM"));
        Assert.Equal(new byte[4], Sub(rec("SMQN").Subs, "XNAM"));
        Assert.True((rec("NPC_").Flags & 0x40000) != 0);
        Assert.All(recs, r => Assert.NotEqual(BethesdaFields.MutagenDefaultFormVersion, r.FormVersion));

        var tes4 = Records(path, header: true).First();
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(Sub(tes4.Subs, "INCC")));

        using var back = game.OpenPlugin(path);   // Mutagen reads it all back
        Assert.Equal("Npc", back.Npcs.Single().EditorID);
        Assert.Equal(0xFFFFFFFFu, back.Messages.Single().BNAM);
    }

    [Fact]
    public void BuilderValuesAndOverridesAreKept()
    {
        using var t = new TempDir();
        // A master with a message that has no BNAM; the plugin overrides it and also sets BNAM 0 on its own message.
        var masterMod = Plugin.New("FieldsMaster.esm", PluginStyle.Full, "test", "master");
        masterMod.Messages.AddNew("MasterMsg").Description = "m";
        using (var g0 = new GameData(t.Path))
            PluginWriter.Write(masterMod, t.Path, g0, new WriteOptions { GapCheck = false, FillBethesdaFields = false });
        using var game = new GameData(t.Path);
        var master = game.Open("FieldsMaster.esm");
        var mod = Plugin.New("FieldsKeep.esm", PluginStyle.Light, "test", "keep");
        var own = mod.Messages.AddNew("OwnMsg");
        own.Description = "o";
        own.BNAM = 0;
        own.FormVersion = 581;
        mod.Messages.GetOrAddAsOverride(master.Messages.Single()).Description = "changed";
        var path = PluginWriter.Write(mod, t.Sub("out"), game, Fixtures.NoGapCheck);

        var msgs = Records(path).Where(r => r.Sig == "MESG").ToList();
        Assert.Equal(2, msgs.Count);
        Assert.Contains(msgs, r => Sub(r.Subs, "BNAM") is { } v && v.SequenceEqual(new byte[4]) && r.FormVersion == 581);   // own, as set
        Assert.Contains(msgs, r => Sub(r.Subs, "BNAM") == null);   // the override: untouched
    }

    /// <summary>Every group header of the file: (type, word at 16, word at 20).</summary>
    static List<(uint Type, uint Stamp, uint Word2)> Groups(string path)
    {
        var b = File.ReadAllBytes(path);
        var list = new List<(uint, uint, uint)>();
        var pos = 0;
        while (pos + 24 <= b.Length)
        {
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos + 4));
            if (System.Text.Encoding.ASCII.GetString(b, pos, 4) == "GRUP")
            {
                list.Add((BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos + 12)),
                    BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos + 16)),
                    BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos + 20))));
                pos += 24;   // step into the group
            }
            else pos += 24 + size;
        }
        return list;
    }

    /// <summary>A plugin whose groups carry Creation Kit style stamps, as a rebuild copied from a CK-saved plugin does:
    /// a top-level group, a cell block and sub-block, the cell's children and temporary groups, a quest's children
    /// group and a topic's children group.</summary>
    static StarfieldMod Stamped(string name)
    {
        var mod = Plugin.New(name, PluginStyle.Light, "test", "group stamps test");
        mod.Keywords.AddNew("Kw");
        mod.Keywords.LastModified = 0x3174; mod.Keywords.Unknown = 1;
        var cell = new Cell(mod) { EditorID = "StampCell", Flags = Cell.Flag.IsInteriorCell, Timestamp = 0x3174, TemporaryTimestamp = 0x3174, TemporaryUnknownGroupData = 1 };
        cell.Temporary.Add(new PlacedObject(mod) { EditorID = "StampRef" });
        var block = new CellBlock
        {
            GroupType = GroupTypeEnum.InteriorCellBlock, LastModified = 0x3174,
            SubBlocks = new Noggog.ExtendedList<CellSubBlock>
            {
                new CellSubBlock { GroupType = GroupTypeEnum.InteriorCellSubBlock, LastModified = 0x3174, Cells = new Noggog.ExtendedList<Cell> { cell } },
            },
        };
        mod.Cells.Add(block);
        var q = Quests.New(mod, "StampQuest");
        q.Timestamp = 0x323E;
        var topic = new DialogTopic(mod) { EditorID = "StampTopic", Timestamp = 0x323E };
        topic.Responses.Add(new DialogResponses(mod));
        q.DialogTopics.Add(topic);
        return mod;
    }

    [Fact]
    public void GroupStampsAreZeroed()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var path = PluginWriter.Write(Stamped("Stamps.esm"), t.Path, game, Fixtures.NoGapCheck);
        var groups = Groups(path);
        foreach (var type in new uint[] { 0, 2, 3, 6, 7, 9, 10 })   // top, block, sub-block, cell, topic, temporary, quest
            Assert.True(groups.Any(g => g.Type == type), $"no group of type {type}; types: {string.Join(",", groups.Select(g => g.Type))}");
        Assert.All(groups, g => Assert.True(g.Stamp == 0 && g.Word2 == 0, $"group type {g.Type}: {g.Stamp:X8} {g.Word2:X8}"));
        using var back = game.OpenPlugin(path);   // Mutagen reads it back
        Assert.Equal("StampRef", back.Cells.Single().SubBlocks.Single().Cells.Single().Temporary.Single().EditorID);

        var off = PluginWriter.Write(Stamped("StampsOff.esm"), t.Sub("off"), game, new WriteOptions { GapCheck = false, ZeroGroupStamps = false });
        var kept = Groups(off);
        Assert.Contains(kept, g => g.Type == 0 && g.Stamp == 0x3174 && g.Word2 == 1);
        Assert.Contains(kept, g => g.Type == 9 && g.Stamp == 0x3174 && g.Word2 == 1);
        Assert.Contains(kept, g => g.Type == 2 && g.Stamp == 0x3174);
        Assert.Contains(kept, g => g.Type == 3 && g.Stamp == 0x3174);
        Assert.Contains(kept, g => g.Type == 6 && g.Stamp == 0x3174);
        Assert.Contains(kept, g => g.Type == 7 && g.Stamp == 0x323E);
        Assert.Contains(kept, g => g.Type == 10 && g.Stamp == 0x323E);
    }

    [Fact]
    public void TurnedOffChangesNothing()
    {
        using var t = new TempDir();
        using var game = new GameData(t.Path);
        var path = PluginWriter.Write(Sample("FieldsOff.esm"), t.Path, game, new WriteOptions
        {
            GapCheck = false, FillBethesdaFields = false, CkFormVersion = false, CompressNpcs = false, InteriorCellCount = false,
        });
        var recs = Records(path);
        Assert.Null(Sub(recs.Single(r => r.Sig == "MESG").Subs, "BNAM"));
        Assert.Null(Sub(recs.Single(r => r.Sig == "FLST").Subs, "ANAM"));
        Assert.True((recs.Single(r => r.Sig == "NPC_").Flags & 0x40000) == 0);
        Assert.Contains(recs, r => r.FormVersion == BethesdaFields.MutagenDefaultFormVersion);
    }
}
