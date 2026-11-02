# Melee Weapon Example: Waveblade creation

This is my source code for the Waveblade demo weapon.
What does it do?
- Waveblade doesn't do anything fancy. It shoots a projectile on every melee attack to test weapon capabilities and rendering. It reuses the Assassin's Dagger crit condition and animation. However, loading the weapon does confirm that you can add custom items.

The code below needs zero custom art: the slash reuses the native SonicBolt look, and nothing in either file loads an image. The only art-adjacent piece is the data row's icon reference, and the simplest path is pointing it at the stock base weapon's icon - the same reuse principle the code already follows by subclassing BackStabber. That makes the whole example runnable with no art pipeline at all; a custom icon remains an optional follow-up (see [Custom Equipment](../custom-equipment/index.md)).

```csharp
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;

namespace Waveblade;

public class WavebladeMod(ModInfo info) : ModBase(info),
    IOnAfterLoadingAssets, IOnAfterLoadingCDB
{
    public const string PrototypeId = "Waveblade";
    private bool initialized;

    public override void Initialize()
    {
        if (initialized) return;
        initialized = true;
        // CreateHook requires explicit Enable (proven SquireBlade/DevConsole pattern).
        // Name lookup avoids variant-specific indices.
        var hooks = HashlinkHooks.Instance;
        hooks.CreateHook("tool.$Weapon", "create", WavebladeCombat.Create).Enable();
        hooks.CreateHook("tool.weap.BackStabber", "onExecute", WavebladeCombat.Execute).Enable();
        Logger.Information("Waveblade M38: factory and swing hooks attached; no hit hook");
    }

    void IOnAfterLoadingAssets.OnAfterLoadingAssets()
    {
        FsPak.Instance.FileSystem.loadPak(Info.ModRoot!.GetFilePath("res.pak").AsHaxeString());
    }

    void IOnAfterLoadingCDB.OnAfterLoadingCDB(dc._Data_ cdb)
    {
        dynamic row = ((dynamic)cdb).item.byId.get(PrototypeId.AsHaxeString());
        Logger.Information("WavebladeProbe row Waveblade: {State}",
            row == null ? "absent" : "PRESENT group=" + row.group.ToString());
    }
}
```

## What to notice

**Two hooks, two moments.** The `create` hook on the weapon factory runs when the game builds the item (this is where a subclass gets substituted in). The `onExecute` hook on the stock melee base runs each swing (this is where the custom behavior chains off the native swing). Factory plus swing is the whole skeleton of a custom melee - everything else is detail inside `WavebladeCombat`.

**The subclass pattern in one line.** Hooking `tool.weap.BackStabber` means the custom weapon reuses a fast stock melee as its base and overrides only the swing step. The native pipeline (animation, stamina, damage numbers, sounds) keeps running underneath.

**Assets load at the asset seam.** `OnAfterLoadingAssets` mounts the mod's own `res.pak` - art and data the weapon needs, packaged from the mod's own sources. One call, one file, no stock files touched.

**The row probe.** `OnAfterLoadingCDB` looks up the weapon's own data row by ID and logs whether it is present and which group it landed in. This is the catalog-presence check from the [Game Data](../game-data/index.md) tutorial, running live: if the row is absent or mis-grouped, the log says so on every boot instead of failing mysteriously in combat.

**The initialized guard.** Same one-line pattern as the handbook's first mod: setup runs exactly once even if `Initialize` is called twice.

## File 2: WavebladeCombat - dispatch, emission, and scaling

