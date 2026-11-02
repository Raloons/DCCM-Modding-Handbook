# Terminology

Words the code uses that the game never says out loud. Read this before installing anything: every tutorial and example after it assumes you know these names. Each entry gives the plain meaning first, then the code detail you will actually see.

## Who is who

**Hero - the player.** In code, the player character is called the Hero (`Hero`, `hero`, `self.hero`, `manager.hero`). If a tutorial says "the hero's inventory" it means your inventory. Related things you will see: `hero.life` (current health), `hero.destroyed` (gone - never touch objects after this is true), `hero.dir` (facing: left or right), and `hero._level` (the level object the hero currently belongs to, or nothing when between levels).

**Mob - an enemy.** Enemies are `Mob`s. Code constantly separates the two: `if (target is Hero) return;` means "ignore friendly hits," and `if (target is not Mob victim) return;` means "I only care about real enemies from here on." `mobUid` is just a label for which enemy is which (see Identity below).

**InventItem and item - anything carryable.** Weapons, skills, and pickups in inventories are items (`InventItem`, `item`). The data describing one lives in `item._itemData`, and its text ID (`item._itemData?.id`) is how code recognizes it - by comparing that ID string to the one the mod expects.

**Weapon and skills - the gear.** `Weapon` is the base concept; specific stocks have specific names like `BackStabber` (a fast melee the Waveblade example builds on) or `SonicBolt` (a projectile). Active skills are managed by `HeroActiveSkillsManager`: `onActiveSkill` is the moment a skill is triggered, `useSkillItem` is the moment its effect runs. `HeroActiveSkill` is one equipped skill with its own cooldown state.

## Where things live

**Level and scroller - the world on screen.** The current place is a `Level` object. Visuals are added to its `scroller` (the layer that pans as you move) at a depth like `DP_ROOM_MAIN_FX` (an effects layer). Effects that must stay where they happened are parented to the scroller; effects that must follow the hero are parented to the hero's sprite instead.

**fx - throwaway visual effects.** `level.fx` offers one-shot effects like `elecNova` (a flash pop). These create no game objects and deal no damage - pure presentation, and the safest kind of visual to add.

**Inventory - what the hero carries.** `hero.inventory` holds items, with `nbActives` active slots you read via `getActiveOn(slot)`. A live skill entry (`getActiveSkill`) is separate from the item: the item is the thing, the entry is its running state.

## How position works

**Cells and pixels.** The world is a grid of cells, each 24 pixels wide. Positions are stored as a cell plus a remainder: `cx`/`cy` is the cell, `xr`/`yr` is the fraction inside it, so pixel position is `(cx + xr) * 24.0`. Heights like `hei` are already in pixels. Whenever you see `* 24.0` in example code, it is converting grid talk into pixel talk.

**Y grows downward.** Screen coordinates put zero at the top, so a *rise* means y gets *smaller*. A bounce check reads "old y minus new y is big enough" - backwards from school math, and the single most common source of inverted-geometry bugs. If your effect appears mirrored vertically, check this first.

**Facing is a direction number.** `dir` is negative for left, positive for right. Angles follow it: 0 radians flies right, pi radians flies left.

## How data works

**CDB - the game's database.** The CastleDB (`_Data_`, `cdb`) holds every data table: `item` for items, `affix` for affixes, and more. Code reads rows by ID (`item.byId.get(...)`) and iterates whole tables (`index.all`). Rows belong to `group`s, difficulties reference complete parent sets (`tier1` and friends), and legendary affixes hang off `legendAffixes`. Mods add rows; they never rewrite stock rows.

**Rows must be exact.** IDs are matched character for character (`BTD_ReserveBattery` is not `BTD_Reservebattery`), a row that should occur once must occur exactly once, and references must resolve to things that exist. The registry files in the examples enforce these rules with checks that throw rather than letting corrupt data reach gameplay.

## How code talks to the game

**dc - the proxy layer.** Your C# does not touch the game directly. Generated proxy classes under names like `dc.*` stand in for game classes (`dc.pr.Level`, `dc.h2d.Graphics`, `dc.tool.weap.PureNail`). Names and shapes can differ slightly between proxy variants, which is why example code prefers name lookups over fixed indices wherever possible.

**dynamic - flexible access.** `dynamic` lets code reach game fields by name without compile-time types. Powerful and fragile in equal measure: a wrong name fails at runtime, not at build time, so dynamic reads are always wrapped in guards and try/catch.

**AsHaxeString - the translator.** Text must be converted before crossing into the game: `"text".AsHaxeString()`. Raw .NET strings handed directly to game calls are a classic crash.

**Ref.From - proven value passing.** Numbers crossing into native calls go through the designed wrapper (`Ref<double>.From(ref v)`). Hand-filling struct fields instead is unproven - use the wrapper the codebase already proved.

**orig - the original function.** Every hook receives the original as a delegate (usually named `orig`) and calls it first (or deliberately skips it, with a comment saying why - see the Reserve Battery effect). `CreateHook(...)` attaches, `.Enable()` switches on; without enable, nothing flows.

## Time and identity

**Ticks, not seconds.** Game logic runs on a shared tick clock (`MutationGameClock.Ticks`). Durations are tick counts (21 ticks is about 0.35 seconds). Never use wall-clock time for gameplay rules: pausing the game pauses ticks, which is exactly the behavior you want.

**Identity: uid and pointers.** Every game object carries `__uid`, a numeric identity, and `HashlinkPointer`, its native handle. Code keys per-victim and per-event state by these (often as tuples like hero-plus-enemy), because names and globals cannot distinguish two identical enemies. Null or negative means "no valid object" - check before use.

**Namespaced IDs.** Custom content uses prefixed IDs (`TUT_` in tutorials, `BTD_` in the research mods) so nothing collides with stock or other mods. Tombstone retired IDs in a comment instead of reusing them.

## Tags, affects, and jumps

**Numeric tags.** Attacks carry numeric tag flags checked with `hasTag(n)`. Their meanings are verified per project, never guessed - the examples only test tags the research proved. If you see a tag number, treat it as "proven elsewhere in this codebase," not general knowledge.

**Affect IDs.** Status effects are numbers too: 88 is burn, 89 is bleed, 90 is poison, 94 is oil (a non-damaging primer, deliberately excluded from spreading). Native merge rules mean at most one entry per type exists, so re-applying refreshes rather than stacks - the design the Contagion example is built around.

**Jump counts are uses, not remaining.** The native AirJump counter tracks *uses*, and capacity varies (1 base, more with amulets). Code reasons in "spent uses" and restores through the existing setter with read-back verification - never by inventing its own counter.

## Reading the example code

The Examples pages print real research code, which carries the project's own shorthand:

- **`f@NNNN` annotations** (like `f@35686`) are the author's decompiler function IDs - breadcrumbs to their own analysis notes, not game API. You can ignore them; they explain where a claim was verified.
- **Milestone tags** (like `M80.21`, `M65.2`) mark which project milestone proved or changed something. They are history, not instructions.
- **Policy vs behavior split.** Tuning numbers live in small `*Policy` files with no game calls (testable alone); behavior lives in `*Rework`/`*Runtime` files that read the policy. When an example spans files, that split is the map.
- **VerboseLimit budgets.** Logging counters stop after a few dozen lines so live sessions stay readable. Unlimited logging is a bug, not a feature.

## Further reading

- [Installation](01-installation.md) - the project-level cast of characters: game, framework, launcher, folders, saves.
- [Your First Mod](03-first-mod.md) - these terms in action, one line at a time.
- [Examples](../examples/melee-weapon.md) - real code to practice reading with this vocabulary.
