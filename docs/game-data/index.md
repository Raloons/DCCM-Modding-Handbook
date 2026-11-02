# Game Data and Resources (status: concept - generalized pattern, examples not live-verified)

How to package data and assets so your mod installs cleanly, coexists with other mods, and never endangers saves. All examples are independently written teaching material. Never publish game data files, built packages, or save bytes - document shapes and policies, not content.

## Concepts you need first

### Overlays add; replacements destroy

A data overlay combines your rows with stock rows at load. Two designs, very different risk:

- **Additive overlay:** your rows are appended; every stock row stays byte-identical in payload and order. Uninstalling restores vanilla exactly. This is the only design this handbook recommends.
- **Replacement overlay:** you ship a full copy of a table with your edits inside. One stale copy (after a game update, or conflicting with another mod's copy) silently reverts stock fixes or breaks other mods.

Verification for an additive overlay is mechanical: stock row count plus exactly your N rows, stock payloads and order unchanged, your rows validated against the schema (types, references, required fields).

### Groups, parents, and order are load-bearing

Catalogs are not flat lists. Rows belong to groups, difficulties reference complete parent sets, and load order can determine which row wins. When you add content:

- Preserve group boundaries and separators; do not renumber or relocate stock groups.
- If a parent set (for example difficulty tiers) must include your entry, append complete entries that mirror the stock structure - a half-added parent corrupts every difficulty that references it.
- References from your rows must resolve (icons, rooms, loot tables exist); references from stock rows must never be retargeted to your content.

### Packages carry only your own bytes

A resource package (`res.pak` style bundle) must contain only assets you created: your art, your audio, your data rows. Never include extracted game files, and never redistribute the game's packages. Build the package from source every time with a pinned tool; verify the built package round-trips (unpack, compare file list and hashes against sources).

### Saves remember your content

The moment a run references your custom ID (an item in inventory, a visited level, a granted entitlement), that save depends on your mod. Plan the full lifecycle before anyone plays:

- **Resume with mod present:** must work, including mid-level and mid-effect states.
- **Resume with mod removed:** must degrade gracefully (blocked with a clear message, never a corrupted slot).
- **Version upgrades:** old saves referencing old row shapes must still load or be cleanly quarantined.
- **Cloud sync:** know which files sync and which do not; never assume a sidecar or companion file follows the save to another machine.

## Tutorial: package and verify your first data overlay

This walkthrough uses a single custom item row (the `TUT_DemoBlade` from [Custom Equipment](../custom-equipment/index.md)) as the running example.

### Step 1 - Author the row from the schema, not from memory

Write the row by copying the *shape* of a stock row (field names and types from documentation), filling only your own values: namespaced ID, display name, stats, icon reference, price. Leave every field you do not understand at its stock default - an invented value in an unknown field is a time bomb.

### Step 2 - Validate offline before ever launching

Build a small validation script (any language) that checks, without the game running:

1. Every stock row from the baseline input is present, byte-identical, in identical order.
2. Exactly your N rows are added, each with a unique namespaced ID and valid references.
3. Parent sets that include your entry are complete (no half-added parents).
4. A negative suite: mutated inputs (duplicate IDs, bad references, wrong types) are all rejected.

Only a suite that rejects bad input earns trust when it accepts yours.

### Step 3 - Package with a pinned, repeatable build

Build the package from source with a recorded tool version, then verify the artifact: unpack it, diff the file list and hashes against your sources, and confirm no stock or third-party bytes are inside. Record the package hash alongside the source hash so any future rebuild is comparable.

### Step 4 - The five-load proof

In a disposable fixture with a copied test save:

1. Clean launch without the mod: record baseline counts.
2. Launch with the mod: stock counts plus exactly your addition; spawn the item; verify icon, name, and behavior.
3. Save, exit, reload: the item persists and works.
4. Remove the mod, reload: graceful degradation, no corruption.
5. Reinstall, reload the pre-removal backup: full function restored.

All five loads with hash-verified rollback between steps. That is the whole tutorial - everything else is commentary.

## Common failures

- Shipping a full-table replacement that goes stale on the next game update.
- Renumbering groups or reordering stock rows, breaking other mods' overlays.
- References to assets that are not in the package (works on your machine, broken everywhere else).
- Including extracted game files in the package.
- No removal test: the mod works until the player uninstalls it, then their save dies.
- Assuming companion/sidecar files sync with cloud saves.

## Safety and rollback

- Data mods are save mods: disposable copied saves, full backup with hash verification before every session.
- Keep the baseline input (stock counts, order, hashes) recorded so every overlay is diffable.
- Never hand-edit a save to "fix" a mod problem; fix the mod and re-prove the five loads.

## Further reading

- [Custom Equipment](../custom-equipment/index.md) - the items these rows describe.
- [Custom Levels and Biomes](../levels-biomes/index.md) - the rooms these rows describe.
- [Debugging & Testing](../debugging-testing/index.md) - fixtures, snapshots, and the five-load proof.
