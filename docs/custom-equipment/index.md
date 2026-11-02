# Custom Equipment (status: concept - generalized pattern, examples not live-verified)

How to design your first weapon, skill, or mutation mod from safe, reusable patterns. Every example below is independently written for teaching: it shows the *shape* of a working mod, not production code. Verify everything in your own disposable fixture before trusting it.

## Concepts you need first

### The factory dispatches, your subclass behaves

DCCM mods rarely build items from scratch. The usual pattern is:

1. The game's item factory decides *what* to spawn (from data rows).
2. Your mod subclasses a stock item class whose base behavior is closest to what you want (a fast melee, a bow, an active skill), and overrides only the behavior you are changing.
3. Your override calls the original implementation first (`orig`), then adds its own effect - or deliberately replaces one step while leaving the rest of the pipeline (damage numbers, sounds, cleanup) on the native path.

Keeping the native pipeline for everything you are *not* changing is the single most important habit in equipment modding. It is what keeps damage popups, sounds, saves, and unlocks working.

### Melee, skills, and mutations hook different moments

- **Melee weapons** live on the attack chain: equip, swing start, hit resolution, swing end. A per-swing effect (a projectile, a shockwave, a banked-damage release) usually attaches at swing start or hit resolution.
- **Active skills** live on the cooldown lifecycle: skill use, cooldown start, cooldown refresh. Gate your effect on the *remaining cooldown timer*, not on derived UI progress values - progress bars can disagree with the authoritative timer on edge ticks.
- **Mutations** live on combat events: kill, take damage, dodge, pick up loot. Attribute effects per victim (or per attacker), because shared global state leaks between simultaneous events.

### Data rows are data, code is code

Spawning usually needs both: a data row describing the item (identity, stats, icon, price) and code implementing its behavior. Treat these as separate layers:

- Never edit a stock row in place. Add your own row additively so removal of your mod restores vanilla exactly.
- Give every custom row a unique, namespaced ID (for example `TUT_DemoBlade`, never a bare English word that could collide with stock or another mod).
- Your code must fail closed on unknown IDs: if the row your code expects is absent, do nothing instead of crashing the factory.

### Icons live in a shared sheet - be a good guest

Item icons typically live in a shared icon sheet where each mod paints into its own assigned cell. Rules that prevent mod conflicts:

- Claim one unused cell, document which one, and never touch other cells or pixels.
- One mod owns each sheet file it ships; if two mods ship the same sheet, they conflict.
- Test the icon at the real in-game size (icons that look fine at 128px can be mud at 24px).

## Tutorial: a custom melee that fires a projectile each swing

This is the classic first weapon: a melee swing that also emits a short-range projectile. The sketch below follows the orig-first, fail-open discipline from [Your First Mod](../getting-started/03-first-mod.md).

```csharp
using HaxeProxy.Runtime;
using ModCore.Mods;
using Serenity; // placeholder: use the repo's logging facade

namespace TutEquipment;

public class TutBladeMod(ModInfo info) : ModBase(info)
{
    private bool initialized;

    // Placeholder delegate shapes: match the real signatures from the
    // DCCM API docs for the base class you subclass. Never guess them.
    public delegate void OrigSwing(object self);
    public delegate void OrigHit(object self, object target);

    public override void Initialize()
    {
        if (initialized) return;
        initialized = true;
        // Attach to the swing and hit steps of YOUR subclass only,
        // never to the stock base class globally.
        HashlinkHooks.Instance
            .CreateHook("tut.TutBlade", "onSwing", OnSwing).Enable();
        HashlinkHooks.Instance
            .CreateHook("tut.TutBlade", "onHit", OnHit).Enable();
        Logger.Information("TutBlade: hooks attached");
    }

    private void OnSwing(OrigSwing orig, object self)
    {
        orig(self); // native swing (animation, stamina, cycle) still runs
        try
        {
            EmitProjectile(self); // your additive effect only
        }
        catch (Exception ex)
        {
            // Fail open: a broken projectile must never break the swing.
            Logger.Warning(ex, "TutBlade: projectile skipped");
        }
    }

    private void OnHit(OrigHit orig, object self, object target)
    {
        orig(self, target); // native damage pipeline still runs
        // Hit-counted effects (combo trackers, per-victim marks) go
        // here, keyed by victim identity - never by global counters.
    }

    private void EmitProjectile(object self)
    {
        // Ownership rules for anything you spawn:
        // 1. You own its lifetime: cap active count, despawn on swing
        //    end, level leave, and mod unload.
        // 2. You own its cleanup: a leaked projectile that outlives the
        //    level can crash later loads.
        // 3. Reuse the native damage call for the hit itself so numbers,
        //    crits, and protections behave like stock.
        throw new NotImplementedException("teaching sketch: wire to real API");
    }
}
```

### Why each piece is shaped this way

- **Subclass-scoped hooks** (`tut.TutBlade`, not the stock base) mean uninstalling your mod cannot change stock weapon behavior.
- **`orig` first** preserves animation timing, stamina costs, and the attack cycle your effect is tuned against.
- **Victim-keyed state** (a map from victim to effect data, cleaned on victim death and level leave) prevents kill A's effect from leaking onto kill B.
- **You-own-lifetime** discipline is what separates a demo from a shippable weapon: enumerate every exit path (swing end, victim death, level transition, save/load, mod unload) and prove each one cleans up.

## Tutorial: a cooldown-gated skill effect

Skills refresh other skills, grant windows of power, or convert resources. The pattern:

1. Hook the skill-use event; read the authoritative remaining-cooldown timer of the target skill.
2. Apply your effect only if the gate passes; otherwise do nothing (no partial application).
3. If your effect grants something per event (healing per pickup, damage per trigger), ring-buffer the grant IDs so a retried or duplicated event cannot double-apply. One grant, one ID, one consumption.

```csharp
private void OnSkillUse(OrigSkillUse orig, object self, object skill)
{
    orig(self, skill);
    try
    {
        // Pseudo-logic: read the NATIVE remaining timer, not UI progress.
        // if (NativeCooldowns.Remaining(skill) > 0) return; // gate closed
        // ApplyEffectOncePerEvent(skill);
    }
    catch (Exception ex)
    {
        Logger.Warning(ex, "TutSkill: gated effect skipped");
    }
}
```

## Common failures

- Hooking the stock base class instead of your subclass, changing every weapon in the game.
- Reading UI progress instead of the authoritative timer, causing same-tick false refusals or false grants.
- Global counters for per-victim effects, leaking state across kills.
- Spawning entities you never despawn; the crash appears three levels later and looks unrelated.
- Editing stock data rows in place, so uninstalling your mod corrupts saves that reference them.
- Shipping an icon sheet that overwrites another mod's cells.

## Safety and rollback

- Develop against a disposable copied save, never your live progression.
- Back up saves before every modded launch; hash-verify after (byte-identical restores only).
- Uninstall = delete the deployed mod folder; a clean additive design restores vanilla exactly.
- Never test equipment on a run you care about until the full loop (spawn, use, save, reload, remove) is proven.

## Further reading

- [Your First Mod](../getting-started/03-first-mod.md) - orig-first and fail-open basics.
- [Animations & VFX](../animations-vfx/index.md) - presenting your weapon's effects.
- [Game Data](../game-data/index.md) - packaging the rows your item needs.
- [Debugging & Testing](../debugging-testing/index.md) - proving the full loop.
