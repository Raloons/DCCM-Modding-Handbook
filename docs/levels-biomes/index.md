# Custom Levels and Biomes (status: concept - generalized pattern, examples not live-verified)

How to think about custom rooms and biomes: what the engine does at startup, where a mod can observe or extend it, and why each step needs its own proof. All examples are independently written teaching sketches. Level work is the highest-risk modding area - read the safety sections twice.

## Concepts you need first

### Startup merges data; generation reads it later

At a high level, game startup merges data from multiple sources (stock files plus each mod's packaged data) into the catalogs the game then uses, and level generation reads those catalogs much later when building a run. Consequences:

- Your data must be present and valid *before* the merge that publishes it; late writes after publication are ignored or, worse, half-applied.
- Observing the merge (read-only) and changing the merge (adding rows) are different operations with different risk. Always build the observer first.
- Generation-time selection (which room template, which exits) has its own filters and fallbacks on top of the catalog. A row present in the catalog is necessary but not sufficient for appearing in a run.

### Catalog presence, selection, and travel are separate proofs

A custom biome is not one feature but a chain, and each link needs its own test:

1. **Catalog:** your level and room rows are visible in the merged catalog next to all stock rows, with stock rows byte-identical.
2. **Selection:** the generator actually picks your room (force-select it in a test fixture; random appearance is not proof).
3. **Travel:** the player can walk the full route - source exit, your rooms, destination exit - with all stock routes preserved.
4. **Persistence:** mid-biome save, reload, resume, and clean removal all work.

Do not start step 2 until step 1 is proven, and never ship step 3 without step 4.

### Additive rows, exact identity, fail-open fallbacks

- Add rows; never rewrite stock rows. Your removal must restore vanilla exactly.
- Custom levels need unique namespaced IDs. Generation hooks must match those IDs exactly and refuse everything else.
- Every generation adapter needs a fail-open fallback: if your room cannot be used (missing asset, failed validation), generation continues with a stock room instead of aborting the run.

### Fixtures are disposable by design

Level testing needs a purpose-built disposable install: stock game plus only your mod, empty saves, no Workshop content, no production cloud sync. One fixture per experiment, thrown away afterward. If a test cannot state its rollback (which files change, what "clean" hashes look like), it is not ready to run.

## Tutorial: a read-only merge observer

Before adding anything, prove you can watch the catalog merge without changing it. This diagnostic is the pattern behind every safe level experiment: log-only, gated, exactly-once forwarding.

```csharp
using HaxeProxy.Runtime;
using ModCore.Mods;

namespace TutLevels;

public class TutMergeObserverMod(ModInfo info) : ModBase(info)
{
    private bool initialized;

    public delegate void OrigLoading(object self, object data);

    public override void Initialize()
    {
        if (initialized) return;
        initialized = true;
        // Teaching sketch: attach at the catalog-ready broadcast seam
        // documented in the DCCM API docs. Log only. Never mutate.
        HashlinkHooks.Instance
            .CreateHook("tut.TutCatalog", "onCatalogReady", OnCatalogReady)
            .Enable();
        Logger.Information("TutObserver: read-only tap attached");
    }

    private void OnCatalogReady(OrigLoading orig, object self, object data)
    {
        orig(self, data); // exactly once, exceptions preserved
        try
        {
            // Teaching sketch of what to record:
            // - count of level rows and room rows (expect stock counts)
            // - presence/absence of YOUR test ID (expect absent: you
            //   added nothing yet - this run calibrates the baseline)
            // - ordered parent/difficulty IDs (expect stock order)
            // Write to your own log file with a hard line cap.
            RecordBaselineSnapshot(data);
        }
        catch (Exception ex)
        {
            // An observer must never break loading.
            Logger.Warning(ex, "TutObserver: snapshot skipped");
        }
    }

    private void RecordBaselineSnapshot(object data)
    {
        throw new NotImplementedException("teaching sketch");
    }
}
```

### Why each piece is shaped this way

- **Read-only first** gives you a stock baseline (exact counts and order) that every later additive change is diffed against.
- **Exactly-once forwarding with preserved exceptions** means the game cannot tell your observer is there.
- **A hard log cap** keeps a per-row diagnostic from writing megabytes into a fixture you later hash-verify.
- **Expect-absent calibration** (your ID absent on the clean run) proves the tap observes the real catalog rather than a fixture of your imagination.

## Tutorial: from observation to one additive room

Once the baseline is proven, the smallest possible extension:

1. Package one room row plus its parent-entries additively (no stock row touched).
2. Re-run the observer: stock counts plus exactly your addition, stock order preserved.
3. Force-select the room in a disposable fixture and walk it; screenshot the layout, then save, reload, and resume inside it.
4. Remove the mod and reload the same save slot: the game must handle the missing custom content gracefully (blocked resume with a clear state, never a corrupted run).

Only after all four steps pass is the room a candidate for a route (exits in and out), and only after the route is walked is it a candidate for art, enemies, and rewards.

## Common failures

- Writing rows after the catalog is published (silently ignored).
- Assuming catalog presence implies generation selection.
- No fail-open fallback, so one bad room aborts entire runs.
- Testing travel on a live progression save instead of a disposable fixture.
- Treating a forced single-room appearance as proof of random-generation integration.
- Shipping without the removal test (players uninstall mods; their saves must survive).

## Safety and rollback

- This is the strictest safety regime in the handbook: disposable installs, empty saves, cloud sync off, hash-verified pre/post snapshots of every protected file.
- Never touch production saves, cloud data, entitlements, or Workshop state.
- Each experiment gets one authorization-equivalent decision: what runs, for how long, what aborts it, what clean looks like. Write it down before launching.
- If anything unexpected happens, roll back first and diagnose from the sealed logs - never "one more run" on a dirty fixture.

## Further reading

- [Game Data](../game-data/index.md) - packaging the rows this chapter consumes.
- [Custom Equipment](../custom-equipment/index.md) - enemies and rewards come later; weapons first.
- [Debugging & Testing](../debugging-testing/index.md) - fixtures, snapshots, and proof discipline.
