# Custom Levels and Biomes (status: concept - generalized pattern, examples not live-verified)

Add a custom room to the game without breaking generation, travel, saves, or other mods. This guide builds a read-only merge observer, a disposable-fixture workflow, an offline validation script, and the four-gate path from catalog presence to a walkable room. Level work is the highest-risk area in this handbook - follow the safety sections exactly.

**You will produce:** a `TutObserver` merge diagnostic, a `TutRoom` one-room additive package, an offline validator, and a fixture runbook with acceptance records.
**You will verify:** baseline catalog counts, additive-only diffs, forced selection, physical traversal, save/resume inside the room, and graceful removal.

## Prerequisites

- [Game Data](../game-data/index.md) concepts (additive overlays, reference integrity).
- Comfort with PowerShell basics (hashing, copying trees) and reading game logs.
- A machine where you can create an isolated test install without touching your live game.

## Part 1 - Concepts

### Startup merges data; generation reads it later

At startup the game merges data from stock files plus each mod's packaged data into live catalogs, then level generation reads those catalogs when building runs. Consequences:

- Your rows must be present and valid *before* the merge that publishes them. Writes after publication are ignored or half-applied.
- Observing the merge (read-only) and extending it (adding rows) are different risk levels. Always ship the observer first and calibrate a baseline.
- Generation-time selection has its own filters and fallbacks on top of the catalog. Catalog presence is necessary but never sufficient for appearing in a run.

### Four gates, in order, no skipping

1. **Catalog:** your level and room rows visible in the merged catalog; all stock rows byte-identical in payload and order.
2. **Selection:** the generator picks your room. Force-select it in a test fixture - random appearance is not proof.
3. **Travel:** walk the full route - source exit, your rooms, destination exit - with every stock route preserved.
4. **Persistence:** save mid-room, reload, resume, remove the mod, reload again gracefully.

Gate N+1 does not start until gate N passes with written evidence.

### Additive rows, exact identity, fail-open generation

- Add rows; never rewrite stock rows. Removal must restore vanilla exactly.
- Namespaced IDs (`TUT_DemoLevel`, `TUT_DemoRoom`), matched exactly by your adapters, refusing everything else.
- Every generation adapter carries a fail-open fallback: unusable custom room means a stock room is used, never an aborted run.

### Fixtures are disposable by design

One fixture per experiment: stock install copy plus only your mod, empty saves, no Workshop content, no cloud sync, stable path outside temp folders. Record pre-run hashes of every protected file; after the run, prove byte-identity (except explicitly expected run-progress files) or the fixture is dirty and the result is void.

## Part 2 - Read-only merge observer, complete

Calibrate the baseline before adding anything. Log-only, gated, exactly-once forwarding, hard log cap.

```csharp
using HaxeProxy.Runtime;
using ModCore.Mods;

namespace TutLevels;

public class TutObserverMod(ModInfo info) : ModBase(info)
{
    private const string GateEnv = "TUT_MERGE_OBSERVE"; // "1" enables
    private const int MaxLines = 4000;
    private int linesWritten;
    private bool initialized;

    public delegate void OrigCatalogReady(object self, object catalog);

    public override void Initialize()
    {
        if (initialized) return;
        initialized = true;
        if (Environment.GetEnvironmentVariable(GateEnv) != "1")
        {
            Logger.Information("TutObserver: dormant (set TUT_MERGE_OBSERVE=1)");
            return; // default OFF: zero game impact unless asked
        }
        HashlinkHooks.Instance
            .CreateHook("tut.TutCatalog", "onCatalogReady", OnCatalogReady)
            .Enable();
        Logger.Information("TutObserver: read-only tap attached");
    }

    private void OnCatalogReady(OrigCatalogReady orig, object self, object catalog)
    {
        orig(self, catalog); // exactly once, exceptions preserved
        try { RecordBaseline(catalog); }
        catch (Exception ex)
        {
            // An observer must never break loading.
            Logger.Warning(ex, "TutObserver: snapshot skipped");
        }
    }

    private void RecordBaseline(object catalog)
    {
        // Illustrative record shape (adapt field access to the real API):
        // - level row count, room row count, ordered parent ids
        // - presence of TUT_DemoLevel / TUT_DemoRoom (expect ABSENT:
        //   this run calibrates the clean baseline)
        // - ordered parent id sequence (expect stock order, hashed)
        // Stop at MaxLines no matter what.
        if (linesWritten >= MaxLines) return;
        linesWritten++;
        throw new NotImplementedException("teaching sketch: read via real API");
    }
}
```

Why this exact shape:

- **Default-dormant with env gate:** the tap cannot affect normal play; it exists only when explicitly enabled for a fixture run.
- **Expect-absent calibration:** recording that your IDs are absent on the clean run proves the tap sees the real catalog.
- **Hashed order sequences, not full dumps:** a hash of the parent order detects any reordering in one line instead of thousands.
- **Hard line cap:** a per-row diagnostic that cannot stop writing will fill disks and invalidate hash comparisons.

