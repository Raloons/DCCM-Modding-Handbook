# Mutation Example: Contagion Range Visual

This is my source code for the Contagion mutation.
What does it do?
- Contagion is a mutation that spreads DoT effects (bleed, poison, and burn) in a circle that is rendered and visible. It only spreads said effects if the enemy that is hit by a melee attack already has that effect on it.

```csharp
using dc;
using HaxeProxy.Runtime;
using Serilog;

namespace BtdMutations;

// Native Graphics world-space outline: no assets, collider, attack or light.
// Evidence: runtime Graphics.drawCircle f@3046 / EntityLight.update f@4412.
// At most ONE geometry object (96 segments), reused during rapid multihits.
internal static class ContagionRangeVisual
{
    private static dc.h2d.Graphics? ring;
    private static dc.pr.Level? level;
    private static long expires;
    private static int errors;
    private const int Purple = 0xA855F7;
    private const long LifetimeTicks = 21; // 0.35 seconds on the shared clock

    public delegate void OrigLevelDispose(dc.pr.Level self);
    public static void OnLevelDispose(OrigLevelDispose orig, dc.pr.Level self)
    {
        if (level?.__uid == self.__uid) Clear();
        orig(self);
    }

    public static void Show(Entity source)
    {
        try
        {
            var active = source._level;
            if (active == null) return;
            if (ring != null && level?.__uid != active.__uid) Clear();
            if (ring == null)
            {
                ring = new dc.h2d.Graphics(null);
                level = active;
                active.scroller.addChildAt(ring, Const.Class.DP_ROOM_MAIN_FX);
            }
            ring.clear();
            // Exact same center and cell scale as Contagion.CellsDelta.
            ring.x = (source.cx + source.xr) * 24.0;
            ring.y = (source.cy + source.yr) * 24.0 - source.hei * 0.5;
            double width = 1.5, alpha = 0.45;
            int color = Purple, segments = 96;
            ring.lineStyle(Ref<double>.From(ref width), Ref<int>.From(ref color), Ref<double>.From(ref alpha));
            ring.drawCircle(0, 0, M62Policy.SpreadRadiusCells * 24.0, Ref<int>.From(ref segments));
            ring.endFill();
            ring.alpha = 1.0;
            expires = MutationGameClock.Ticks + LifetimeTicks;
        }
        catch (Exception ex)
        {
            if (++errors <= 4) Log.Warning(ex, "Sol Contagion visual failed; spread already completed");
            Clear();
        }
    }

    public static void Tick(dc.en.Hero hero)
    {
        if (ring == null) return;
        try
        {
            long remaining = expires - MutationGameClock.Ticks;
            if (hero.destroyed || hero._level?.__uid != level?.__uid || remaining <= 0
                || !MutationEquipment.IsEquipped(hero.inventory, ContagionRework.PerkId))
            { Clear(); return; }
            ring.alpha = remaining / (double)LifetimeTicks;
        }
        catch { Clear(); }
    }

    private static void Clear()
    {
        var g = ring; ring = null; level = null; expires = 0;
        if (g == null) return;
        try { g.remove(); } catch { }
        try { g.clear(); } catch { }
    }
}
```

## What to notice

**One geometry object, reused.** The header comment states the budget outright: at most one ring, redrawn per show, never allocated per hit. Rapid multihits redraw the same object instead of spawning new ones - worst-case node count is one, provable by reading the code.

**World-space parenting.** The ring is added to the level scroller at the effects depth, positioned from cell coordinates times 24 pixels per cell, centered on the source with a half-height lift. It stays where the spread happened even as the hero moves away - the anchored-afterimage pattern from the animations tutorial, in production form.

**Visual matches logic exactly.** The comment on the center calculation points at the spread logic's own delta (`Contagion.CellsDelta`), and the radius reads the same `SpreadRadiusCells` policy the damage uses. A ring that disagrees with the damage it advertises is worse than no ring; sharing one constant keeps them identical by construction.

**Three independent kill conditions.** The tick releases the ring when time expires, when the hero is destroyed or changes level, or when the mutation is unequipped. Any one suffices. Visuals tied to equipment must watch the equipment, not assume it is still there.

**Failure degrades to nothing.** The catch path logs at most four warnings, then clears. A broken ring never breaks the spread it illustrates ("spread already completed") - cosmetics fail open while mechanics already finished.

## File 2: ContagionRework - trigger, spread, and apply

