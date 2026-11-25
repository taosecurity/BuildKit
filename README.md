# BuildKit

BuildKit is a small C# library for programs that build Starfield plugins (`.esm` files) with
[Mutagen](https://github.com/Mutagen-Modding/Mutagen). It does the setup work such a builder otherwise copies from
project to project, and it writes your plugin the way Bethesda's Creation Kit does, so you can spend your time on your
mod's own records.

Get it from [NuGet](https://www.nuget.org/packages/Taosecurity.Starfield.BuildKit). It targets
**Mutagen.Bethesda.Starfield 0.54.4** exactly; each BuildKit release says which Mutagen version it was tested with,
and a new Mutagen release gets a new BuildKit release.

## First command

In a .NET 9 (or later) console project:

```
dotnet add package Taosecurity.Starfield.BuildKit
```

For AI assistants: step-by-step instructions are in [AGENTS.md](AGENTS.md).

## Example

This builder opens the game's main master, makes a light plugin with a keyword, an on-screen message and a "Show
Debug Messages (PC only)" option, keeps its FormIDs fixed, and writes it:

```csharp
using BuildKit;
using Mutagen.Bethesda;            // AddNew and other group helpers
using Mutagen.Bethesda.Starfield;

var dataDir = @"C:\Program Files (x86)\Steam\steamapps\common\Starfield\Data";
using var game = new GameData(dataDir);
game.Open("Starfield.esm");                      // plus any other master, e.g. game.Open("SFBGS003.esm")

var mod = Plugin.New("MyMod.esm", PluginStyle.Light, "Your Name", "My Mod 0.1.0");
var kw = mod.Keywords.AddNew("MM_Keyword");      // ... your records ...
var hello = Messages.Hud(mod, "MM_Msg_Hello", "My Mod is running", 3);

var dbg = GameplayOptions.Debug(mod, "MM_Option_Debug", "My Mod's log lines on screen");
GameplayOptions.Group(mod, "MM_Options", "My Mod", [dbg]);

Console.WriteLine(IdLock.Apply(mod, "ids.lock.json"));   // commit ids.lock.json with your builder
PluginWriter.Write(mod, "out", game);                    // writes out\MyMod.esm
```

`dotnet run` printed this with BuildKit 0.5.0 and sf-mod-check 0.15.0 installed as a .NET global tool (so it is on the PATH), and wrote an 883-byte `out\MyMod.esm`:

```
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
```

Each `BuildKit:` line is one thing it changed to match Bethesda's own plugins. The first line is the ID lock's
summary, which your builder prints. The `gap check:` lines are the output of `sf-mod-check gap` (see "The gap
check" below). Without sf-mod-check, the build still succeeds and prints one line instead:
`gap check: NOT RUN (sf-mod-check was not found). Install it ...`. Use `game.Get<IKeywordGetter>("SomeEditorID")` to look
up a record in the open masters by its EditorID.

## What it does for you

- It opens masters with the right light, medium or full flags, so FormIDs resolve correctly.
- It writes the plugin the way the Creation Kit does: the ONAM list, effect data Mutagen leaves out (EFIF and MUID),
  Bethesda-style FormIDs for light and medium plugins, and the header and record fields Bethesda always fills.
- It keeps your records' FormIDs the same from build to build, so saves and patches keep working.
- It adds a "Show Debug Messages (PC only)" option to the game's Gameplay Options.
- It makes Message records for on-screen text that also works on Xbox and PlayStation.
- It writes the same bytes for the same inputs, so you can prove a change to your builder changed nothing.

## Words used here

- **Plugin:** a mod's data file (`.esm`, `.esp` or `.esl`). It adds or changes records in the game.
- **Master:** a plugin that another plugin needs loaded before it. Starfield.esm is the master of nearly every mod.
  Every plugin BuildKit makes is itself flagged as a master.
- **Light plugin (light master):** a plugin flagged to use a small slot, so it doesn't count toward the limit of
  about 250 full plugins. A light master is a light plugin that is also flagged as a master. Medium plugins are a
  newer Starfield size between light and full.
- **Record:** one thing in a plugin, such as a weapon, a quest, a location or a setting.
- **FormID:** a record's number, such as `0002B560`. The first two digits depend on the load order.
- **EditorID:** a record's readable name, such as `Ammo_556`, used by the Creation Kit and xEdit.
- **Override:** a later plugin's copy of a record from an earlier one, which replaces it in the game.
- **ONAM:** a list in the plugin's header of the master records it overrides inside cells. Without it, the game
  ignores the plugin's changes to temporary references in its masters' cells.
- **EFIF and MUID:** two small fields in every effect entry on a spell, enchantment or ingestible (food, drink or
  medicine). Mutagen leaves them out of new effects, and without them the effect never applies in game.
- **HITME:** xEdit's warning "Higher Index Than Master-list Entries", for a FormID whose first digits point past the
  plugin's master list.
- **INCC:** a header field that holds the number of interior cells in the plugin.
- **Creation Kit (CK):** Bethesda's official editor for making Starfield mods.
- **xEdit (SF1Edit):** a community tool for viewing and editing plugins record by record.
- **Mutagen:** a .NET library for reading and writing Bethesda plugins, which BuildKit builds on.
- **Papyrus:** Starfield's scripting language. Scripts are written as `.psc` source files and compiled into `.pex`
  files that the game runs.

## Install

```
dotnet add package Taosecurity.Starfield.BuildKit
```

BuildKit needs the .NET 9 SDK (or later) and brings Mutagen.Bethesda.Starfield 0.54.4 with it. Don't add another
Mutagen version to the same project.

## What's in it

| Type | What it does |
|---|---|
| `GameData` | Opens masters from the Data folder (and optional extra folders searched first). Each master's own masters are opened before it, with their light, medium or full flags, so FormIDs resolve correctly. Gives EditorID lookups (`Get<T>`, where a later master wins), a link cache, and `OpenPlugin` to read back a plugin you just wrote. |
| `Plugin` | `Plugin.New(fileName, style, author, description, localized)` makes a master plugin with the Creation Kit's header (Master plus Light or Medium flag, form version 582). `IdRange(style)` gives the FormIDs a style may use. |
| `PluginWriter` | `Write(mod, outDir, game, options)` fills effect data, writes ONAM like the Creation Kit, takes master flags from the open masters, lists masters in load order and writes on one thread, so the same build gives the same bytes. See the options below. |
| `Effects` | `New(baseEffect, magnitude, area, duration)` makes an effect entry. `Fix(list)` and `FixAll(mod)` fill a missing EFIF (0) and MUID (position + 1, never a duplicate). Without them, effects on spells, enchantments and ingestibles never apply in game. |
| `Quests` | `New(mod, editorId)` makes a quest with empty (not missing) condition and alias lists, so it's written with NEXT and ANAM like a Creation Kit quest. A quest made with plain `AddNew` has neither. `Basic`, `Stage`, `Objective`, the alias helpers, `Scene` and `Fragments` build a working quest in the Creation Kit's layout (see "Quests" below). |
| `DialogTopics` | `FixSubtypeNames(mod)` fills a blank SubtypeName (SNAM) on every dialogue topic with the code that matches its Subtype, as vanilla topics have. `PluginWriter.Write` calls it for you. |
| `IdLock` | `Apply(mod, lockPath)` keeps every own record's FormID the same from build to build, using a JSON file of EditorID to FormID. New records get the next free ID. The build stops if a locked record disappears, if two records share an EditorID, or if a record has no EditorID. `update: false` only checks. |
| `GameplayOptions` | `Debug(...)` makes a "Show Debug Messages (PC only)" option, off by default, for Settings > Gameplay Options. `Bool(...)` makes other on/off options (both carry the two value rows the Creation Kit writes for an on/off option), and `Group(...)` makes the mod's own options group. Create the options first and the group last. |
| `Localized` | `Text(s)` gives the same text in all nine languages the game ships, for localized plugins. `For(mod, s, text:)` picks the right kind of text for your own records: your `text:` function if you pass one, otherwise all nine languages on a localized plugin and plain text on any other. `GameplayOptions` and `Messages` use `For`, so pass your own `text:` function to change what they write. |
| `Messages` | `Hud(mod, editorId, text, seconds)` makes a top-left HUD line, and `Box(mod, editorId, text, title, buttons, ownerQuest)` makes a message box. Both copy the shape of vanilla messages. Show them in Papyrus with `Message.Show()`, which works on Xbox and PlayStation. `Debug.Notification` and `Debug.MessageBox` do nothing on consoles (see below). |

### On-screen text that works on Xbox and PlayStation

Bethesda's `Debug.psc` marks the whole Debug script `DebugOnly`, so `Debug.Notification`, `Debug.MessageBox` and
`Debug.Trace` do nothing in the console builds of the game. Text the player must see belongs in a Message record:

```csharp
var sent = Messages.Hud(mod, "MM_Msg_Sent", "%.0f items sent to your ship", 3);
var stop = Messages.Box(mod, "MM_Msg_Stop", "The hunt is stopped.");          // no buttons: the game adds OK
var menu = Messages.Box(mod, "MM_Msg_Menu", "Where to?", "MY MOD", ["Home", "Cancel"], quest);
```

```papyrus
Message Property MM_Msg_Sent Auto Const Mandatory

Function Say(Message akMsg, float afArg1 = 0.0, float afArg2 = 0.0)
  akMsg.Show(afArg1, afArg2)                           ; shows on PC, Xbox and PlayStation
  Debug.Trace("[MM] " + akMsg + " " + afArg1)          ; the PC Papyrus log only
EndFunction

Function SendItems(int sentCount)
  ; ... send the items ...
  Say(MM_Msg_Sent, sentCount)
EndFunction
```

A Message fills in numbers (`%.0f`, up to nine), never text, so a message whose wording changes needs one Message
per case. A `%` in the text starts a number slot, so don't put a literal percent sign in it (write "percent").
`Box` with buttons returns the index of the button picked from `Show()`. Keep `Debug.Trace` and the
"Show Debug Messages (PC only)" switch for troubleshooting on PC.

On a localized plugin, `Messages` and `GameplayOptions` put their text in all nine languages unless you pass your
own `text:` function. They decide this when each record is made, so make the plugin with
`Plugin.New(..., localized: true)` rather than turning localization on later.

Your own records can follow the same rule with `Localized.For`:

```csharp
var kw = mod.Keywords.AddNew("MM_Keyword");
kw.Name = Localized.For(mod, "Shiny");                                 // nine languages if localized, else plain text
var weapon = mod.Weapons.AddNew("MM_Weapon");
weapon.Name = Localized.For(mod, "Laser", text: s => s.ToUpperInvariant());   // your own function wins
```

### Quests

The `Quests` helpers (since 0.6.0) make a quest with stages, objectives, aliases and a simple two-actor scene,
with the field order and fixed values the Creation Kit writes on vanilla quests. This made-up errand starts with the
game, shows an objective that points at a note created inside a box, plays three lines between a placed actor and
the nearest actor with a keyword, and completes when stage 20 is set:

```csharp
var q   = Quests.Basic(mod, "MM_Errand", "A Small Errand", priority: 60, startGameEnabled: true);
var s0  = Quests.Stage(mod, q, 0, startUp: true, note: "editor-only note");
var s10 = Quests.Stage(mod, q, 10, log: "I was asked to find a note.");
var s20 = Quests.Stage(mod, q, 20, log: "I found the note.", end: StageEnd.CompleteQuest);

var box     = Quests.ForcedAlias(q, "Box", boxRef.ToLink());                     // a placed reference
var giver   = Quests.ForcedAlias(q, "Giver", giverRef.ToLink());                 // a placed actor
var speaker = Quests.FindAlias(q, "Speaker", speakerKeyword.ToLink(), actorsOnly: true, closest: true);
var note    = Quests.CreatedAlias(q, "Note", noteItem.ToLink<IStarfieldMajorRecordGetter>(), box, inside: true);
var place   = Quests.LocationAlias(q, "Place", location.ToLink(), optional: true);

var find = Quests.Objective(mod, q, 10, "Find the note", [note]);               // the marker points at the note

Quests.Scene(mod, q, "MM_Errand_Talk", giver, speaker,
    [new SceneLine(0, "Have you seen a note?"), new SceneLine(1, "Look in the box."), new SceneLine(0, "Thanks.")],
    endStage: 10);                                                              // the scene sets stage 10 when it ends

var f = Quests.Fragments(q)
    .SetStage(s0, s10).ShowObjective(s10, find)
    .CompleteObjective(s20, find).CompleteQuest(s20);
f.Apply();                                                                      // the quest's script fragment entries
var psc = Path.Combine(@"Scripts\Source", f.SourcePath);
Directory.CreateDirectory(Path.GetDirectoryName(psc)!);
File.WriteAllText(psc, f.Source());   // Scripts\Source\Fragments\Quests\QF_MM_Errand_<8 hex digits>.psc
```

- **Stages** keep one log entry each, as the Creation Kit writes them. `log` is the quest log text the player sees,
  `note` is an editor-only note, `startUp` runs the stage when the quest starts, and `end` completes or fails the
  quest when the stage is set.
- **Stage links are Papyrus.** Showing an objective, completing it, setting the next stage and completing the quest
  are script lines in the quest's fragment script, as in the Creation Kit. `Fragments` collects them (any other line
  with `Line(stage, "...")`), `Apply()` writes the fragment entries into the quest, and `Source()` gives the script.
  Compile it with your own scripts (`Starfield_Papyrus_Flags.flg`, the game's sources as imports) and ship the
  `.pex` in `Scripts\Fragments\Quests\` (loose or in your archive). Call `Apply()` after the last stage is added and before `IdLock.Apply`
  (IdLock replaces the quest with a copy, so a later `Apply()` would go to the old one). The script name holds the
  quest's FormID as it is before `IdLock` runs, so if a build shifts that ID, delete the old `QF_` files.
- **Aliases** get the Creation Kit's numbering (one more than the highest alias ID). Pass records as
  `record.ToLink()`. `optional: true` lets the quest start when the alias can't be filled.
- **Scenes** give each line its own phase, dialogue action and scene topic with one response. Actor 0 is the first
  alias and 1 the second; both must be filled with actors (a container or other object can't speak). The topics and responses are named `<scene>_Line01`, `<scene>_Line01_Info` and so on,
  so `IdLock` keeps their FormIDs too. BuildKit writes no voice files or lip files, and the scene uses only
  dialogue actions.
- Each helper throws on a mistake it can see, with a message that names the fix: a repeated stage, objective,
  alias or scene name, an alias from another quest, a scene end stage or a fragment stage the quest lacks, or an
  EditorID that can't name a Papyrus script.

Not yet tested in game: a quest built with these helpers has passed `sf-mod-check gap` and `check`, and its
records were compared by bytes with vanilla quests, but none has been played yet.

### Write options

| Option | Default | Meaning |
|---|---|---|
| `OverriddenForms` | `Iterate` | Fill the header's ONAM list like the Creation Kit. Mutagen 0.54.4's own default writes none, and the game then ignores a plugin's edits of temporary references in its masters' cells. |
| `Deterministic` | `true` | Write on one thread. With parallel writing, Mutagen 0.54.4 can number a localized plugin's strings differently on every run. |
| `FixEffects` | `true` | Run `Effects.FixAll` before writing. |
| `FixDialogSubtypes` | `true` | Run `DialogTopics.FixSubtypeNames` before writing. Topics that already have an SNAM are left alone. |
| `BethesdaFormIds` | `true` | Store a light or medium plugin's own new records in Bethesda's `FE` / `FD` form (see below). |
| `InteriorCellCount` | `true` | Set the header's INCC to the number of interior cells in the plugin, as every Bethesda plugin has it (xEdit marks it required). |
| `FillBethesdaFields` | `true` | Fill empty fields that Bethesda always writes, on the plugin's own records only (see below). |
| `CkFormVersion` | `true` | Own records still at Mutagen 0.54.4's default form version (576) get the Creation Kit's 582. |
| `CompressNpcs` | `true` | Own NPC records are written compressed, as all of Bethesda's are. |
| `ZeroGroupStamps` | `true` | `BethesdaFields.ZeroGroupStamps`: the two version-control words in every group header are written as zero, as in all of Bethesda's own plugins. A Creation Kit save stamps them, and Mutagen keeps those stamps when a builder copies records or groups from such a plugin. The game doesn't use them. |
| `MasterOrder` | open order | The order masters are listed in the header. |
| `Masters` | none | An explicit master list, written exactly as given, even masters no record points into (for patches that list a master only to load after it). |
| `MastersListContent` | Mutagen's | Lower-level form of `Masters`: `NoCheck` writes the header's `MasterReferences` as you filled them. |
| `NextFormId` | Mutagen's | `NoCheck` keeps the Next Object ID you set in the header instead of recomputing it. |
| `GapCheck` | `true` | After writing, run `sf-mod-check gap` on the plugin if sf-mod-check is found (see below). |
| `GapCheckExe` | none | Path to `sf-mod-check.exe` (or `sf-mod-check` on Linux). |
| `GapCheckMastersDir` | none | Extra folder the gap check searches for masters. |
| `FailOnGapErrors` | `true` | Throw when the gap check exits nonzero (it found errors, or it could not check the plugin). |

**`BethesdaFormIds` in detail.** Bethesda's own light plugins store their new records as `FE` plus the plugin's place
among light masters (`FE000800` with no light masters), and medium plugins as `FD` plus their place among medium
masters. Mutagen 0.54.4 does this only for Bethesda's plugins and gives every other one the full-master form
(`01000800`), which xEdit's Check for Errors reports as a HITME.

The game loads both forms, and in-game FormIDs don't change, so saves are unaffected. Full plugins are written the
same either way.

**`FillBethesdaFields` in detail.** `BethesdaFields.Fill` gives each empty field Bethesda's usual value: message
"index chosen on back out" -1, form list "add to list" null, model light layer 1 on Aid items and activators, keyword
color FF FF FF 00 / type 0 / FNAM zeros, and story-manager quest node "max concurrent quests" 0. Overrides, and
fields the builder set itself, are left alone.

### The gap check

The gap check compares a written plugin with its masters. It reports data Mutagen 0.54.4 is known to lose on write,
plus missing ONAM entries. BuildKit doesn't contain it: it runs `sf-mod-check gap`, the gap command of the separate
tool [sf-mod-check](https://github.com/taosecurity/sf-mod-check) (version 0.15.0 or later).

BuildKit looks for sf-mod-check in this order:

- `WriteOptions.GapCheckExe`;
- the path in the `BUILDKIT_SFMODCHECK` environment variable;
- the first `sf-mod-check` found on PATH (by its full path, so an `sf-mod-check.exe` in the current folder is never
  picked). On Windows only `sf-mod-check.exe` is found there.

A path named by the option or the variable must be a full path to an existing program, or the build throws.
`PluginWriter.GapCheck`, called on its own, uses the variable and PATH the same way. Install sf-mod-check with
`dotnet tool install -g sf-mod-check` (it is on nuget.org since 0.18.0), or get the stand-alone exe from its
[releases page](https://github.com/taosecurity/sf-mod-check/releases), unzip it and put its folder on PATH.

If it finds none, it prints one line on standard error naming the fix (`gap check: NOT RUN (sf-mod-check was not
found). Install it ...`) and the build still succeeds, so a missing check is never silent. When it runs, BuildKit
prints the exe it uses and relays the output, and any nonzero exit (errors found, or the plugin couldn't be checked)
throws unless `FailOnGapErrors` is off. The Data folder you gave `GameData` is passed as `--data`, and
`GapCheckMastersDir` as `--masters-dir`.

### Master names

A plugin lists its masters by file name. `GameData` refuses a plugin whose master list holds anything other than a
plain name such as `Starfield.esm` (a folder, a drive, a network path like `\\server\share\x.esm`, or `..`), with an
`InvalidDataException` naming the plugin. Without this check, opening a crafted mod could make Windows contact
another computer. `GameData.IsPlainPluginName` is public for builders that read master names themselves.

## More examples

The three builders below were each built with BuildKit 0.6.0 and Mutagen 0.54.4. They need the same `using` lines as the
example above plus `using Mutagen.Bethesda.Plugins;`, and each starts with
`var dataDir = @"C:\Program Files (x86)\Steam\steamapps\common\Starfield\Data";`. The names `AuthorsMod`, `AM_`, `AMA_`,
`MH_` and `MQ_` are made up.

### An add-on for another author's mod

An add-on is a plugin of your own that lists the other author's plugin as a master and points at its records. It
changes none of them, so it can't conflict with them. Put a copy of their plugin in a folder of your own (here
`C:\Mods\AuthorsMod`), tell `GameData` about that folder, open their plugin after `Starfield.esm`, and look their records up
by EditorID:

```csharp
using var game = new GameData(dataDir, extraDirs: [@"C:\Mods\AuthorsMod"]);   // folders searched before Data
game.Open("Starfield.esm");
game.Open("AuthorsMod.esm");                     // their plugin: read only, becomes a master

var mod = Plugin.New("AuthorsModAddon.esm", PluginStyle.Light, "Your Name", "Authors Mod Add-on 0.1.0");
var theirKeyword = game.Get<IKeywordGetter>("AM_WidgetKeyword");   // stops the build if it is missing
var theirWidget = game.Get<IMiscItemGetter>("AM_Widget");

// Your own records point at theirs; nothing of theirs is copied or changed.
var gadget = mod.MiscItems.AddNew("AMA_Gadget");
gadget.Name = "Gadget";
gadget.Keywords = new();
gadget.Keywords.Add(theirKeyword.ToLink());
var bundle = mod.FormLists.AddNew("AMA_Bundle");
bundle.Items.Add(theirWidget.ToLink<IStarfieldMajorRecordGetter>());
bundle.Items.Add(gadget.ToLink<IStarfieldMajorRecordGetter>());

Console.WriteLine(IdLock.Apply(mod, "ids.lock.json"));
PluginWriter.Write(mod, "out", game, new WriteOptions { GapCheckMastersDir = @"C:\Mods\AuthorsMod" });
```

- `extraDirs` folders are searched before the Data folder, so the build works without installing their mod.
  `GameData.Get` stops the build with a message such as `no record AM_Widget in ...` if their EditorID is missing, for example after they rename it.
- Their plugin becomes a master of yours only because your records point into it. Players need both mods loaded, and
  yours loaded after theirs.
- `IdLock` locks the FormIDs of your own records only. Commit `ids.lock.json`.
- `GapCheckMastersDir` tells `sf-mod-check gap` where to find their plugin, the same as `--masters-dir`.

### A magic effect and a spell

An effect on a spell is an `Effect` entry that points at a `MagicEffect` record. `Effects.New` makes the entry. Mutagen
leaves out two fields of every new entry (EFIF and MUID), and without them the effect never applies in the game.
`PluginWriter.Write` fills them while it writes when `FixEffects` is on, which is the default; the option is named
here so you can see where it lives:

```csharp
using var game = new GameData(dataDir);
game.Open("Starfield.esm");
var mod = Plugin.New("MyHeal.esm", PluginStyle.Light, "Your Name", "My Heal 0.1.0");
var health = game.Get<IActorValueInformationGetter>("Health");
var eitherHand = game.Get<IEquipTypeGetter>("EitherHand");

// The magic effect: restores Health, once cast on yourself.
var regen = mod.MagicEffects.AddNew("MH_RegenEffect");
regen.Name = "Slow Heal";
regen.Archetype = new MagicEffectArchetype { Type = MagicEffectArchetype.TypeEnum.ValueModifier };
regen.SecondActorValue.SetTo(health);            // the actor value a Value Modifier changes
regen.CastType = CastType.FireAndForget;
regen.TargetType = TargetType.Self;
regen.Flags = MagicEffect.Flag.NoArea;

// The spell that uses it: Effects.New(effect, magnitude, area, duration).
var spell = mod.Spells.AddNew("MH_SlowHealSpell");
spell.Name = "Slow Heal";
spell.Type = Spell.SpellType.Spell;
spell.CastType = CastType.FireAndForget;
spell.TargetType = TargetType.Self;
spell.EquipmentType.SetTo(eitherHand);
spell.Effects.Add(Effects.New(regen.ToLink<IMagicEffectGetter>(), magnitude: 5, area: 0, duration: 10));

// FixEffects (on by default) fills the missing EFIF and MUID in every effect entry while writing.
PluginWriter.Write(mod, "out", game, new WriteOptions { FixEffects = true });
```

For a Value Modifier with a duration, the magnitude is the amount per second. The actor value the effect changes is
`SecondActorValue`. The build prints `BuildKit: EFIF/MUID filled on 1 effect entries`. If you build effects outside
`PluginWriter.Write`, call `Effects.Fix(spell.Effects)` or `Effects.FixAll(mod)` yourself.

### A script on the player alias of a quest

A quest alias is a named slot that points at a record. A script on an alias is stored in the quest's own script data
(`QuestAdapter.Aliases`) as an entry that points at the quest and the alias ID. The player alias uses the `Player`
NPC record as its unique actor, as the game's own quests do. `Quests.New` makes the quest shell, and the alias and its
script use plain Mutagen objects:

```csharp
using var game = new GameData(dataDir);
game.Open("Starfield.esm");
var mod = Plugin.New("MyQuest.esm", PluginStyle.Light, "Your Name", "My Quest 0.1.0");
var playerNpc = game.Get<INpcGetter>("Player");

var quest = Quests.New(mod, "MQ_Quest");
quest.Name = "My Quest";
quest.Data = new QuestData { Flags = Quest.Flag.StartGameEnabled | Quest.Flag.StartsEnabled };

// The player alias, filled by the Player NPC, as the game's own quests do it.
// Quests.NextAliasId gives the next free alias ID (0 on a quest with no aliases yet).
var playerAliasId = Quests.NextAliasId(quest);
var playerAlias = new QuestReferenceAlias { ID = playerAliasId, Name = "Player", Flags = 0, ALFG = 0 };
playerAlias.UniqueActor.SetTo(playerNpc);
quest.Aliases!.Add(playerAlias);

// The script on the alias (Papyrus: Scriptname MQ:PlayerAliasScript extends ReferenceAlias).
var aliasScript = new ScriptEntry { Name = "MQ:PlayerAliasScript", Flags = ScriptEntry.Flag.Local };
// aliasScript.Properties.Add(new ScriptObjectProperty { Name = "SomeProperty", Object = someRecord.ToLink<IStarfieldMajorRecordGetter>() });

// An alias script is stored in the quest's script data: an entry that points at the quest and the alias ID.
// ??= keeps script data the quest already has, such as the fragments from Quests.Fragments(...).Apply().
var vmad = quest.VirtualMachineAdapter ??= new QuestAdapter();
vmad.Version = 6;
vmad.ObjectFormat = 2;
vmad.ExtraBindDataVersion = 3;
vmad.Aliases.Add(new QuestFragmentAlias
{
    Property = new ScriptObjectProperty { Name = "", Object = quest.ToLink<IStarfieldMajorRecordGetter>(), Alias = (short)playerAliasId },
    Version = 6,
    ObjectFormat = 2,
    Scripts = { aliasScript },
});

PluginWriter.Write(mod, "out", game);
```

The script itself is Papyrus, compiled separately: `Scriptname MQ:PlayerAliasScript extends ReferenceAlias`. Its
properties are listed on the `ScriptEntry`, with the same names as the `Property` lines in the `.psc`. The script on
the quest itself goes in `QuestAdapter.Scripts` the same way. The example works next to the `Quests` helpers above:
an alias made by `Quests.ForcedAlias` and the others keeps its own ID, and fragments written by `Apply()` stay.

## What BuildKit prints

BuildKit writes what it changed to standard output, one line per decision, each starting `  BuildKit:` (for example
EFIF/MUID filled, Bethesda fields filled, group headers zeroed) or `  gap check:`. The Example above shows a real run. A problem it can't fix is thrown as
an exception whose message names the file or records involved. To keep a log of a build, redirect your
builder's output: `dotnet run > build.log 2>&1`.

Write one plugin at a time: the `BethesdaFormIds` switch lives in a setting Mutagen shares across the process, so
don't call `PluginWriter.Write` from several threads at once.

## Same bytes every build

A builder on BuildKit gives byte-identical output for the same inputs: one-thread writing fixes string numbering, and
the ID lock fixes FormIDs. That makes it easy to prove a refactor changed nothing: build before and after, then compare
the files.

## Tests

```
dotnet test tests/BuildKit.Tests
```

The tests make every plugin they need in a temp folder. No game files are needed or included.

## Found a mistake?

Open an issue: https://github.com/taosecurity/BuildKit/issues

## Privacy

BuildKit collects nothing and makes no network calls. See [PRIVACY.md](PRIVACY.md).

## Credits

BuildKit is made by Richard Bejtlich. It is built on [Mutagen](https://github.com/Mutagen-Modding/Mutagen) by the
Mutagen team.

## License

GPL-3.0 (see [LICENSE](LICENSE)), the same license as Mutagen. BuildKit doesn't include or need any Bethesda files;
it reads the plugins in your own game install.
