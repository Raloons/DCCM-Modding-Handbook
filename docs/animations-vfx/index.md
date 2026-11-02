# Animations and Visual Effects

Ship visual effects that survive the engine's sync, the layout's reflow, and the player's movement. This guide builds a complete pooled slash-echo system and a layout-safe UI element, with budgets, cleanup proofs, and performance checks. All code is independently written teaching material.

**You will produce:** a `TutEcho` pooled world-space echo system plus a `TutLabel` layout-safe UI addition, with a cleanup matrix covering every exit path.
**You will verify:** pool caps hold under spam, echoes anchor correctly while moving, UI survives resizes, and level transitions release everything.

## Prerequisites

- [Custom Equipment](../custom-equipment/index.md) concepts (orig-first, fail-open, victim-keyed state) - Part 1 at minimum.
- A disposable fixture where you can swing freely and change levels without consequences.

## Part 1 - Concepts

### The engine owns the sprite; you borrow space next to it

Stock sprites are re-synchronized every frame. Assigning your tile to a stock sprite works for exactly one frame, then sync overwrites it. The additive pattern:

- Never retile stock sprites. Add your own child sprite to the same parent.
- To remove a stock visual, hide the sprite (`visible = false`). Hiding survives sync; retiling does not.
- Parent your effect to the moving body sprite (not the scene root) when it must share pivot, facing, and movement; parent to the level scroller (not the hero) when it must stay where it happened.

### Layout containers reposition children on reflow

Auto-layout containers place children by their own rules at reflow time. Rules learned the hard way:

- Position in or just after the reflow step using coordinate conversion (global-to-local). Never force layout syncs from a per-frame hook - that path breaks resize and fullscreen.
- Overlay containers should have zero intrinsic size so they do not push siblings around.
- Guard insertion with an applied flag: insert once at an explicit child index, then only adjust coordinates.

### Every visual needs a budget, a lifetime, and a funeral

- **Budget:** a fixed pool size (12 is generous for echoes; UI needs 1). Worst-case node count must be readable from the code.
- **Lifetime:** exact despawn condition per node (tick count, swing end, effect end). Whichever comes first.
- **Funeral:** level leave, run end, and mod unload release the whole pool unconditionally. One mass-release hook covers every forgotten edge case.

## Part 2 - Example VFX

This is source code of a melee weapon's trail. This wants to be separated from the weapon so it doesn't just disappear when you move left or right by even a pixel.

```csharp
using HaxeProxy.Runtime;
using ModCore.Mods;

namespace TutVfx;

public sealed class EchoNode
{
    public object? Handle;   // live sprite handle or null when free
    public int TicksLeft;    // fixed fade duration in ticks
    public bool InUse;
}

public class TutEchoMod(ModInfo info) : ModBase(info)
{
    private const int PoolSize = 12;
    private const int FadeTicks = 10; // ~0.33s at 30 ticks/sec
    private readonly EchoNode[] pool = new EchoNode[PoolSize];
    private bool initialized;

    public delegate void OrigSwingEnd(object self);
    public delegate void OrigTick(object self, double dt);
    public delegate void OrigLevelLeave(object self);

    public override void Initialize()
    {
        if (initialized) return;
        initialized = true;
        for (int i = 0; i < PoolSize; i++) pool[i] = new EchoNode();
        var hooks = HashlinkHooks.Instance;
        hooks.CreateHook("tut.TutBlade", "onSwingEnd", OnSwingEnd).Enable();
        hooks.CreateHook("tut.TutLevel", "onTick", OnTick).Enable();
        hooks.CreateHook("tut.TutLevel", "onLeave", OnLevelLeave).Enable();
        Logger.Information("TutEcho: pool ready");
    }

    private void OnSwingEnd(OrigSwingEnd orig, object self)
    {
        var anchor = CaptureAnchor(self); // immutable snapshot first
        orig(self);
        try { SpawnEcho(anchor); }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TutEcho: echo skipped");
        }
    }

    private void OnTick(OrigTick orig, object self, double dt)
    {
        orig(self, dt);
        try
        {
            foreach (var node in pool)
            {
                if (!node.InUse) continue;
                if (--node.TicksLeft <= 0) ReleaseNode(node);
                else FadeNode(node); // step opacity toward zero
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TutEcho: fade step skipped");
        }
    }

    private void OnLevelLeave(OrigLevelLeave orig, object self)
    {
        try
        {
            foreach (var node in pool) ReleaseNode(node);
        }
        finally { orig(self); }
    }

    private object CaptureAnchor(object self)
    {
        // Illustrative: snapshot facing + position + swing direction
        // into an immutable record. Never hold the hero, the sprite,
        // or any level object - all three can move or unload.
        throw new NotImplementedException("teaching sketch");
    }

    private void SpawnEcho(object anchor)
    {
        EchoNode? slot = null;
        foreach (var node in pool)
        {
            if (!node.InUse) { slot = node; break; }
        }
        slot ??= OldestNode(); // pool full: recycle oldest, never grow
        slot.TicksLeft = FadeTicks;
        slot.InUse = true;
        // Illustrative: create the sprite parented to the level
        // scroller at the anchor, store handle in slot.Handle.
    }

    private EchoNode OldestNode()
    {
        EchoNode oldest = pool[0];
        foreach (var node in pool)
        {
            if (node.TicksLeft < oldest.TicksLeft) oldest = node;
        }
        return oldest;
    }

    private void FadeNode(EchoNode node) { /* step opacity by TicksLeft */ }

    private void ReleaseNode(EchoNode node)
    {
        // Illustrative: destroy handle immediately, null it, mark free.
        node.Handle = null;
        node.InUse = false;
    }
}
```

