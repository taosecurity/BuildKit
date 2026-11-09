# Changelog

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