```csharp
using dc;
using dc.en;
using dc.tool.atk;
using ModCore.Utilities;
using Serilog;

namespace BtdMutations
{
    public static class ContagionRework
    {
        public const string PerkId = CustomMutationIds.Contagion;

        public const string Description =
            "Melee hits on enemies affected by bleeding, poison, or burning spread those effects to nearby enemies.";

        // Duration text: one or two decimals, no trailing zeros
        // ("1", "1.84", "2.89", "5"). Says "effects", never "stacks":
        // single-entry native merge means exact stack counts are not copied.
        // Unit grammar (M65.7.1): singular "second" only when the formatted
        // value is exactly 1, otherwise plural "seconds".
        public static string DurationText(double seconds) => seconds.ToString("0.##");

        public static string DynamicDescription(int tactics)
        {
            double d = M62Policy.DurationFor(tactics < 1 ? 1 : tactics);
            return Description + " Copied effects last " + DurationText(d) + " " +
                DurationUnit(d) + ". Repeating refreshes duration.";
        }

        public static string DurationUnit(double seconds) =>
            DurationText(seconds) == "1" ? "second" : "seconds";

        public delegate void OrigOwnAttackDealt(Hero self, AttackData atk, Entity target);

        private static int triggers;
        private static int spreads;
        private static int noDotSkips;
        private static int failures;
        private static int refreshLogs;
        private const int VerboseLimit = 40;
        private const int QuietLimit = 12;

        // Subscriber: called from MutationDispatch.AttackDealt (no new hook).
        // orig already ran (pass-through delegate); this only reads + spreads.
        public static void OnOwnAttackDealt(OrigOwnAttackDealt orig, Hero self, AttackData atk, Entity target)
        {
            try { orig(self, atk, target); }
            catch (Exception ex)
            {
                Log.Warning(ex, "BtdMutations Contagion: onOwnAttackDealt orig failed");
                return;
            }

            try
            {
                if (self == null || target == null) return;
                if (target is Hero) return;
                if (target is not Mob victim) return;
                bool equipped;
                try { equipped = MutationEquipment.IsEquipped(self.inventory, PerkId); }
                catch { return; }
                if (!equipped) return;
                if (!Qualifies(atk, target)) return;

                // Read eligible source statuses (dps copied unscaled).
                var present = new System.Collections.Generic.List<(int id, double dps)>();
                foreach (int id in M62Policy.EligibleAffectIds)
                {
                    bool has = false;
                    try { has = victim.hasAffect(id); } catch { continue; }
                    if (!has) continue;
                    double dps = 0.0;
                    try { dps = victim.getHighestAffectValue(id); } catch { dps = 0.0; }
                    if (!(dps > 0.0)) continue;
                    present.Add((id, dps));
                }
                if (present.Count == 0)
                {
                    if (System.Threading.Interlocked.Increment(ref noDotSkips) <= QuietLimit)
                        Log.Information("BtdMutations Contagion: direct hit, no eligible DoT on victim mobUid={Mob}",
                            SafeUid(victim));
                    return;
                }

                int tactics = 1;
                try { tactics = self.tacticTier; } catch { tactics = 1; }
                double durationS = M62Policy.DurationFor(tactics);
                int t = System.Threading.Interlocked.Increment(ref triggers);
                if (t <= VerboseLimit)
                    Log.Information("BtdMutations Contagion: trigger ({N} types) victim mobUid={Mob} tactics={T} dur={Dur:F2}s",
                        present.Count, SafeUid(victim), tactics, durationS);

                Spread(self, victim, present, durationS);
                ContagionRangeVisual.Show(victim);
            }
            catch (Exception ex)
            {
                if (System.Threading.Interlocked.Increment(ref failures) <= VerboseLimit)
                    Log.Warning(ex, "BtdMutations Contagion: spread failed");
            }
        }

        // Direct-hit gate (M65.2: melee ONLY - the proven M56 gate via
        // IsDirectMelee). Ranged/skill branches removed per approved trigger
        // rules; melee-weapon projectiles stay excluded (carrier + no tag17
        // unless verified direct). DoT ticks never arrive here (path
        // exclusion, proven single caller f@18901 HeroAttackSource.onHit).
        internal static bool Qualifies(AttackData? atk, Entity? target)
            => VengeanceRework.IsDirectMelee(atk, target);

        private static void Spread(Hero hero, Mob victim, System.Collections.Generic.List<(int id, double dps)> present, double durationS)
        {
            int applied = 0, immune = 0, outOfRange = 0, dead = 0;
            try
            {
                dynamic? team = null;
                try { team = ((dynamic)hero)._team; } catch (Exception ex)
                {
                    if (System.Threading.Interlocked.Increment(ref failures) <= VerboseLimit)
                        Log.Warning(ex, "BtdMutations Contagion: team unreadable");
                    return;
                }
                if (team == null) return;
                dynamic? iter = null;
                try { iter = team.opponentMobsIterator.reset(team); }
                catch (Exception ex)
                {
                    if (System.Threading.Interlocked.Increment(ref failures) <= VerboseLimit)
                        Log.Warning(ex, "BtdMutations Contagion: iterator failed");
                    return;
                }
                int victimUid = SafeUid(victim);
                // Bounded iteration (M63.1 audit): a poisoned iterator (e.g.
                // concurrent team mutation via onAffectChange) must never spin
                // the game thread - cap total steps and consecutive errors.
                const int MaxSteps = 128;
                const int MaxErrors = 8;
                int steps = 0, errors = 0;
                while (true)
                {
                    if (++steps > MaxSteps) break;
                    bool hasNext = false;
                    try { hasNext = iter.hasNext(); }
                    catch { if (++errors >= MaxErrors) break; else continue; }
                    if (!hasNext) break;
                    Entity? e = null;
                    try { e = iter.next() as Entity; }
                    catch { if (++errors >= MaxErrors) break; else continue; }
                    errors = 0;
                    if (e == null || e is Hero) continue;
                    if (e is not Mob mob) continue;
                    int uid = SafeUid(mob);
                    if (uid == victimUid) continue; // never self-refresh the source
                    int life = 0;
                    bool destroyed = true;
                    try { life = mob.life; destroyed = mob.destroyed; } catch { continue; }
                    if (destroyed || life <= 0) { dead++; continue; }
                    double dx, dy;
                    try { CellsDelta(victim, mob, out dx, out dy); }
                    catch { continue; }
                    if (!M62Policy.WithinRadius(dx, dy)) { outOfRange++; continue; }
                    foreach (var (id, dps) in present)
                    {
                        bool ok = ApplySingle(mob, id, dps, durationS);
                        if (ok) applied++; else immune++;
                    }
                }
            }
            catch (Exception ex)
            {
                if (System.Threading.Interlocked.Increment(ref failures) <= VerboseLimit)
                    Log.Warning(ex, "BtdMutations Contagion: recipient loop failed");
                return;
            }

            int s = System.Threading.Interlocked.Increment(ref spreads);
            if (s <= VerboseLimit)
                Log.Information("BtdMutations Contagion: spread applied={Applied} immuneOrFailed={Immune} outOfRange={Range} dead={Dead}",
                    applied, immune, outOfRange, dead);
        }

        // Single application; native merge keeps at most one entry per type
        // (f@888 KeepHighestProduct: upgrade-or-noop, never append) and native
        // resists/immunity filter. Remaining-before vs applied-duration is
        // logged on refresh so live sessions prove refresh-only behavior.
        // Value is passed via Ref.From (designed .NET-ref wrapper - a
        // hand-filled struct field is NOT a proven marshaling path).
        // set-throw, read-back, and resist are logged separately so live
        // sessions distinguish marshaling failure from genuine immunity.
        private static bool ApplySingle(Mob mob, int affectId, double dps, double durationS)
        {
            double resist = double.NaN;
            double remainingBefore = double.NaN;
            try { resist = mob.getAffectResist(affectId); } catch { }
            try { remainingBefore = mob.getHighestAffectDurationS(affectId); } catch { }
            try
            {
                double v = dps;
                var valRef = HaxeProxy.Runtime.Ref<double>.From(ref v);
                try { mob.setAffectS(affectId, durationS, valRef, null); }
                catch (Exception ex)
                {
                    if (System.Threading.Interlocked.Increment(ref failures) <= VerboseLimit)
                        Log.Warning(ex, "BtdMutations Contagion: setAffectS threw id={Id}", affectId);
                    return false;
                }
                bool present = false;
                try { present = mob.hasAffect(affectId); } catch { return false; }
                if (!present)
                {
                    if (System.Threading.Interlocked.Increment(ref failures) <= VerboseLimit)
                        Log.Information("BtdMutations Contagion: apply read-back false id={Id} dps={Dps} resist={Resist}",
                            affectId, dps, resist);
                    return false;
                }
                if (!double.IsNaN(remainingBefore) && remainingBefore > 0.0
                    && System.Threading.Interlocked.Increment(ref refreshLogs) <= VerboseLimit)
                    Log.Information("BtdMutations Contagion: refreshed id={Id} remaining {Rem:F2}s -> {Dur:F2}s",
                        affectId, remainingBefore, durationS);
                return true;
            }
            catch (Exception ex)
            {
                if (System.Threading.Interlocked.Increment(ref failures) <= VerboseLimit)
                    Log.Warning(ex, "BtdMutations Contagion: apply ref failed id={Id}", affectId);
                return false;
            }
        }

        // Cell-space delta mirroring the vanilla proximity pattern (f@21553):
        // (cx+xr) centers, hei*0.5 vertical adjust, hei in px (/24).
        internal static void CellsDelta(Entity a, Entity b, out double dx, out double dy)
        {
            double ax = a.cx + a.xr;
            double ay = a.cy + a.yr;
            double bx = b.cx + b.xr;
            double by = b.cy + b.yr;
            dx = ax - bx;
            dy = (ay - by) - ((a.hei - b.hei) * 0.5 / 24.0);
        }

        private static int SafeUid(Entity e)
        {
            try { return e.__uid; } catch { return -1; }
        }
    }
}
```

