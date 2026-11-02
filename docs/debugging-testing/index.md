# Debugging and Testing

Prove your mod works instead of hoping it does. This guide builds the complete testing discipline the other tutorials assume: reading logs first, isolating failures, disposable fixtures with hash-verified snapshots, and probes that collect evidence without changing behavior.

**You will produce:** a reusable test fixture workflow, a snapshot-and-compare habit, and a written proof record for every mod you ship.
**You will verify:** clean-baseline runs, isolated failure reproduction, and full rollback after every session.

## Prerequisites

- [Getting Started](../getting-started/03-first-mod.md) completed - you have launched modded at least once and found the log file.
- One mod under test. The examples below assume the TitleScreenTag mod; the same steps apply to any mod in this handbook.

## Part 1 - The log is always step one

Every debugging session starts the same way: open the latest log in `coremod/logs/` and read it before changing anything. The log answers three questions in order:

1. **Did my mod load?** Search for your attach line (`HelloTitle: hook attached`, `Waveblade M38: factory and swing hooks attached`). Absent means a loading problem: folder placement, manifest, entry-point names, or wrong launcher. Do not touch logic until this line exists.
2. **Did my hook fire?** Search for your runtime lines (emission logs, grant logs, refusal reasons). Attach line present but no runtime lines means a wiring problem: hook names, delegate shapes, or env gates.
3. **Did my logic fail?** Search for your warning lines. The fail-open warnings name the exact failure (`label append failed`, `activation refused`, `damage scaling skipped`). Each one tells you which side broke.

Read in that order every time. Jumping to step 3 with step 1 unproven is how hours disappear: you will be debugging logic that never ran.

Log hygiene makes this possible: one line per significant event, machine-parseable fields, hard caps on per-event logging (the `VerboseLimit` pattern in the examples). A probe that logs every hit without a cap will bury the one line you need and slow the game while doing it. Never log per-tick; never log without a budget.

## Part 2 - Isolate before you fix

A failure in a full setup has too many suspects. Isolate with this sequence:

1. **Disable half.** Remove half your hooks (or test with only the mod's entry point active). Failure persists: the bug is in the active half. Failure vanishes: it is in the removed half. Repeat until one hook remains.
2. **Minimal repro.** Reproduce with the smallest possible trigger: one swing, one kill, one activation. If it takes a twenty-minute run to reproduce, you cannot iterate.
3. **Change one thing.** One edit, one rebuild, one redeploy, one run. Two simultaneous changes that fix the bug teach you nothing about which one mattered - and the wrong lesson ships.
4. **Read the error, all of it.** Exception type, message, and the first three stack lines name the failing call. Warnings about marshaling mean the handoff to the game; warnings about reads mean missing objects; silence with no crash means the hook never fired.

Common isolation results and what they mean: fails on clean fixture too means your mod, not interference. Passes clean but fails in the full set means a mod conflict (shared sheets, shared hook points, load order). Fails intermittently means shared state, ordering, or timing - look for globals, missing gates, and wall-clock use.

## Part 3 - Fixtures and snapshots

A fixture is a disposable install used for exactly one experiment: stock game copy plus only the mod under test, empty saves, no Workshop content, no cloud sync, stable path outside temp folders. Build it with a script so it is reproducible by you or anyone following your notes:

```powershell
# Illustrative fixture script (adapt paths to your machine).
$Fixture = 'D:\TutFixture\R01-Clean'
$Install = '<Dead Cells install>'
# 1. Copy stock install.
Copy-Item -Recurse $Install $Fixture
# 2. Prune to stock-plus-your-mod: remove other mods, empty saves,
#    confirm no Workshop content, install ONLY the mod under test.
# 3. Record pre-run hashes of saves, configs, and mod manifests.
Get-ChildItem $Fixture -Recurse -File |
    Get-FileHash -Algorithm SHA256 |
    Export-Csv pre-hashes.csv
# 4. Launch the modded executable from the FIXTURE (never the live
#    install), perform the test, exit normally through the menus.
# 5. Re-hash and diff: unexpected deltas fail the run.
```

Acceptance for a clean run: expected behavior observed, zero errors in the log, post-run hashes identical except explicitly expected files (cache, logs). Write all four down or the run did not happen. A fixture that cannot state what "clean" looks like cannot prove anything - it can only produce anecdotes.

Snapshots are the other half: before every modded session, back up saves and hash-verify the backup. After the session, rollback is restore-plus-verify, not "it looked fine." The five-load proof in [Game Data](../game-data/index.md) is this discipline applied to data mods; apply the same shape to every mod type.

## Part 4 - Probes that do not lie

A probe measures; it never modifies. Rules for trustworthy probes, drawn from the example code:

- Read the record the engine just produced; never recompute what you are measuring.
- Tag every sample with source, victim, and event ID so overlapping events stay distinguishable.
- Bound the sample count and the log budget. A probe that changes timing or floods the log invalidates its own data.
- Analyze copies, never live files. Copy the log out of the fixture, then run counts and group-bys over the copy: events per ID (duplicates show here), agreement between displayed and native values, attribution correctness across simultaneous events, counter behavior across save and reload.

The probe section of [Gameplay Modifications](../gameplay-mods/index.md) shows this pattern running; the discipline here is when and how to deploy it.

## Part 5 - Failure catalog

| Symptom | Likely cause | Where to look |
|---|---|---|
| No attach line in log | Loading problem: folder, manifest, entry point, wrong launcher | Fixture mod folder listing, `ModMain` vs code names |
| Attach line present, nothing happens, no warning | Hook name or delegate shape mismatch; env gate unset | API docs, character-for-character hook strings |
| Attach line present, warning present | Logic-side failure | The warning text - it names the failure |
| Crash on trigger | Exception outside try/catch, or skipped orig | Wrap every game touch; orig discipline |
| Works clean, breaks in full set | Mod conflict: shared sheet, shared hook, load order | Isolate by halves |
| Intermittent failure | Shared state, ordering, wall clock | Globals, missing gates, native-clock conversion |
| Corrupt save after removal | No graceful-absence path | Removal test from the five-load proof |
| Ghost effects after level change | Missing cleanup path | Cleanup matrix from the animations tutorial |
| Numbers wrong only in boss rooms | Protections, caps, phase rules | Route through native calls; never bypass |

## Safety and rollback

- Disposable fixtures only. Never test on live progression, never sync experiments to cloud, never hand-edit a save to fix a mod problem.
- Every session: backup with hash verification before, rollback with verification after. Unverified backups are hopes, not restores.
- Write the run authorization before launching: what runs, for how long, what aborts it, what clean looks like. On any surprise: roll back first, diagnose from sealed logs, never another run on a dirty fixture.

## Further reading

- [Game Data](../game-data/index.md) - the five-load proof and validation suites.
- [Custom Equipment](../custom-equipment/index.md) - the full verification table this discipline executes.
- [Gameplay Modifications](../gameplay-mods/index.md) - probes and the two-session test protocol.
- [Custom Levels and Biomes](../levels-biomes/index.md) - the strictest fixture regime in this handbook.
