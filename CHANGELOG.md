# Changelog

## 0.4.0 (2026-11-18)

Built on Mutagen.Bethesda.Starfield 0.54.4.

- New `Messages.Hud` and `Messages.Box` make Message records for on-screen text that also shows on Xbox and
  PlayStation, where the Debug script does nothing. The field shapes and their order copy vanilla HUD and box messages.
  Tests cover the fields, the buttons, a fixed FormID, localized text and the subrecord order (also with an owner
  quest, checked against Starfield.esm's 1,149 messages). Calling them adds only the new Message records.
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