Note: the file above keeps the project's own evidence annotations (`f@...`, milestone tags) - those point at the author's analysis notes, not game code. Two comment sentences were reordered during page assembly (the duration-text note now sits with the other description helpers); logic is untouched.

### What to notice in File 2

- **Subscriber, not a new hook.** `OnOwnAttackDealt` is called from the existing dispatch with `orig` already run - it only reads and spreads. Fewer hooks means fewer ordering hazards.
- **The gate is one shared predicate.** `Qualifies` delegates to the Vengeance melee check, so both mutations agree on what "direct melee" means. DoT ticks can never arrive here by path exclusion, and the comment proves it rather than asserting it.
- **Bounded iteration with an error budget.** 128 steps max, 8 consecutive errors max - a poisoned iterator (concurrent team mutation mid-loop) breaks the loop instead of hanging the game thread. The caps are named constants, not magic numbers.
- **Set, then read back.** Every application verifies via `hasAffect`, distinguishing marshaling failure from genuine immunity in the logs. Resist and remaining-before values are captured so refresh behavior is provable from live logs.
- **The visual shares the math.** `Spread` calls `ContagionRangeVisual.Show(victim)` right after spreading, and both use `M62Policy` distances - the ring on screen and the damage in the game cannot disagree.

## File 3: M62Policy - the single source of truth