```csharp
using dc.en;
using dc.en.bu;
using dc.hl.types;
using dc.tool;
using dc.tool.atk;
using dc.tool.weap;
using Serilog;

namespace Waveblade;

// Native BackStabber retains melee, critical-hit, area, animation and affixes.
public static class WavebladeCombat
{
    public delegate Weapon OriginalCreate(Hero hero, InventItem item);
    public delegate bool OriginalExecute(BackStabber weapon);
    private static readonly HashSet<nint> executing = new();
    private static long swingSequence;

    public static Weapon Create(OriginalCreate original, Hero hero, InventItem item)
    {
        if (!IsWavebladeItem(item))
            return original(hero, item);
        Log.Information("Waveblade M36: native BackStabber factory dispatch");
        return new BackStabber(hero, item);
    }

    // Fail-open identity check: anything unexpected (null item/data/id) is not
    // Waveblade, so vanilla and SquireBlade swings can never be broken by this hook.
    private static bool IsWavebladeItem(InventItem item)
    {
        try
        {
            return item?._itemData?.id?.ToString() == WavebladeMod.PrototypeId;
        }
        catch
        {
            return false;
        }
    }

    public static bool Execute(OriginalExecute original, BackStabber weapon)
    {
        if (weapon == null || !IsWavebladeItem(weapon.item))
            return original(weapon!);

        // One event per released strike, before _executeImpl advances the combo.
        // False means a melee miss, so it must not suppress emission.
        // Nested re-entry for this native weapon cannot emit another wave.
        var key = weapon.HashlinkPointer;
        bool outermost = executing.Add(key);
        try
        {
            bool hit = original(weapon);
            if (outermost && !weapon.destroyed && weapon.owner != null &&
                !weapon.owner.destroyed && weapon.owner.life > 0 && weapon.owner._level != null)
            {
                long sequence = ++swingSequence;
                try
                {
                    EmitSlash(weapon);
                    Log.Information("Waveblade M36: swing {Sequence} emitted 1 slash; dir={Dir}; meleeHit={Hit}",
                        sequence, weapon.owner.dir, hit);
                }
                catch (Exception ex)
                {
                    // No retry: a partially constructed actor could be registered.
                    // Preserve the melee result and make failed emission visible.
                    Log.Error(ex, "Waveblade M36: slash emission failed for swing {Sequence}", sequence);
                }
            }
            return hit;
        }
        finally
        {
            if (outermost) executing.Remove(key);
        }
    }

    public static void EmitSlash(BackStabber weapon)
    {
        // Snapshot strike damage before combo advancement. Native Bullet owns
        // faction, collision, affixes, hit dispatch and cleanup.
        // Vanilla template: SonicCrossbow.shoot (f@35822): SonicBolt at spd 1.5,
        // init, radius 19.2, shootFromWeapon, maxDist override. BulletBlade
        // (f@35272) confirms the new-Bullet + init + shootFromWeapon shape.
        // Entity.init is load-bearing: it creates attack source/target and inserts
        // the projectile into the level quad tree. Without it the slash exists but
        // never collides (pass-through). shootFromWeapon re-origins at the muzzle,
        // propagates Fire/Poison affixes and updates direction.
        var attack = AttackUtils.Class.createFromHeroWeapon(weapon, null);
        ScaleProjectileDamage(attack);
        double angle = weapon.owner.dir < 0 ? Math.PI : 0.0;
        var slash = new SonicBolt(weapon.owner, attack, angle, 1.5, 0x00FFFF, true);
        try
        {
            slash.init();
            slash.radius = 19.2; // SonicCrossbow.shoot value; default SonicBolt radius is 6.0.
            // Melee carries no ammo: pass useAmmo=false explicitly (vanilla null
            // defaults to true; shootFromWeapon itself never consumes, it only sets
            // itemAmmoRef when the weapon has ammo).
            bool useAmmo = false;
            double xOff = 0.0, yOff = 0.0;
            slash.shootFromWeapon(weapon, ref useAmmo, ref xOff, ref yOff);
            MoveOriginToBladeTip(weapon, slash);
            slash.maxDist = 800.0; // Pixels, not cx tiles. Provisional intended M31 cap.
            slash.pierceCount = 0; // Native destroy-on-first-target-hit.
        }
        catch
        {
            // init() registers the projectile with the level; a later failure
            // must not orphan it. Best effort only: never mask the emission
            // error and never destroy twice.
            try { if (!slash.destroyed) slash.destroy(); }
            catch (Exception destroyEx) { Log.Error(destroyEx, "Waveblade M36: slash cleanup failed"); }
            throw;
        }
        // Keep the established native cyan SonicBolt visual. Hiding its sprite
        // also hides children (shipped h2d.Object.drawRec f1395 ops 0-2), so the
        // obsolete child-bitmap probe cannot supply a visible replacement.
    }

    // Director-approved balance: the slash carries a quarter of the sword
    // strike's nominal base. Applied once, on our private snapshot, at the
    // baseDmg stage (vanilla overrideBaseDamage, f@1807: plain field set, the
    // same slot BulletBlade replaces with power2/n). Everything downstream -
    // tier scaling, affix bonuses, crit multiplication, negation - reads that
    // field, so it scales proportionally and the melee snapshot is untouched.
    // Fail-safe: any unexpected shape leaves the snapshot unscaled (melee
    // equivalent) and logs, rather than risking a zero-damage slash.
    private static void ScaleProjectileDamage(AttackData attack)
    {
        try
        {
            dynamic raw = attack.baseDmg;
            double? nominal = ReadNominalBase(raw);
            if (nominal.HasValue)
            {
                attack.overrideBaseDamage(nominal.Value * 0.25);
                return;
            }
            Log.Warning("Waveblade M36: unexpected baseDmg shape; slash left unscaled");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Waveblade M36: damage scaling skipped; slash left unscaled");
        }
    }

    // The strike power arrives either as a number or as a single-element power
    // array (CDB `"power": [30]`; the array branch keeps the vanilla damage
    // jitter in updateDamages f@36014). A plain-number replacement follows the
    // BulletBlade precedent; the slash then deals exactly a quarter of nominal
    // while the melee keeps its roll.
    private static double? ReadNominalBase(dynamic raw)
    {
        if (raw is double d) return d;
        if (raw is float f) return f;
        if (raw is int n) return n;
        if (raw is long l) return l;
        if (raw is ArrayDyn dyn && dyn.get_length() == 1)
            return ReadNominalBase(dyn.getDyn(0));
        return null;
    }

    // The muzzle sits ~25px ahead of hero center (get_shootX adds dir*10,
    // Bullet ctor adds 15px along travel), but the melee arc's outer edge is
    // at area.x + widPx/2 (Weapon ctor f@4904: setRelativePos(widPx/2 + off),
    // canHit mirrors by dir) - 67.2px for our 2.8-case arc. Nudge the slash
    // to just inside that edge so it reads as leaving the blade, while staying
    // inside already-melee-validated space. Live per-cycle derivation with a
    // constant fallback; dir mirroring matches canHit (area.x * dir).
    private static void MoveOriginToBladeTip(BackStabber weapon, SonicBolt slash)
    {
        const double fallbackEdge = 67.2;
        const double muzzle = 25.0;
        const double margin = 7.0;
        double edge = fallbackEdge;
        try
        {
            var area = weapon.areas?.getDyn(weapon.get_cycle()) as Area;
            if (area != null && area.widPx > 0)
                edge = area.x + area.widPx * 0.5;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Waveblade M36: arc read failed; using fallback edge");
        }
        double nudge = Math.Max(0.0, edge - margin - muzzle);
        if (nudge > 0)
            slash.offsetOrigin(weapon.owner.dir * nudge, 0.0);
    }
}
```

