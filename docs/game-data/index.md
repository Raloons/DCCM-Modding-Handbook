# Game Data and Resources (status: concept - generalized pattern, examples not live-verified)

Package data and assets so your mod installs cleanly, coexists with other mods, and never endangers saves. This guide covers overlay design, reference integrity, repeatable packaging, an offline validation suite with negative cases, and the five-load proof with hash-verified rollback. Never publish game data files, built packages, or save bytes - document shapes and policies, not content.

**You will produce:** a validated additive overlay for one item row, a repeatably built package with recorded hashes, and a five-load proof record.
**You will verify:** stock rows byte-identical in payload and order, exactly your rows added, all references resolving, and graceful behavior on removal.

## Prerequisites

- [Custom Equipment](../custom-equipment/index.md) Parts 2-3 (project layout, row authoring).
- Python 3 (any recent version) for the offline validator, plus PowerShell for hashing.

## Part 1 - Concepts

### Overlays add; replacements destroy

A data overlay combines your rows with stock rows at load. Two designs:

- **Additive overlay (recommended):** your rows appended; every stock row byte-identical in payload and order. Removal restores vanilla exactly.
- **Replacement overlay (avoid):** a full copy of a table with your edits inside. Goes stale on game updates, conflicts with other mods' copies, silently reverts stock fixes.

Acceptance for an additive overlay is mechanical: baseline count plus exactly your N rows, stock payloads and order unchanged, your rows schema-valid. If any check needs human judgment, the overlay is not done.

### Groups, parents, and order are load-bearing

Catalogs are not flat lists. Rows belong to groups, parents (such as difficulty tiers) reference complete sets, and load order can decide which row wins. Rules:

- Preserve group boundaries and separators. Never renumber or relocate stock groups.
- When a parent set must include your entry, append complete entries mirroring stock structure. A half-added parent corrupts every difficulty referencing it.
- Your references must resolve (icons, rooms, loot pools exist). Stock references must never be retargeted to your content.
- Your IDs use a reserved prefix (`TUT_`) so validators can allowlist them mechanically.

### Packages carry only your own bytes

A resource package contains only what you created: your art, audio, and data rows. Never extracted game files, never other mods' files. Build from source every time with a recorded tool version, then verify: unpack the artifact, diff file list and hashes against sources, confirm zero foreign bytes. Record source hash next to artifact hash so any rebuild is comparable.

### Saves remember your content

Once a run references your ID (inventory item, visited level, granted entitlement), that save depends on your mod. Design the lifecycle before anyone plays:

- **Present:** resume works, including mid-level and mid-effect states.
- **Removed:** graceful degradation (clear blocked message, run intact, never corruption).
- **Upgraded:** old saves referencing old row shapes load or quarantine cleanly.
- **Synced:** know which files cloud-sync follows and which it ignores; never assume companion files travel with the save.

## Part 2 - Author the overlay source

Keep one authored source per row family (JSON, or whatever your toolchain consumes), plus a manifest listing every file the package build must include:

```
TutData/
  src/
    items.tut.json        <-- your rows only, never stock rows
    manifest.json         <-- file list + schema version + your prefix
  tools/
    validate.py           <-- Part 4
  out/
    tutdata.pkg           <-- built artifact (gitignored, never committed)
  verify/
    five-loads.md         <-- Part 5 record
```

Authoring rules per row:

1. Copy the *shape* of an analogous stock row from documentation (field names and types). Change only your values.
2. Identity first: `TUT_` prefix, unique across all your files, never reused after deletion (tombstone retired IDs in a comment so they are never reissued to something else).
3. References second: every ID you point at must exist in baseline or in your own source.
4. Unknown fields keep stock defaults. Invented values in unknown fields are time bombs.
5. One row per concern while learning. Multi-row edits multiply failure surfaces.

## Part 3 - Repeatable packaging

Build with one command, recorded in the manifest (tool name plus version). After every build, verify the artifact:

