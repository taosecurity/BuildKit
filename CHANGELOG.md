# Changelog

## 0.4.2 (2026-11-20)

### What's new

`Localized.For` is now public, so a builder can give its own records the same kind of text that `Messages` and `GameplayOptions` write: all nine languages on a localized plugin and plain text on any other, or the builder's own `text:` function when it passes one.

Plugins built with 0.4.1 are unchanged when they're built again with 0.4.2.

### Example

The README's `Localized.For` lines, run once on a plain plugin and once on a localized one, with each name's language count:

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

A light plugin built with 0.4.1 against the Steam copy of Starfield.esm (the builder is in the README's Example):

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
- New `GameData.Warnings` (read-only): when the file used differs only in case from other files in its folder and none is spelled exactly as requested, the first in sorted order is used and a warning is added there (and written once to standard error).
- A damaged plugin that makes `TryOpen` throw gives the same error on a second try, not "is its own master".

### Not yet

- The optional gap check (`mutagen-gap-check.py`, run by `PluginWriter`) still looks masters up by their exact names.
- String tables inside a BA2 archive of a differently spelled plugin are looked for under the requested name.

### Download

`dotnet add package Taosecurity.Starfield.BuildKit --version 0.4.1`

## 0.4.0 (2026-11-18)

Built on Mutagen.Bethesda.Starfield 0.54.4.

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

Security fixes. Plugins built with 0.3.0 are unchanged.

- `GameData` refuses master names that aren't plain plugin file names. `HeaderMasters` (and so `Open`/`TryOpen`)
  throws `InvalidDataException` when a plugin lists a master such as `\\server\share\x.esm`, `..\x.esm` or
  `C:\x.esm`; `Locate` throws `ArgumentException` for such a name. Before this, opening a crafted plugin could make
  Windows contact another computer and send the user's login hash. New `GameData.IsPlainPluginName` and
  `GameData.Printable`.
- The gap check's script search stops below the drive root, so a `C:\tools` or `D:\tools` folder another account
  created is never run. Python is started by its full path from PATH (`PluginWriter.FindPython`), and the script and
  Python paths are printed before it runs.

## 0.3.0 (2026-11-16)

Built on Mutagen.Bethesda.Starfield 0.54.4. Tested in game with a light plugin built on it (FE FormIDs and the
Bethesda fields below).

- AGENTS.md: step-by-step instructions for AI assistants, also in the package. README: "What BuildKit prints".

- New write option `BethesdaFormIds`, on by default: a light or medium plugin's own new records, and references to
  them, are stored in the form Bethesda's own Starfield plugins use (`FE...` for light, `FD...` for medium), instead
  of Mutagen's full-master form (`01...`). xEdit 4.1.5r no longer reports HITME warnings on them. Full plugins are
  unchanged. This option on its own changes only those FormID bytes. Plugins written with this form read back
  correctly in plain Mutagen 0.54.4, which accepts both forms.
- A plugin rebuilt on 0.3.0 with the default options also differs from a 0.2.0 build in the fields the options below
  fill, the form version, NPC compression, INCC and group-header stamps. Each option prints a `BuildKit:` line when it
  changes something.
- `BethesdaFormIds.Enable` / `Disable` / `IsEnabled` for builders that write without `PluginWriter.Write`.
- Four more write options, all on by default, so the output matches Bethesda's own plugins more closely:
  `InteriorCellCount` (header INCC), `FillBethesdaFields` (fields Bethesda always writes, filled on own records
  only: MESG BNAM, FLST ANAM, model FLLD on ALCH/ACTI, KYWD CNAM/TNAM/FNAM, SMQN XNAM), `CkFormVersion` (582 for own
  records left at Mutagen's 576) and `CompressNpcs`. The helpers are public in `BethesdaFields`.
- New write option `ZeroGroupStamps`, on by default: group headers are written with zero version-control words, as
  in Bethesda's own plugins, instead of the stamps a Creation Kit save leaves (which Mutagen keeps when a builder
  copies from such a plugin). Only group headers change; records are untouched.
- Tests now run one at a time (Mutagen keeps the list this option uses in one shared set).

## 0.2.0 (2026-11-09)

Built on Mutagen.Bethesda.Starfield 0.54.4.

- `Quests.New`: a quest written with NEXT and ANAM, as the Creation Kit writes every quest.
- `DialogTopics.FixSubtypeNames`: fills a blank SNAM on dialogue topics from their Subtype. `PluginWriter.Write`
  runs it by default (new write option `FixDialogSubtypes`); topics that already have an SNAM are left alone.

## 0.1.0 (2026-11-09)

First public release. Built on Mutagen.Bethesda.Starfield 0.54.4.

- `GameData`: opens masters with their light, medium or full flags, master-of-master first; extra folders searched
  before the Data folder; EditorID lookups (later master wins); link cache; `OpenPlugin` to read back a written plugin.
- `Plugin.New` with the Creation Kit header (form version 582) and the FormID range for each plugin style.
- `PluginWriter.Write`: EFIF/MUID filled on effects, ONAM written like the Creation Kit, masters in load order,
  one-thread writing for the same bytes on every run, optional gap check.
- New write options: `Masters` (explicit master list), `NextFormId` (keep the header's Next Object ID) and
  `GapCheckScript`. The gap check is now skipped, with one line, when its script isn't found.
- `Effects`, `IdLock`, `GameplayOptions` (with the "Show Debug Messages" option) and `Localized`.
- Tests on plugins made by the test suite itself; no game files.
