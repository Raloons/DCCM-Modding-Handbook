# Animations and Visual Effects (status: concept - generalized pattern, examples not live-verified)

How to present custom effects with the engine instead of against it. All examples are independently written teaching sketches. Verify in your own fixture.

## Concepts you need first

### The engine owns the sprite; you borrow space next to it

Stock sprites are re-synchronized by the engine every frame (position, tile, visibility). The classic beginner mistake is assigning a custom tile to a stock sprite and watching it work for one frame, then get overwritten by the next sync. The robust pattern is additive:

- Leave the stock sprite's tile assignment alone.
- Attach your own child sprite (or bitmap) to the same parent for your effect.
- If the stock visual must disappear, hide the stock sprite (`visible = false`) rather than retiling it - hiding survives sync, retiling does not.

### Layout containers reposition their children

UI and scene graphs often use auto-layout containers that place children by their own rules every reflow. Lessons learned the hard way:

- Never fight the layout every frame with forced reposition calls; that path leads to resize bugs and stuck windows.
- Do your placement in (or just after) the container's own reflow step, then leave it alone.
- Prefer containers with zero intrinsic size for overlay anchors so they do not disturb sibling layout.

### Every visual needs an owner and a funeral

An effect that outlives its context is a crash waiting for a later level. For each visual, decide up front:

- **Budget:** maximum live nodes (a dozen is plenty for most effects; share one pool, never allocate unboundedly per frame).
- **Lifetime:** exact despawn condition - effect end, swing end, level leave, mod unload. Whichever comes first.
- **Cleanup owner:** level-owned visuals die with the level; run-owned visuals die with the run; nothing lives forever.

## Tutorial: an additive slash echo

A slash echo that lingers where the swing happened, then fades. The world-space anchoring (capture the position at swing time, do not follow the hero) is what makes it look right when the player keeps moving.

```csharp
using HaxeProxy.Runtime;
using ModCore.Mods;

namespace TutVfx;

public class TutEchoMod(ModInfo info) : ModBase(info)
{
    private const int MaxEchoes = 12; // shared pool, never more
    private bool initialized;

    public delegate void OrigSwingEnd(object self);

    public override void Initialize()
    {
        if (initialized) return;
        initialized = true;
        HashlinkHooks.Instance
            .CreateHook("tut.TutBlade", "onSwingEnd", OnSwingEnd).Enable();
        Logger.Information("TutEcho: hook attached");
    }

    private void OnSwingEnd(OrigSwingEnd orig, object self)
    {
        // Capture the world anchor BEFORE running anything else:
        // facing, position, and swing direction at this exact tick.
        var anchor = CaptureAnchor(self);
        orig(self);
        try
        {
            SpawnEcho(anchor); // oldest echo recycled past MaxEchoes
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TutEcho: echo skipped");
        }
    }

    private object CaptureAnchor(object self)
    {
        // Teaching sketch: snapshot facing + position into an
        // immutable record. The echo must never hold a live
        // reference to a mutable hero or level object.
        throw new NotImplementedException("teaching sketch");
    }

    private void SpawnEcho(object anchor)
    {
        // Teaching sketch of the lifetime contract:
        // - draw under the level scroller so it stays where it happened
        // - fade over a fixed tick count, then release the node
        // - level leave releases the whole pool immediately
        throw new NotImplementedException("teaching sketch");
    }
}
```

### Why each piece is shaped this way

- **Immutable anchor snapshots** instead of live references: the hero moves, the level unloads, sprites get recycled - a snapshot cannot dangle.
- **A fixed pool with recycling** instead of per-swing allocation: worst-case node count is provable by reading the code.
- **Level-leave mass release**: one hook that frees the whole pool covers every forgotten edge case at once.
- **Fail open**: a broken cosmetic never breaks the swing that triggered it.

## Tutorial: placing UI without fighting the layout

When adding a title-screen or HUD element:

1. Find the stable parent container (the one that survives resizes and reflows).
2. Insert your container at an explicit child index, once, guarded by an applied flag.
3. Do fine positioning in the reflow step using coordinate conversion (global-to-local), never by forcing layout syncs from a per-frame hook.
4. Hide (do not destroy) stock elements you replace, so rollback is a visibility flip.

## Common failures

- Retiling a stock sprite instead of adding a child (works for one frame).
- Per-frame forced repositioning that breaks fullscreen and window resize.
- Echoes that follow the hero because they hold live references instead of snapshots.
- Unbounded per-frame allocation with no pool cap or level-leave release.
- Custom art drawn from another mod's or the game's files instead of your own assets.

## Safety and rollback

- Visual mods are low-risk but not zero-risk: broken cleanup corrupts later loads, not the current screen.
- Test the full loop: trigger the effect, change levels, save, reload, remove the mod, reload again.
- Ship only original art. Never redistribute extracted frames, atlas pages, or sound files.

## Further reading

- [Custom Equipment](../custom-equipment/index.md) - the weapons these effects present.
- [Gameplay Mods](../gameplay-mods/index.md) - timing your visuals to game events.
- [Debugging & Testing](../debugging-testing/index.md) - proving cleanup across level transitions.
