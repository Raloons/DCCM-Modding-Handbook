# Gameplay Modifications (status: concept - generalized pattern, examples not live-verified)

How to change combat behavior, damage, cooldowns, and movement without destabilizing the run. All examples are independently written teaching sketches. Verify in your own fixture.

## Concepts you need first

### Orig-first, fail-open, exactly-once

Three rules govern every gameplay hook:

1. **Orig first.** Call the original implementation before your logic so native state (timers, cycles, protections) is settled when you read it.
2. **Fail open.** Any exception in your logic must leave the native behavior intact. A broken bonus is a balance bug; a broken `orig` call is a crash.
3. **Exactly-once per event.** If the engine can deliver an event twice (retries, reloads, duplicated callbacks), your effect must still apply once. Ring-buffer the IDs of processed events and refuse replays.

### Read authoritative state, not derived displays

Cooldown progress bars, displayed damage numbers, and cached UI values are projections - they can disagree with the authoritative value on edge ticks, during reloads, or under effect stacking. Always read the native timer or counter your gate is defined against (remaining cooldown time, live hit result), never the display derived from it.

### Attribute shared events to their source

Kills, hits, dodges, and pickups can overlap: two victims die on the same tick, a damage-over-time tick lands during a swing. Key every piece of effect state by its source or victim identity, and clean the entry when that entity dies or the level unloads. Global counters and single-slot "last event" fields are the classic source of one player's reward landing on another player's kill.

### Respect protections you did not build

Bosses and special encounters carry damage caps, phase rules, and immunity windows owned by the native game. Your mod must never assume it can bypass them, and must never silently change them. Route your damage through the native damage call so caps, immunities, and difficulty scaling keep working exactly as stock.

## Tutorial: a guarded on-kill damage window

A mutation-style effect: killing an enemy grants a short damage window. The sketch shows victim-keyed attribution and exactly-once grants.

```csharp
using HaxeProxy.Runtime;
using ModCore.Mods;

namespace TutGameplay;

public class TutWindowMod(ModInfo info) : ModBase(info)
{
    // Victim-keyed state with bounded size: entries are removed on
    // victim death (consumed) and on level leave (swept).
    private readonly Dictionary<string, long> grants = new();
    private const int MaxTracked = 64;
    private bool initialized;

    public delegate void OrigKill(object self, object victim);

    public override void Initialize()
    {
        if (initialized) return;
        initialized = true;
        HashlinkHooks.Instance
            .CreateHook("tut.TutRules", "onKill", OnKill).Enable();
        Logger.Information("TutWindow: hook attached");
    }

    private void OnKill(OrigKill orig, object self, object victim)
    {
        orig(self, victim); // native kill (loot, counters, phases) first
        try
        {
            string id = VictimKey(victim); // stable per-victim identity
            if (grants.ContainsKey(id)) return; // duplicate delivery: refuse
            if (grants.Count >= MaxTracked) return; // bound the map
            grants[id] = GrantWindow(self); // authoritative timer used
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TutWindow: window grant skipped");
        }
    }

    private string VictimKey(object victim)
    {
        // Teaching sketch: derive a stable identity for the victim.
        // Never use a global incrementing counter as the key.
        throw new NotImplementedException("teaching sketch");
    }

    private long GrantWindow(object self)
    {
        // Teaching sketch: start the window from the NATIVE clock and
        // return the grant record. Consumption checks the same clock,
        // never a UI progress value.
        throw new NotImplementedException("teaching sketch");
    }
}
```

### Why each piece is shaped this way

- **Duplicate-delivery refusal** makes retries and reloads safe by construction instead of by luck.
- **A bounded map with two cleanup paths** (consume on use, sweep on level leave) proves memory cannot grow across a run.
- **Native-clock windows** stay correct when the game pauses, hitches, or stacks other time effects; tick-counted windows drift.

## Tutorial: a per-hit probe that does not lie

When investigating or displaying per-hit behavior (damage numbers, proc rates, stacking interactions), sample at the hit-resolution step with the live hit record:

1. Read the hit record the engine just produced - do not recompute damage yourself.
2. Tag each sample with attacker, victim, and event ID so overlapping hits stay distinguishable.
3. Write samples to a bounded, log-budgeted sink; a probe that spams the log every hit will hide the bug you are hunting and slow the game.

Probes are read-only by definition: a probe that *changes* damage to measure it invalidates its own data.

## Common failures

- Skipping `orig` or calling it twice (double application, broken native state).
- Gating on UI progress instead of the authoritative timer.
- Global "last kill" state shared across simultaneous events.
- Recomputing damage instead of routing through the native call, silently dropping caps and protections.
- Unbounded log spam from per-hit hooks.
- Assuming test-room numbers transfer to boss encounters with different protections.

## Safety and rollback

- Gameplay mods touch saves and run state: disposable copied saves only, hash-verified backup and restore around every session.
- Prove the loop: trigger, save, reload, remove mod, reload again - the run must survive each step.
- Never promise balance ("2x damage") from a single observation; level scaling, difficulty tiers, and stacking change the number.

## Further reading

- [Custom Equipment](../custom-equipment/index.md) - weapons and skills that use these hooks.
- [Animations & VFX](../animations-vfx/index.md) - presenting windows and procs.
- [Game Data](../game-data/index.md) - the rows behind the numbers.
- [Debugging & Testing](../debugging-testing/index.md) - probes, fixtures, and proof.
