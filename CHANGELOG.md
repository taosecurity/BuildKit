# Changelog

## 0.6.1 (2026-11-25)

### What's new

Two fixes, no change to the API. The README's "More examples" section is back: the 0.6.0 README rewrite dropped it by mistake. It holds three worked examples, "An add-on for another author's mod", "A magic effect and a spell" and "A script on the player alias of a quest", under the same headings as before, so old links to them work again. The message printed when `sf-mod-check` is not found (and the README and AGENTS.md) now says `dotnet tool install -g sf-mod-check`, with the releases page as the second way.

### Example

The three restored examples were built as they stand in the README against the Steam copy of Starfield.esm, with a made-up `AuthorsMod.esm` standing in for the other author's plugin. Each wrote its plugin without an error. The effect example printed:

      BuildKit: EFIF/MUID filled on 1 effect entries

and the add-on example printed `ID lock: 2 records, 2 new, 0 moved back to their locked IDs (ids.lock.json)`. All three plugins passed `sf-mod-check gap` with 0 errors and 0 warnings. The player-alias example changed in one way: it now takes the next free alias ID from `Quests.NextAliasId` and keeps any script data the quest already has, so it works next to the 0.6.0 `Quests` helpers (aliases and fragments made with them are kept).

### Changes

- README: "More examples" restored (three examples, checked against 0.6.0). The player-alias example uses `Quests.NextAliasId` and keeps the quest's existing script data.
- The sf-mod-check install hint (README, AGENTS.md and the `InstallHint` message) names `dotnet tool install -g sf-mod-check`.

## 0.6.0 (2026-11-25)

### What's new

Quest helpers: a builder can now make a working quest without raw record work. `Quests.Basic`, `Quests.Stage`, `Quests.Objective`, four alias helpers, `Quests.Scene` and `Quests.Fragments` write stages, objectives, aliases, a simple two-actor scene and the stage fragment script with the field order and fixed values the Creation Kit writes on vanilla quests. Existing builders keep working unchanged.

### Example

