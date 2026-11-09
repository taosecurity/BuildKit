// Small plugins written by the tests themselves into a temp folder (no game files are used anywhere).
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Noggog;

namespace BuildKit.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("buildkit-test-").FullName;
    public string Sub(string name) { var p = System.IO.Path.Combine(Path, name); Directory.CreateDirectory(p); return p; }
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

public static class Fixtures
{
    public static readonly WriteOptions NoGapCheck = new() { GapCheck = false };

    /// <summary>Base.esm: a full master with a global "Shared" (value 1), a global "BaseOnly", and an interior
    /// cell "BaseCell" holding one temporary placed object. Returns (cell, placed object) FormKeys.</summary>
    public static (FormKey Cell, FormKey Placed) WriteBase(string dir)
    {
        var mod = Plugin.New("Base.esm", PluginStyle.Full, "test", "base master");
        var shared = mod.Globals.AddNew("Shared"); shared.Data = 1;
        mod.Globals.AddNew("BaseOnly");
        var cell = new Cell(mod) { EditorID = "BaseCell" };
        var placed = new PlacedObject(mod) { EditorID = "BasePlaced" };
        cell.Temporary.Add(placed);
        AddInteriorCell(mod, cell);
        using var game = new GameData(dir);
        PluginWriter.Write(mod, dir, game, NoGapCheck);
        return (cell.FormKey, placed.FormKey);
    }

    /// <summary>Light.esm: a light master on Base.esm that overrides "Shared" (value 2) and adds "LightOnly".</summary>
    public static void WriteLight(string dir, string? baseDir = null)
    {
        using var game = new GameData(baseDir ?? dir);
        var b = game.Open("Base.esm");
        var mod = Plugin.New("Light.esm", PluginStyle.Light, "test", "light master");
        var over = mod.Globals.GetOrAddAsOverride(b.Globals.First(g => g.EditorID == "Shared"));
        over.Data = 2;
        mod.Globals.AddNew("LightOnly");
        PluginWriter.Write(mod, dir, game, NoGapCheck);
    }

    public static void AddInteriorCell(StarfieldMod mod, Cell cell) =>
        mod.Cells.Add(new CellBlock
        {
            SubBlocks = new ExtendedList<CellSubBlock> { new CellSubBlock { Cells = new ExtendedList<Cell> { cell } } },
        });

    public static IStarfieldModDisposableGetter Read(GameData game, string path) => game.OpenPlugin(path);
}