### What to notice in File 2

- **Fail-open identity gate.** `IsWavebladeItem` is null-safe and exception-safe: anything unexpected returns false, so vanilla and unrelated-mod swings pass through untouched. The factory hook can never break another weapon.
- **Re-entrancy guard.** The `executing` set plus `outermost` flag mean nested execution of the same native weapon cannot emit a second wave. One swing, one slash, by construction.
- **Emission never suppresses a miss.** The slash fires whether or not the melee hit - the comment states the rule outright, because a miss-gated projectile would feel broken.
- **Native Bullet ownership.** Faction, collision, affixes, hit dispatch, and cleanup all stay native. `init` is load-bearing (it inserts the projectile into the level quad tree - without it the slash passes through everything), and the catch block cleans up a half-registered projectile without masking the original error.
- **Damage scaled once, upstream of everything.** A quarter of nominal base damage is applied at the `overrideBaseDamage` stage, so tier scaling, affixes, and crits all scale proportionally downstream. Unexpected shapes fail safe to unscaled-with-a-log, never to zero damage.
- **Origin nudge with live read plus fallback.** The muzzle position derives from the live melee arc each swing and falls back to a constant if the read fails - derived when possible, safe always.

## Further reading

- [Custom Equipment](../custom-equipment/index.md) - the full weapon-building tutorial this file compresses.
- [Game Data](../game-data/index.md) - the row-presence discipline behind the probe.
- [Your First Mod](../getting-started/03-first-mod.md) - the hook basics in beginner form.