Why this exact shape:

- **Snapshot before `orig`:** the swing-end pipeline may mutate facing or position; the anchor must predate it.
- **Recycle-oldest, never grow:** pool-full behavior is defined (oldest fades early), so spam-swinging cannot allocate.
- **Tick-driven fade, not wall-clock:** pausing the game pauses the effect; wall-clock fades desync from gameplay.
- **Mass release before `orig` on level leave** in `try/finally`: the transition always proceeds.

## Part 3 - Layout-safe UI element, complete

Adding one label to the title screen without fighting layout:

1. Find the stable parent container (survives resize and reflow).
2. Insert your zero-size container once at an explicit child index, guarded by an applied flag.
3. Position via global-to-local conversion in the reflow step.
4. Hide (never destroy) any stock element you replace.

```csharp
private void OnReflow(OrigReflow orig, object self)
{
    orig(self); // layout settles first
    if (self != parentContainer) return; // only our parent
    try { PositionOverlay(); }
    catch (Exception ex)
    {
        Logger.Warning(ex, "TutLabel: position skipped");
    }
}
```

Verification for UI work: resize the window mid-session, toggle fullscreen twice, change resolution. Your element must track its anchor through all three with no manual refresh, and the stock layout must be pixel-identical with your mod removed.

## Part 4 - Budgets and performance

- Count worst-case live nodes by reading the code (pool sizes plus UI elements). Write the number in your checklist. If you cannot state it, the design is not done.
- Per-frame work must be O(pool size): iterate, decrement, fade. No allocation in the tick path - allocate only on spawn.
- Measure, do not feel: log scene-build milliseconds before and after enabling the mod across 5 identical loads. A consistent regression over 2ms on the same machine deserves investigation; a single slow load proves nothing.
- Cap log output: per-tick logging is forbidden. Log spawns at debug level with a per-session line budget.

## Part 5 - Cleanup matrix (fill one per effect)

| Exit path | Echo pool | UI overlay | Proof |
|---|---|---|---|
| Effect ends naturally | Node released at 0 ticks | N/A (persistent) | Spam 50 swings, count live nodes = 0 after fade |
| Swing interrupted | Released at next swing-end or tick drain | N/A | Interrupt 20 swings, pool drains to 0 |
| Level change | Mass release before transition | Repositioned on reflow | Change levels 5 times, no errors, no ghosts |
| Save and reload | Pool empty at save; rebuilt empty | Rebuilt by init path | Reload mid-effect: clean state, no dangling handle |
| Mod removed | N/A (code gone) | Stock layout pixel-identical | Screenshot diff before/after |

## Part 6 - Troubleshooting matrix

| Symptom | Likely cause | Fix |
|---|---|---|
| Effect visible one frame then gone | Retiled a stock sprite; sync overwrote it | Add a child sprite; hide stock instead of retiling |
| Echo follows the hero | Live reference instead of snapshot | Capture immutable anchor at spawn |
| UI drifts on resize | Per-frame forced positioning | Move positioning to the reflow step |
| Fullscreen toggle breaks layout | Forced layout sync from update hook | Remove the forced sync; let reflow own layout |
| Nodes accumulate over time | Missing release path | Fill the cleanup matrix; add the missing mass release |
| Art looks wrong in game | Tested only at large size | Iterate at real 24px-equivalent size; simplify shapes |

## Safety and rollback

- Cosmetic bugs still corrupt later loads when cleanup is broken: always run the level-change and removal rows of the matrix.
- Ship only original art and audio. Never redistribute extracted frames, atlas pages, or sound files.
- Rollback is deleting the deployed folder plus restoring the save backup.

## Further reading

- [Custom Equipment](../custom-equipment/index.md) - the weapons these effects present.
- [Gameplay Mods](../gameplay-mods/index.md) - timing visuals to game events.
- [Game Data](../game-data/index.md) - packaging any art your effects need.
- [Debugging & Testing](../debugging-testing/index.md) - proving cleanup across transitions.