A made-up quest with three stages, one objective, all four kinds of alias, a three-line scene and its fragment script (the README's "Quests" example, with its own keyword, location, item and placed box) was written against the Steam copy of Starfield.esm. Its compiled fragment script sat beside the plugin. The build printed:

      BuildKit: Bethesda fields filled: 0 message back-out index, 0 form list add-to-list, 0 model light layer, 1 keyword, 0 story-manager quest node
      BuildKit: form version 582 set on 5 own records left at Mutagen's 576
      BuildKit: header interior cell count (INCC) set to 1 (was unset)
      BuildKit: own records written in Bethesda's FE FormID form (light plugin)
      gap check: running D:\Tools\sf-mod-check\sf-mod-check.exe gap
      gap check: BKQuestE2E.esm: 13 own records, 0 overrides, 0 compared with their master's copy
      gap check:   0 error(s), 0 warning(s)
      gap check: RESULT: OK (0 errors, 0 warnings)

and `sf-mod-check check` on the plugin then printed `RESULT: OK, 0 errors, 0 warnings (BKQuestE2E.esm: 13 records, 15 links)`.

### Changes

- `Quests.Basic(mod, editorId, name, priority, startGameEnabled, runOnce)` makes a quest with its name and its flags and priority (DNAM; RunOnce by default, as on vanilla quests).
- `Quests.Stage(mod, quest, index, log, startUp, end, note)` adds a stage with exactly one log entry, as the Creation Kit writes it: the player's log text, an editor-only note, a start-up flag and `StageEnd.CompleteQuest` or `FailQuest`. Stages stay sorted by index.
- `Quests.Objective(mod, quest, index, displayText, targets)` adds an objective with one quest marker target per alias (or alias ID).
- `Quests.ForcedAlias`, `FindAlias` (by keyword, with actors-only and closest), `CreatedAlias` (at or inside another alias) and `LocationAlias` add aliases with the Creation Kit's numbering; `optional: true` lets the quest start when the alias can't be filled. `Quests.NextAliasId` gives the next free ID.
- `Quests.Scene(mod, quest, editorId, first, second, lines, endStage, beginOnQuestStart)` with `SceneLine(Actor, Text)` makes a two-actor scene: one phase, one dialogue action and one scene topic with one response per line, and the stage to set when it ends. The topics and responses get the EditorIDs `<scene>_Line01` and `<scene>_Line01_Info` and so on, so `IdLock` can lock them.
- `Quests.Fragments(quest)` collects the stage links (`ShowObjective`, `CompleteObjective`, `SetStage`, `CompleteQuest` or any Papyrus `Line`). `Apply()` writes the fragment entries into the quest, and `Source()` returns the fragment script (`ScriptName`, `SourcePath`) for the builder to compile with its own scripts.
- The helpers throw on mistakes they can see (a repeated stage, objective, alias or scene, an alias from another quest, a missing end stage or fragment stage, an EditorID that can't name a script), with a message that names the fix.
- A blank or whitespace `WriteOptions.GapCheckExe` now counts as not set, so the gap check falls back to `BUILDKIT_SFMODCHECK` and then PATH, as a blank variable already did. Before, it threw.
- `IdLock.Apply` can now move records that live inside another record or in a cell block (a quest's scene topics, responses and scenes, cells and their placed references, our own references in an override of a vanilla cell, a worldspace's top cell) back to their locked FormIDs. Before, a later build that shifted their IDs (for example by adding a record ahead of them) stopped with "Unknown major record type".
- `Quests.NextAliasId` and the alias helpers count the IDs inside a collection alias added by hand, instead of throwing.
- 24 new tests (112 in all) compare the helpers' raw output with the vanilla layouts by bytes and check that a quest with a scene, and a cell with a reference, keep their FormIDs through `IdLock`.

### Not yet

- No quest built with the helpers has been played in game yet. Scenes have no voice or lip files, and use only dialogue actions.
- Collection aliases, quest-type and other optional quest fields, and scene package actions are not covered.

## 0.5.0 (2026-11-23)

### What's new

The gap check now runs `sf-mod-check gap`, from the public sf-mod-check tool (0.15.0 or later), instead of the private `mutagen-gap-check.py` script. It is never skipped silently: when sf-mod-check can't be found, BuildKit prints one line that names the fix. The "Show Debug Messages (PC only)" option and every other on/off option now carry the two value rows the Creation Kit writes.

Plugins with a Gameplay Options on/off option are 81 bytes larger when built again with 0.5.0 (the eight added fields). A localized plugin's string IDs after the option's text shift too.

### Example

This is the output of the README's builder, run with BuildKit 0.5.0 and sf-mod-check 0.15.0 installed as a .NET global tool (so it is on the PATH) against the Steam copy of Starfield.esm. It wrote an 883-byte `out\MyMod.esm`:

    > dotnet run
    ID lock: 4 records, 4 new, 0 moved back to their locked IDs (ids.lock.json)
      BuildKit: Bethesda fields filled: 0 message back-out index, 0 form list add-to-list, 0 model light layer, 1 keyword, 0 story-manager quest node
      BuildKit: form version 582 set on 1 own records left at Mutagen's 576
      BuildKit: header interior cell count (INCC) set to 0 (was unset)
      BuildKit: own records written in Bethesda's FE FormID form (light plugin)
      gap check: running C:\Users\Me\.dotnet\tools\sf-mod-check.exe gap
      gap check: MyMod.esm: 4 own records, 0 overrides, 0 compared with their master's copy
      gap check:   0 error(s), 0 warning(s)
      gap check: RESULT: OK (0 errors, 0 warnings)
      gap check: sf-mod-check 0.15.0 by Richard Bejtlich - GPL-3.0 - https://github.com/taosecurity/sf-mod-check

Without sf-mod-check, the same build prints this instead of the five `gap check:` lines, and still writes the plugin:

      gap check: NOT RUN (sf-mod-check was not found). Get it from github.com/taosecurity/sf-mod-check/releases (unzip the exe and put its folder on PATH, or install the .nupkg as a .NET global tool), or set BUILDKIT_SFMODCHECK or WriteOptions.GapCheckExe to its path.

### Changes

- The gap check looks for sf-mod-check in `WriteOptions.GapCheckExe`, then the `BUILDKIT_SFMODCHECK` environment variable, then PATH (on Windows, only `sf-mod-check.exe` is found on PATH). A path named by the option or the variable must be a full path to an existing program, or the build throws naming the fix. `PluginWriter.GapCheck` called directly reads the variable and PATH the same way, and `PluginWriter.ResolveSfModCheck` is the public form of that lookup. It passes the Data folder as `--data` and `GapCheckMastersDir` as `--masters-dir`, and any nonzero exit throws when `FailOnGapErrors` is on. Eight new tests cover it, most with a stub exe.
- Breaking for builders that used the old gap check: `WriteOptions.GapCheckScript`, `PluginWriter.GapCheckScriptName` and `PluginWriter.FindPython` are gone. Use `GapCheckExe` instead of `GapCheckScript`. The fourth argument of `PluginWriter.GapCheck` is now `exe`, the sf-mod-check program, instead of `script`; a `.py` path there or in `GapCheckExe` throws an error that says so. The gap check no longer looks in `BUILDKIT_TOOLS` for a Python script or for Python itself, and BuildKit no longer runs Python. `PluginWriter.TryFindTool` and `FindTool` still use `BUILDKIT_TOOLS` for a builder's own helper files.
- When the gap check exits 1 the error says it found errors; any other nonzero exit says it could not check (a missing master, or an sf-mod-check older than 0.15.0).
- `GameplayOptions.Bool` and `GameplayOptions.Debug` write the two value rows (`VOVS`, `VORV`, `RESN`, `VORN`: DISABLE at 0, ENABLE at 1) that the Creation Kit writes on every on/off option, and no `GPOD` (the Creation Kit writes that only on sliders; all of Bethesda's own options are sliders). A byte compare against a Creation Kit-made option from another author shows the same eight fields. These fixed values never go through a builder's `text:` function. Three new tests check the written fields.

### Not yet

- The new rows haven't been looked at in game on a rebuilt mod; the menu should still show the switch and its text.

### Download

`dotnet add package Taosecurity.Starfield.BuildKit --version 0.5.0`

## 0.4.2 (2026-11-20)

### What's new

`Localized.For` is now public, so a builder can give its own records the same kind of text that `Messages` and `GameplayOptions` write: all nine languages on a localized plugin and plain text on any other, or the builder's own `text:` function when it passes one.

Plugins built with 0.4.1 are unchanged when they're built again with 0.4.2.

### Example

This is the output of the README's `Localized.For` lines, run once on a plain plugin and once on a localized one, with each name's language count:

    > dotnet run
    localized=False: 1 Shiny, 1 LASER
    localized=True: 9 Shiny, 1 LASER

### Changes

- `Localized.For(mod, s, text:)` is public, and its `text:` argument is optional. It takes any `IStarfieldModGetter`, so a builder that holds a plugin through that interface can call it too. A test covers plain text on a normal plugin, nine languages on a localized one, and a builder's own function on both.
- The optional gap check (`mutagen-gap-check.py`, which `PluginWriter` runs when it can find it) now finds a master whatever its capitalization. The exact spelling is still tried first in every folder, so an exact match in the Data folder wins over a differently spelled copy in the masters folder. It has its own tests.
- String tables inside a BA2 archive were listed under "Not yet" in 0.4.1. A new test shows they were never a problem: Mutagen matches the archive's name and the table names inside it without regard to capitalization, so a localized plugin spelled differently on disk keeps its text from its archive. The test passes on Windows and on Linux. The proof for any capitalization comes from the Linux run, because Windows finds files whatever their capitalization anyway.

### Not yet

- On Linux outside Proton, Mutagen 0.54.4 can't read string tables from BA2 archives unless the `LocalAppData` environment variable is set, because it looks for the game's Plugins.txt there to order the archives. This affects any localized plugin whose text is in an archive, Starfield.esm included. Setting `LocalAppData` to any folder works around it.

### Download

`dotnet add package Taosecurity.Starfield.BuildKit --version 0.4.2`

## 0.4.1 (2026-11-20)

### What's new

This is a fix for Linux and Proton. `GameData` now finds a plugin whatever its capitalization, so a builder works on a case-sensitive file system even when a load order or a master list spells a plugin differently from the file (for example `Starfield.esm` against `starfield.ESM`).

Windows behaves exactly as before, and plugins built with 0.4.0 are unchanged.

### Example

This is a light plugin built with 0.4.1 against the Steam copy of Starfield.esm (the builder is in the README's Example):

    > dotnet run
    ID lock: 4 records, 4 new, 0 moved back to their locked IDs (ids.lock.json)
      BuildKit: Bethesda fields filled: 0 message back-out index, 0 form list add-to-list, 0 model light layer, 1 keyword, 0 story-manager quest node
      BuildKit: form version 582 set on 1 own records left at Mutagen's 576
      BuildKit: header interior cell count (INCC) set to 0 (was unset)
      BuildKit: own records written in Bethesda's FE FormID form (light plugin)
      gap check: skipped (mutagen-gap-check.py not found; set BUILDKIT_TOOLS or WriteOptions.GapCheckScript)

### Fixes

- `GameData.Locate` (and so `Open` and `TryOpen`) finds a plugin in any spelling. The exact spelling is still tried first, in every folder, so an exact match in the Data folder wins over a differently spelled copy in an extra folder.
- Each folder is listed once, sorted, and listed again when its modification time changes. A plugin written a moment ago is still found, and a long-unchanged Data folder isn't listed again for every missing optional plugin.
- A plugin found under another spelling keeps the requested name as its ModKey, so master lists in built plugins don't depend on how the disk spells the file.
- Its loose string tables (in a `Strings` folder of any spelling) are copied under the requested name into a temporary folder, deleted on `Dispose`, so a localized plugin keeps its text. The same happens for an exactly spelled plugin whose `Strings` folder is spelled differently.
- A new read-only `GameData.Warnings` list collects warnings: when the file used differs only in case from other files in its folder and none is spelled exactly as requested, the first in sorted order is used and a warning is added there (and written once to standard error).
- A damaged plugin that makes `TryOpen` throw gives the same error on a second try, not "is its own master".

### Not yet

- The optional gap check (`mutagen-gap-check.py`, run by `PluginWriter`) still looks masters up by their exact names.
- String tables inside a BA2 archive of a differently spelled plugin are looked for under the requested name.

### Download

`dotnet add package Taosecurity.Starfield.BuildKit --version 0.4.1`

## 0.4.0 (2026-11-18)

This version is built on Mutagen.Bethesda.Starfield 0.54.4.

- New `Messages.Hud` and `Messages.Box` make Message records for on-screen text that also shows on Xbox and
  PlayStation, where the Debug script does nothing. The field shapes and their order copy vanilla HUD and box messages.
  Tests cover the fields, the buttons, a fixed FormID, localized text and the subrecord order (also with an owner
  quest, in the order vanilla messages such as MissionBoardAcceptMessage use). Calling them adds only the new Message records.
- On a localized plugin, `GameplayOptions` and `Messages` now put their text in all nine languages
  (`Localized.Text`) when the builder passes no `text:` function. Before, `GameplayOptions` wrote English only in that
  case, so the other languages had missing strings. Builders that pass `text:`, and plugins that aren't localized,
  are unchanged by this.
- The debug option made by `GameplayOptions.Debug` is now named "Show Debug Messages (PC only)", and its description
  ends "PC only: Xbox and PlayStation can't show debug lines." The Debug script does nothing on consoles. So every plugin
  that has this option differs from a 0.3.1 build in that option's name and description (and, for a localized plugin
  built without `text:`, in the languages above); nothing else changes.

## 0.3.1 (2026-11-16)

This release has security fixes. Plugins built with 0.3.0 are unchanged.

- `GameData` refuses master names that aren't plain plugin file names. `HeaderMasters` (and so `Open`/`TryOpen`)
  throws `InvalidDataException` when a plugin lists a master such as `\\server\share\x.esm`, `..\x.esm` or
  `C:\x.esm`; `Locate` throws `ArgumentException` for such a name. Before this, opening a crafted plugin could make
  Windows contact another computer and send the user's login hash. The new helpers are `GameData.IsPlainPluginName` and
  `GameData.Printable`.
- The gap check's script search stops below the drive root, so a `C:\tools` or `D:\tools` folder another account
  created is never run. Python is started by its full path from PATH (`PluginWriter.FindPython`), and the script and
  Python paths are printed before it runs.

## 0.3.0 (2026-11-16)

This version is built on Mutagen.Bethesda.Starfield 0.54.4. It was tested in game with a light plugin built on it (FE FormIDs and the
Bethesda fields below).

- AGENTS.md gives step-by-step instructions for AI assistants, and it is also in the package. The README has a "What BuildKit prints" section.

- A new write option, `BethesdaFormIds`, is on by default: a light or medium plugin's own new records, and references to
  them, are stored in the form Bethesda's own Starfield plugins use (`FE...` for light, `FD...` for medium), instead
  of Mutagen's full-master form (`01...`). xEdit 4.1.5r no longer reports HITME warnings on them. Full plugins are
  unchanged. This option on its own changes only those FormID bytes. Plugins written with this form read back
  correctly in plain Mutagen 0.54.4, which accepts both forms.
- A plugin rebuilt on 0.3.0 with the default options also differs from a 0.2.0 build in the fields the options below
  fill, the form version, NPC compression, INCC and group-header stamps. Each option prints a `BuildKit:` line when it
  changes something.
- `BethesdaFormIds.Enable` / `Disable` / `IsEnabled` are there for builders that write without `PluginWriter.Write`.
- Four more write options, all on by default, make the output match Bethesda's own plugins more closely:
  `InteriorCellCount` (header INCC), `FillBethesdaFields` (fields Bethesda always writes, filled on own records
  only: MESG BNAM, FLST ANAM, model FLLD on ALCH/ACTI, KYWD CNAM/TNAM/FNAM, SMQN XNAM), `CkFormVersion` (582 for own
  records left at Mutagen's 576) and `CompressNpcs`. The helpers are public in `BethesdaFields`.
- A new write option, `ZeroGroupStamps`, is on by default: group headers are written with zero version-control words, as
  in Bethesda's own plugins, instead of the stamps a Creation Kit save leaves (which Mutagen keeps when a builder
  copies from such a plugin). Only group headers change; records are untouched.
- Tests now run one at a time (Mutagen keeps the list this option uses in one shared set).

## 0.2.0 (2026-11-09)

This version is built on Mutagen.Bethesda.Starfield 0.54.4.

- `Quests.New` writes a quest with NEXT and ANAM, as the Creation Kit writes every quest.
- `DialogTopics.FixSubtypeNames` fills a blank SNAM on dialogue topics from their Subtype. `PluginWriter.Write`
  runs it by default (new write option `FixDialogSubtypes`); topics that already have an SNAM are left alone.

## 0.1.0 (2026-11-09)

This is the first public release. It is built on Mutagen.Bethesda.Starfield 0.54.4.

- `GameData` opens masters with their light, medium or full flags, master-of-master first. It searches extra folders
  before the Data folder, and it has EditorID lookups (later master wins), a link cache and `OpenPlugin` to read back a written plugin.
- `Plugin.New` makes a plugin with the Creation Kit header (form version 582) and the FormID range for each plugin style.
- `PluginWriter.Write` fills EFIF/MUID on effects, writes ONAM like the Creation Kit and masters in load order,
  writes on one thread for the same bytes on every run, and runs an optional gap check.
- The new write options are `Masters` (explicit master list), `NextFormId` (keep the header's Next Object ID) and
  `GapCheckScript`. The gap check is now skipped, with one line, when its script isn't found.
- The package also has `Effects`, `IdLock`, `GameplayOptions` (with the "Show Debug Messages" option) and `Localized`.
- The tests run on plugins made by the test suite itself, and they use no game files.