```powershell
# Illustrative verification (adapt to your packer format).
# 1. Unpack artifact to a temp dir.
# 2. Compare file list against manifest.json: exact match, no extras.
Get-FileHash -Algorithm SHA256 out/tutdata.pkg
# 3. Record: source hash + artifact hash + tool version in verify/five-loads.md
```

Rebuild twice from clean checkouts: identical bytes both times. A nondeterministic build cannot be verified, rolled back, or trusted across machines. If timestamps leak into the artifact, normalize or exclude them.

## Part 4 - Offline validator, complete

The validator runs without the game and gates every launch. It must reject bad input to earn trust on good input.

```python
# validate.py - illustrative: adapt sheet/field access to the real schema.
import json, sys

errors = []
def reject(reason):
    errors.append(reason)

baseline = json.load(open('baseline.json'))   # observed stock catalog
source = json.load(open('src/items.tut.json')) # your rows only

# 1. Baseline integrity: every baseline row present, identical, in order.
#    (Compare against OBSERVED baseline from the merge observer runs,
#    never against remembered counts.)
# 2. Additive-only: row ids in source all carry the TUT_ prefix;
#    no baseline id appears in source (no silent overwrites).
# 3. Reference integrity: every reference in source resolves in
#    baseline-plus-source.
# 4. Parent completeness: any parent set touching your rows is complete.
# 5. Negative suite (each must be REJECTED with a named error):
#    - duplicate id within source
#    - reference to a nonexistent id
#    - wrong field type on any field
#    - stock row reordered or altered (simulate by mutation)
#    - TUT_ row missing a required field

if errors:
    print('REJECT:')
    for e in errors:
        print(' -', e)
    sys.exit(1)
print('READY: stock intact, N rows added, all references resolve')
```

Validator discipline:

- Negative cases are first-class: a validator that only checks the happy path is a rubber stamp.
- Run it pre-launch every time and in CI if you have it. Skipped validation under time pressure is how bad overlays ship.
- Version the baseline with the game version. A game update invalidates old baselines; re-observe before re-validating.

## Part 5 - The five-load proof

Disposable fixture, copied test save, hash-verified backup. Record every step in `verify/five-loads.md` with hashes:

| Load | Setup | Expected |
|---|---|---|
| 1 | Clean fixture, no mod | Baseline counts recorded, title clean, no errors |
| 2 | Mod installed | Baseline plus exactly your rows; spawn item; name, icon, stats correct |
| 3 | Save, exit, reload with mod | Item persists and works; counts unchanged |
| 4 | Mod removed, reload same save | Graceful missing-content handling; run intact; no corruption |
| 5 | Backup restored, mod reinstalled | Full function back; hashes match load-3 state |

Rollback between loads is hash-verified restore, not "it looked fine". Any unexpected file delta fails the step. The five-load record (dates, hashes, counts, log excerpts) is the deliverable - the overlay without it is unproven.

## Part 6 - Troubleshooting matrix

| Symptom | Likely cause | Fix |
|---|---|---|
| Rows absent in game | Packaged after publication merge, or wrong install path | Verify placement and timing via the merge observer |
| Stock behavior changed | Replacement overlay or edited stock row | Return to additive-only; diff source against baseline |
| Works for you, broken elsewhere | Reference to a file not in the package | Unpack artifact, diff file list against manifest |
| Breaks after game update | Stale baseline or replacement copy | Re-observe baseline, re-validate, rebuild |
| Removal corrupts save | No graceful-absence path | Design blocked-resume; re-prove load 4 |
| Two mods conflict | Same sheet shipped twice, or ID collision | Split sheet ownership; namespace all IDs |

## Safety and rollback

- Data mods are save mods: disposable copies, full hash-verified backup before every session.
- Keep the observed baseline recorded per game version so every overlay stays diffable.
- Never hand-edit a save to fix a mod problem. Fix the mod, re-validate, re-prove the five loads.

## Further reading

- [Custom Equipment](../custom-equipment/index.md) - the items these rows describe.
- [Custom Levels and Biomes](../levels-biomes/index.md) - the rooms these rows describe.
- [Debugging & Testing](../debugging-testing/index.md) - fixtures, snapshots, and proof discipline.