Run it twice on the clean fixture: the two snapshots must be identical. That identical pair is your baseline. Any later additive change is diffed against it.

## Part 3 - Disposable fixture runbook

Script every fixture build so it is reproducible by you or an AI agent following the same steps:

```powershell
# Illustrative fixture script (adapt paths to your machine).
$Fixture = 'D:\TutFixture\R01-Clean'
$Install = '<Dead Cells install>'
# 1. Copy stock install.
Copy-Item -Recurse $Install $Fixture
# 2. Prune to stock-plus-observer: remove other mods, clear saves,
#    confirm no Workshop content, install ONLY TutObserver.
# 3. Set the env gate for this run only: $env:TUT_MERGE_OBSERVE = '1'
# 4. Record pre-run hashes of saves, configs, and mod manifests.
Get-ChildItem $Fixture -Recurse -File |
    Get-FileHash -Algorithm SHA256 |
    Export-Csv pre-hashes.csv
# 5. Launch the modded executable from the FIXTURE (never the live
#    install), reach title, exit normally.
# 6. Re-hash and diff: unexpected deltas fail the run.
```

Acceptance for a clean baseline run: title reached, observer snapshot written, zero errors, post-run hashes identical except explicitly expected cache/log files. Write all four down or the run did not happen.

## Part 4 - Offline validator

Validate the additive package without launching the game. Sketch in Python (runs anywhere, no game needed):

```python
# Illustrative validator: adapt sheet/field names to the real schema.
import json, sys

baseline = json.load(open('baseline-catalog.json'))   # from observer runs
candidate = json.load(open('candidate-overlay.json'))  # your package source

# 1. Every baseline row present, byte-identical, in identical order.
# 2. Exactly your N rows added (by namespaced TUT_ prefix allowlist).
# 3. Every reference in added rows resolves (rooms, parents, loot).
# 4. Parent sets including your entry are complete, none half-added.
# 5. Negative cases: duplicate id, dangling reference, wrong type,
#    reordered stock rows - each must be REJECTED with a named error.

print('READY' if all_checks_pass else 'REJECT with reasons')
sys.exit(0 if all_checks_pass else 1)
```

Rules for the validator itself:

- It must reject bad input to earn trust on good input. Ship the negative suite alongside it.
- It compares against the *observed* baseline from Part 2, never against remembered counts.
- It runs in CI (or at least pre-launch, every launch). A validation that is skipped under time pressure is decoration.

## Part 5 - From observation to one walkable room

With baseline and validator green:

1. Author one room row plus its parent entries additively. One room, minimal geometry, stock presentation. No art, no enemies, no loot yet.
2. Re-run the validator, then the observer in a fresh fixture: stock counts plus exactly your addition, stock order preserved.
3. Force-select the room (test-only forced template path with stock fallback) and walk it: reach both edges, confirm collision matches the authored geometry, screenshot the layout.
4. Save inside the room, exit, reload, resume: position and state correct.
5. Walk out through the custom exit to the destination; walk every adjacent stock route to confirm nothing else changed.
6. Remove the mod, reload: graceful missing-content handling, run intact, no corruption.

Then - and only then - consider a second room, a route, art, enemies, and rewards, each repeating the same six steps.

## Part 6 - Troubleshooting matrix

| Symptom | Likely cause | Fix |
|---|---|---|
| Custom rows absent from snapshot | Packaged after publication merge, or wrong package path | Verify package placement and merge timing with the observer |
| Rows present but never selected | Selection filters or fallback rejected the room | Check eligibility (size, tags, exits); keep the stock fallback and read why it fired |
| Run aborts on your room | No fail-open fallback | Add stock fallback; an experiment must never kill runs |
| Snapshot differs between clean runs | Nondeterministic order or unseeded content | Hash ordered sequences; separate stable from nondeterministic fields |
| Fixture dirty after clean run | Cache/log writes outside the allowlist | Extend the expected-delta list explicitly, never silently |
| Removal corrupts the save | Missing graceful-absence path | Design blocked-resume with a clear message; re-prove gate 4 |

## Safety and rollback

- Strictest regime in this handbook: disposable installs, empty saves, cloud off, hash-verified snapshots, written run authorization (what runs, how long, what aborts, what clean looks like) before every launch.
- Never touch production saves, cloud data, entitlements, or Workshop state from an experiment.
- On any surprise: roll back first, diagnose from sealed logs. Never another run on a dirty fixture.

## Further reading

- [Game Data](../game-data/index.md) - packaging the rows this chapter consumes.
- [Custom Equipment](../custom-equipment/index.md) - enemies and rewards come after rooms work.
- [Debugging & Testing](../debugging-testing/index.md) - fixtures, snapshots, and proof discipline.
