# BuildKit: instructions for AI assistants

Plain steps for a model helping someone build a Starfield plugin with BuildKit. Human readers: see [README.md](README.md).

## 1. What it is and when to use it

BuildKit is a C# library (NuGet package `Taosecurity.Starfield.BuildKit`) for programs that write Starfield plugins
(`.esm`) with Mutagen. Use it when you write or maintain a Mutagen builder for Starfield: it opens masters with the
right flags, writes the plugin the way Bethesda and the Creation Kit do (ONAM, EFIF/MUID, SNAM, Bethesda-style FE/FD
FormIDs and fields), keeps FormIDs fixed between builds, adds a "Show Debug Messages (PC only)" Gameplay Option, makes Message
records for on-screen text that also works on Xbox and PlayStation, and builds quests (stages, objectives, aliases,
a simple scene and the stage fragment script) in the Creation Kit's layout.

Don't use it for: editing a plugin by hand (use xEdit or the Creation Kit), other games, or any Mutagen version other
than 0.54.4.

## 2. Prerequisites

1. .NET SDK 9 or later (`dotnet --version` prints 9.x or higher; checked with 10.0.404).
2. Mutagen.Bethesda.Starfield 0.54.4 exactly. The package brings it in; don't add another version.
3. A Starfield install, only for builders that read the game's masters. The tests need no game files.

## 3. Install

In a new or existing .NET 9 (or later) console project:

```
dotnet new console -n MyBuilder
cd MyBuilder
dotnet add package Taosecurity.Starfield.BuildKit --version 0.6.1
```

## 4. Prove the install works

Replace `Program.cs` with:

```csharp
using BuildKit;
var mod = Plugin.New("MyMod.esm", PluginStyle.Light, "Your Name", "My Mod 0.1.0");
var dbg = GameplayOptions.Debug(mod, "MM_Option_Debug", "My Mod's log lines on screen");
GameplayOptions.Group(mod, "MM_Options", "My Mod", [dbg]);
using var game = new GameData(Directory.CreateDirectory("data").FullName);   // an empty Data folder is enough here
PluginWriter.Write(mod, "out", game);
Console.WriteLine(File.Exists(Path.Combine("out", "MyMod.esm")) ? "OK" : "FAILED");
```

Then run `dotnet run`. It prints any `BuildKit:` lines, a "gap check: NOT RUN" line and `OK`; `out\MyMod.esm`
exists. The "gap check: NOT RUN" line (on standard error) is expected when `sf-mod-check` isn't installed: the gap
check runs the separate tool `sf-mod-check gap` (found by `WriteOptions.GapCheckExe`, the `BUILDKIT_SFMODCHECK`
variable, or PATH; install it with `dotnet tool install -g sf-mod-check`, or from github.com/taosecurity/sf-mod-check/releases), and the plugin is written either way. With it
installed, the line is replaced by the check's own `gap check:` output ending in `RESULT: OK`. No game files are needed for this; a real builder points `GameData` at the game's Data folder.

## 5. Common tasks

1. Open masters by EditorID: `using var game = new GameData(@"<Starfield>\Data"); game.Open("Starfield.esm");`
   then `game.Get<IKeywordGetter>("SomeEditorID")`. Pass `game` to `PluginWriter.Write(mod, "out", game)`.
2. Keep FormIDs the same between builds: `Console.WriteLine(IdLock.Apply(mod, "ids.lock.json"));` before writing,
   and commit `ids.lock.json` with the builder. The first run creates the file; `Apply` returns a one-line summary
   ("ID lock: 4 records, 4 new, ...") for you to print.
3. Turn a write option off: `PluginWriter.Write(mod, "out", game, new WriteOptions { BethesdaFormIds = false });`
   The options and their defaults are in README.md, "Write options".
4. Run the library's tests from a clone of the repo: `dotnet test tests/BuildKit.Tests`.
5. On-screen text that also works on Xbox and PlayStation: `Messages.Hud(mod, "MM_Msg_X", "Text %.0f", 3)` (3 = seconds
   on screen) or `Messages.Box(mod, "MM_Msg_Y", "Text")`, shown in Papyrus with `Message.Show()`. The number for the
   `%.0f` slot is passed to `Show()` in Papyrus, not here. Never use `Debug.Notification`
   or `Debug.MessageBox` for text the player must see: the Debug script does nothing on consoles. Don't put a
   literal `%` in message text (it starts a number slot; write "percent"). README.md, "On-screen text that works on
   Xbox and PlayStation", has the Papyrus side.
6. Localized plugin: make it with `Plugin.New(..., localized: true)`. `Messages` and `GameplayOptions` then put
   their text in all nine languages by default; pass `text:` only to change that. For the names and descriptions
   of your own records, use `Localized.For(mod, "Text")`: all nine languages on a localized plugin, plain text on
   any other, or your own function with `Localized.For(mod, "Text", text: myFunction)`.
7. A quest: `var q = Quests.Basic(mod, "MM_Quest", "Name", startGameEnabled: true);`, then
   `Quests.Stage(mod, q, 10, log: "Log text")` per stage (`startUp: true` on the first, `end: StageEnd.CompleteQuest`
   on the last), aliases with `Quests.ForcedAlias`, `FindAlias`, `CreatedAlias` or `LocationAlias` (records passed as
   `record.ToLink()`), `Quests.Objective(mod, q, 10, "Text", [alias])`, and optionally
   `Quests.Scene(mod, q, "MM_Scene", aliasA, aliasB, [new SceneLine(0, "Hi."), new SceneLine(1, "Hello.")])`.
   Link stages with `var f = Quests.Fragments(q).ShowObjective(10, 10).CompleteQuest(20); f.Apply();`, write
   `f.Source()` to `Scripts\Source\` + `f.SourcePath`, and compile it with the mod's other Papyrus scripts (the
   `.pex` ships in `Scripts\Fragments\Quests\`). Without that `.pex`, `sf-mod-check check` reports the script
   missing and the stages do nothing. Call `f.Apply()` before `IdLock.Apply`. README.md, "Quests", has a full
   example.

## 6. Inputs, outputs, exit codes, log

- Inputs: the `mod` your builder made; masters read from the Data folder you give `GameData`.
- Output: `<out>\<ModName>.esm`. Nothing else is written, apart from `ids.lock.json` when you call `IdLock.Apply`.
  (The proof program in step 4 also makes an empty `data` folder itself.)
- Exit codes: BuildKit is a library and never exits the process. Problems are exceptions whose message names the
  file or records involved; let them reach your `Main` so the builder exits non-zero.
- Log: BuildKit writes its decisions to standard output, one line each, starting `  BuildKit:` (fields filled,
  FormIDs changed, group headers zeroed) or `  gap check:`. The `ID lock:` line is the summary your own program
  prints from `IdLock.Apply`. To keep a log, redirect it:
  `dotnet run > build.log 2>&1`.

## 7. Limits

- Never changes or deletes game files or masters; it only reads them.
- Makes no network calls and uploads nothing (PRIVACY.md).
- Writes only the plugin you ask for. It doesn't pack BA2 archives, compile Papyrus or make zips. `Quests.Fragments`
  returns the fragment script's source as text; your builder writes and compiles it.
- Quest scenes have no voice or lip files and use only dialogue actions. Quests built with the helpers pass
  `sf-mod-check gap` and `check` but have not yet been tested in game.
- Mutagen 0.54.4 only. A newer Mutagen needs a new BuildKit release.
- Call `PluginWriter.Write` from one thread at a time; don't write several plugins in parallel in one process.