```csharp
// M62 pure policy (no game-proxy dependency) - single source of truth for
// Contagion numbers. The rework class delegates here so the standalone
// production-policy tests verify without GameProxy.
namespace BtdMutations
{
    public static class M62Policy
    {
        public const double SpreadRadiusCells = 6.0;

        // Eligible DoT affect IDs (proven: 88=burn, 89=bleed, 90=poison -
        // KeepHighestProduct single-entry merge family in f@888).
        // Oil (94) is a non-damaging primer: excluded. No other compatible
        // damaging statuses exist in evidence: excluded.
        public static readonly int[] EligibleAffectIds = new[] { 88, 89, 90 };

        public static bool IsEligibleAffect(int affectId)
        {
            for (int i = 0; i < EligibleAffectIds.Length; i++)
                if (EligibleAffectIds[i] == affectId) return true;
            return false;
        }

        // min(5.0, 1.0 + 4.0 * (max(1, Tactics) - 1) / 19.0).
        // 1->1.00s, 5->1.84s, 10->2.89s, 15->3.95s, 20+->5.00s.
        public static double DurationFor(int tactics)
        {
            if (tactics < 1) tactics = 1;
            double duration = 1.0 + 4.0 * (tactics - 1) / 19.0;
            return duration > 5.0 ? 5.0 : duration;
        }

        public static bool WithinRadius(double dx, double dy)
            => dx * dx + dy * dy <= SpreadRadiusCells * SpreadRadiusCells;
    }
}
```

### What to notice in File 3

- **One file owns every number.** Radius, eligible IDs, duration curve - the spread logic (File 2) and the visual ring (File 1) both read from here. A balance change is one edit, verified once, reflected everywhere.
- **Excluded IDs are documented exclusions.** Oil (94) is named and rejected with a reason, not silently absent. Future readers know it was considered.
- **The duration curve shows its work.** Formula, sample values, and caps are all in the comment, so the design intent survives without its author.

## Further reading

- [Animations & Visual Effects](../animations-vfx/index.md) - budgets, anchors, and cleanup matrices.
- [Gameplay Modifications](../gameplay-mods/index.md) - the spread-logic side of this mutation.
- [Affix example](affix.md) - Pure Nail policy, the numbers counterpart.
