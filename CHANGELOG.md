# Changelog

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
