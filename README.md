# BuildKit

BuildKit is a small C# library for programs that build Starfield plugins (`.esm` files) with
[Mutagen](https://github.com/Mutagen-Modding/Mutagen). It holds the setup code such a builder otherwise copies from
project to project: opening masters with the right flags, writing the plugin the way the Creation Kit does, filling
effect data Mutagen leaves out, keeping FormIDs fixed between builds, and adding a "Show Debug Messages" option.

It targets **Mutagen.Bethesda.Starfield 0.54.4** exactly. Each BuildKit release says which Mutagen version it was
tested with; a new Mutagen release gets a new BuildKit release.

## Install

```
dotnet add package Taosecurity.Starfield.BuildKit
```

BuildKit needs .NET 9 and brings Mutagen.Bethesda.Starfield 0.54.4 with it.

## Quick start

```csharp
using BuildKit;
using Mutagen.Bethesda;            // AddNew and other group helpers
using Mutagen.Bethesda.Starfield;

var dataDir = @"C:\Program Files (x86)\Steam\steamapps\common\Starfield\Data";
using var game = new GameData(dataDir);
game.Open("Starfield.esm");                      // plus any other master, e.g. game.Open("SFBGS003.esm")

var mod = Plugin.New("MyMod.esm", PluginStyle.Light, "Your Name", "My Mod 0.1.0");
var kw = mod.Keywords.AddNew("MM_Keyword");      // ... your records ...
var vanilla = game.Get<IKeywordGetter>("SomeVanillaKeyword");   // EditorID lookups across the open masters

var dbg = GameplayOptions.Debug(mod, "MM_Option_Debug", "My Mod's log lines on screen");
GameplayOptions.Group(mod, "MM_Options", "My Mod", [dbg]);

Console.WriteLine(IdLock.Apply(mod, "ids.lock.json"));   // commit ids.lock.json with your builder
PluginWriter.Write(mod, "out", game);                    // writes out\MyMod.esm
```

## What's in it

| Type | What it does |
|---|---|
| `GameData` | Opens masters from the Data folder (and optional extra folders searched first). Each master's own masters are opened before it, with their light, medium or full flags, so FormIDs resolve correctly. Gives EditorID lookups (`Get<T>`, where a later master wins), a link cache, and `OpenPlugin` to read back a plugin you just wrote. |
| `Plugin` | `Plugin.New(fileName, style, author, description, localized)` makes a master plugin with the Creation Kit's header (Master plus Light or Medium flag, form version 582). `IdRange(style)` gives the FormIDs a style may use. |
| `PluginWriter` | `Write(mod, outDir, game, options)` fills effect data, writes ONAM like the Creation Kit, takes master flags from the open masters, lists masters in load order and writes on one thread, so the same build gives the same bytes. See the options below. |
| `Effects` | `New(baseEffect, magnitude, area, duration)` makes an effect entry. `Fix(list)` and `FixAll(mod)` fill a missing EFIF (0) and MUID (position + 1, never a duplicate). Without them, effects on spells, enchantments and ingestibles never apply in game. |
| `IdLock` | `Apply(mod, lockPath)` keeps every own record's FormID the same from build to build, using a JSON file of EditorID to FormID. New records get the next free ID. The build stops if a locked record disappears, if two records share an EditorID, or if a record has no EditorID. `update: false` only checks. |
| `GameplayOptions` | `Debug(...)` makes a "Show Debug Messages" option, off by default, for Settings > Gameplay Options. `Bool(...)` makes other on/off options, and `Group(...)` makes the mod's own options group. Create the options first and the group last. |
| `Localized` | `Text(s)` gives the same text in all nine languages the game ships, for localized plugins. |

### Write options

| Option | Default | Meaning |
|---|---|---|
| `OverriddenForms` | `Iterate` | Fill the header's ONAM list like the Creation Kit. Mutagen 0.54.4's own default writes none, and the game then ignores a plugin's edits of temporary references in its masters' cells. |
| `Deterministic` | `true` | Write on one thread. With parallel writing, Mutagen 0.54.4 can number a localized plugin's strings differently on every run. |
| `FixEffects` | `true` | Run `Effects.FixAll` before writing. |
| `MasterOrder` | open order | The order masters are listed in the header. |
| `Masters` | none | An explicit master list, written exactly as given, even masters no record points into (for patches that list a master only to load after it). |
| `MastersListContent` | Mutagen's | Lower-level form of `Masters`: `NoCheck` writes the header's `MasterReferences` as you filled them. |
| `NextFormId` | Mutagen's | `NoCheck` keeps the Next Object ID you set in the header instead of recomputing it. |
| `GapCheck` | `true` | After writing, run `mutagen-gap-check.py` on the plugin if the script is found (see below). |
| `GapCheckScript` | none | Path to that script. |
| `GapCheckMastersDir` | none | Extra folder the gap check searches for masters. |
| `FailOnGapErrors` | `true` | Throw when the gap check reports an error. |

### The gap check

The gap check is an optional Python script that compares a written plugin with its masters and reports data Mutagen
0.54.4 is known to lose on write, plus missing ONAM entries. It isn't part of this package. BuildKit runs it when it
finds `mutagen-gap-check.py` (from `WriteOptions.GapCheckScript`, the `BUILDKIT_TOOLS` environment variable, or a
`tools` folder above the builder); otherwise it prints one "skipped" line and carries on.

## Same bytes every build

A builder on BuildKit gives byte-identical output for the same inputs: one-thread writing fixes string numbering, and
the ID lock fixes FormIDs. That makes it easy to prove a refactor changed nothing: build before and after, then compare
the files.

## Tests

```
dotnet test tests/BuildKit.Tests
```

The tests make every plugin they need in a temp folder. No game files are needed or included.

## Privacy

BuildKit collects nothing and makes no network calls. See [PRIVACY.md](PRIVACY.md).

## License

GPL-3.0 (see [LICENSE](LICENSE)), the same license as Mutagen. BuildKit doesn't include or need any Bethesda files;
it reads the plugins in your own game install.
