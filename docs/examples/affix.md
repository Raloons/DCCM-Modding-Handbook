# Legendary Affix Replacement Example: Pure Nail

This is my raw code for my Pure Nail affix. 

What does it do?
- This makes Pure Nail have a unique affix instead of being a plain placeholder of spawning biters on kill. When you have missing jumps, Pure Nail will refresh one jump for the first hit you land in the air with the weapon. This resets upon touching the ground.

```csharp
namespace BtdMutations
{
    public static class PureNailPolicy
    {
        public const string ItemId = "PureNail";
        public const string ForcedAffixId = "DeathWorm";
        // Bounce confirmation: hero rises at least this many pixels within
        // the watch window after the candidate hit (screen coords: smaller
        // y is higher, so a rise is yBefore - yAfter).
        public const double BounceRisePx = 8.0;
        public const long BounceWindowTicks = 12;
        // A target counts as below the hero when its top edge is clearly
        // beneath the hero's feet (pixel units, 24 px per cell).
        public const double BelowMarginPx = 12.0;
        public const int MaxWatches = 8;
        // Native AirJump counts USES, not remaining jumps: onStart adds 1,
        // canUse requires used < capacity (1 base, 2/3 amulets, 4 gameplay mod).
        // Restore one spent use; reject unknown/negative/out-of-profile counts.
        public const int RestoredJumps = 1;
        public const int MaxNativeUsedJumps = 4;
        public const int DownwardCycle = 3; // PureNail.onBeforeCurSkillPrepare f@35686
        public const int MaxGrantsPerAirtime = 64;

        public static bool IsBelow(double heroFeetPx, double targetTopPx) =>
            targetTopPx > heroFeetPx + BelowMarginPx;

        public static bool BounceConfirmed(double yBeforePx, double yAfterPx) =>
            yBeforePx - yAfterPx >= BounceRisePx;

        public static bool ShouldGrant(int usedJumps) =>
            usedJumps >= RestoredJumps && usedJumps <= MaxNativeUsedJumps;

        public static int GrantedTotal(int usedJumps) =>
            ShouldGrant(usedJumps) ? usedJumps - RestoredJumps : usedJumps;
    }
}
```

## What to notice

**Two confirmations before one grant.** A pogo restores a jump only when the native success marker fired AND the hero measurably rose 8 pixels within 12 ticks. Either signal alone can lie (a graze, a knockback); both together mean a real bounce. The policy file states the thresholds; behavior code enforces the pairing.

**Screen-coordinate literacy in comments.** "Smaller y is higher, so a rise is yBefore minus yAfter" - the kind of one-line note that prevents an entire class of inverted-geometry bugs. Coordinate-system facts belong next to the numbers that depend on them.

**Native polarity documented, not assumed.** The AirJump counter counts *uses*, and the comment records the exact capacity ladder (1 base, 2/3 amulets, 4 with gameplay mods) plus the reject rule for out-of-profile counts. When the native system surprised the team mid-project, the correction landed here as a documented constant, not a magic number in behavior code.

**Predicates, not just constants.** `IsBelow`, `BounceConfirmed`, `ShouldGrant`, and `GrantedTotal` are the rules themselves, testable with plain numbers and no game running. Standalone tests feed pixel values and jump counts in, and assert grant decisions out - the full affix logic verified in milliseconds.

**Bounds on everything.** `MaxWatches` caps concurrent bounce watches; `MaxGrantsPerAirtime` caps grants. Even a correct rule needs a ceiling, because events can arrive faster than anyone expects.

## File 2: PureNailRework - watch, confirm, grant

```csharp
using dc;
using dc.en;
using dc.tool.atk;
using Serilog;

namespace BtdMutations
{
    public static class PureNailRework
    {
        private sealed class Watch
        {
            internal int EnemyUid;
            internal long AirtimeId;
            internal int StartY;
            internal long DeadlineTick;
            internal dc.tool.weap.PureNail Nail = null!;
            internal int StartStreak;
        }

        private static readonly System.Collections.Generic.Dictionary<(int Hero, int Enemy), Watch> watches = new();
        private static readonly System.Collections.Generic.HashSet<(int Hero, int Enemy, long Airtime)> granted = new();
        private static int grants;
        private static int confirms;
        private static int skips;
        private static int rejects;
        private const int VerboseLimit = 40;

        internal static void DiagnoseReject(AttackData atk, Entity target, string? detail = null)
        {
            if (atk == null || target == null) return;
            try
            {
                if (target is not Mob || atk.sourceWeapon == null || !atk.hasTag(17)
                    || atk.hasTag(12) || atk.hasTag(13)) return;
                string stage = LegendaryMeleeGate.Diagnose(atk, target, PureNailPolicy.ItemId,
                    PureNailPolicy.ForcedAffixId, false);
                // Other weapons must not exhaust the Nail diagnostic budget.
                if (stage.StartsWith("id-mismatch:", StringComparison.Ordinal)) return;
                if (stage == "ok") stage = detail ?? "native-pogo-watch";
                if (System.Threading.Interlocked.Increment(ref rejects) <= VerboseLimit)
                    Log.Information("BtdMutations Pogo: reject stage={Stage}", stage);
                }
            catch { }
        }

        private static void DiagnoseSkip(int heroUid, int enemyUid, string why)
        {
            if (System.Threading.Interlocked.Increment(ref skips) <= VerboseLimit)
                Log.Information("BtdMutations Pogo: grant skipped ({Why}) (heroUid={Hero} enemy={Enemy})",
                    why, heroUid, enemyUid);
        }

        public const string Description =
            "Pogo Master: downward pogo hits restore one midair jump, once per enemy per airtime. Legendary Pure Nail only.";

        private static void Prune(int heroUid, long airtimeId, bool dead = false)
        {
            foreach (var key in new System.Collections.Generic.List<(int Hero, int Enemy)>(watches.Keys))
                if (key.Hero == heroUid && (dead || watches[key].AirtimeId != airtimeId))
                    watches.Remove(key);
            granted.RemoveWhere(key => key.Hero == heroUid && (dead || key.Airtime != airtimeId));
        }

        public static void RecordHit(Hero self, AttackData atk, Entity target)
        {
            try
            {
                if (self == null || self.__uid < 0) return;
                int ownerUid = self.__uid;
                if (!LegendaryMeleeGate.Matches(atk, target, PureNailPolicy.ItemId,
                    PureNailPolicy.ForcedAffixId, out int targetUid))
                {
                    DiagnoseReject(atk, target);
                    return;
                }
                // Native direction, not an enemy-top approximation. Tall enemies
                // can overlap hero feet during genuine down attacks.
                if (atk.sourceWeapon is not dc.tool.weap.PureNail nail ||
                    nail.get_cycle() != PureNailPolicy.DownwardCycle)
                {
                    DiagnoseReject(atk, target, "not-native-down-attack");
                    return;
                }
                long now = MutationGameClock.Ticks;
                long airtimeId = AerialMomentumRework.LastGroundTickValue;
                Prune(ownerUid, airtimeId);
                if (airtimeId < 0 || now - airtimeId <= 2)
                {
                    DiagnoseReject(atk, target, "grounded-or-unknown-airtime");
                    return;
                }
                var key = (ownerUid, targetUid);
                if (granted.Contains((ownerUid, targetUid, airtimeId)) || watches.ContainsKey(key)) return;
                // Fail closed at either bound; never evict current-airtime grants
                // (eviction would allow the same enemy to grant twice).
                if (watches.Count >= PureNailPolicy.MaxWatches ||
                    granted.Count >= PureNailPolicy.MaxGrantsPerAirtime) return;
                watches[key] = new Watch
                {
                    EnemyUid = targetUid,
                    AirtimeId = airtimeId,
                    StartY = (self.cy + self.yr) * 24.0,
                    DeadlineTick = now + PureNailPolicy.BounceWindowTicks,
                    Nail = nail,
                    StartStreak = nail.jumpStreakCount,
                };
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BtdMutations Pogo: record failed");
            }
        }

        internal static void OnHeroTick(Hero hero, int uid)
        {
            try
            {
                if (hero == null || uid < 0) return;
                long airtimeId = AerialMomentumRework.LastGroundTickValue;
                Prune(uid, airtimeId, hero.life <= 0);
                if (hero.life <= 0) return;
                long now = MutationGameClock.Ticks;
                double y = (hero.cy + hero.yr) * 24.0;
                foreach (var mineKey in new System.Collections.Generic.List<(int Hero, int Enemy)>(watches.Keys))
                {
                    if (mineKey.Hero != uid || !watches.TryGetValue(mineKey, out Watch? mine)) continue;
                    if (now > mine.DeadlineTick || airtimeId < 0 || now - airtimeId <= 2)
                    {
                        watches.Remove(mineKey);
                        continue;
                    }
                    // Callback runs inside native hit(), BEFORE the down-hit
                    // streak increment and onExecute bounce. Wait for both.
                    if (mine.Nail.jumpStreakCount <= mine.StartStreak ||
                        !PureNailPolicy.BounceConfirmed(mine.StartY, y)) continue;
                    watches.Remove(mineKey);
                    if (System.Threading.Interlocked.Increment(ref confirms) <= VerboseLimit)
                        Log.Information("BtdMutations Pogo: bounce confirmed (heroUid={Hero} enemy={Enemy})", uid, mine.EnemyUid);
                    var grantKey = (uid, mine.EnemyUid, mine.AirtimeId);
                    if (granted.Contains(grantKey) || granted.Count >= PureNailPolicy.MaxGrantsPerAirtime) continue;
                    try
                    {
                        if (hero.mainSkillsManager.getMainSkill(dc.tool.mainSkills.AirJump.Class)
                            is not dc.tool.mainSkills.AirJump air)
                        {
                            DiagnoseSkip(uid, mine.EnemyUid, "no-airjump-skill");
                            continue;
                        }
                        // isEnabled checks required/forbidden inventory items,
                        // not count, controls, hold locks, or instant usability.
                        // canUse can legitimately be false during Nail recovery;
                        // never bypass its other gates or change any input lock.
                        if (!air.isEnabled())
                        {
                            DiagnoseSkip(uid, mine.EnemyUid, "skill-disabled");
                            continue;
                        }
                        int used = air.get_airJumps(); // read failure -> no write
                        if (!PureNailPolicy.ShouldGrant(used))
                        {
                            DiagnoseSkip(uid, mine.EnemyUid, $"no-spent-use-or-unsupported used={used}");
                            continue;
                        }
                        int restored = PureNailPolicy.GrantedTotal(used);
                        // Count the attempt before writing: an uncertain write
                        // cannot be retried against this enemy in this airtime.
                        granted.Add(grantKey);
                        air.set_airJumps(restored);
                        int readBack = air.get_airJumps();
                        if (readBack != restored)
                        {
                            DiagnoseSkip(uid, mine.EnemyUid,
                                $"write-not-persisted used={used} expected={restored} actual={readBack}");
                            continue;
                        }
                        if (System.Threading.Interlocked.Increment(ref grants) <= VerboseLimit)
                            Log.Information("BtdMutations Pogo: jump restored (heroUid={Hero} enemy={Enemy} used={Used}->{Restored})",
                                uid, mine.EnemyUid, used, readBack);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "BtdMutations Pogo: grant failed");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BtdMutations Pogo: tick failed");
            }
        }
    }
}
```

Note: the file above keeps the project's own evidence-path comment and native-function annotations (`f@...`) - those are the author's research notes pointing at their own analysis, not game code.

### What to notice in File 2

- **Two-signal confirmation.** `RecordHit` opens a watch only for the right weapon, the native down-attack cycle, and a genuine airtime. `OnHeroTick` grants only after the native streak increments AND the 8-pixel rise confirms. One signal arms, the other fires.
- **Read, compute, count, write, verify.** The grant reads used jumps, computes the restored value, records the attempt (consuming the one-per-enemy-per-airtime allowance), writes through the existing setter, then reads back to prove persistence. An uncertain write is never retried - the allowance is already spent.
- **Gates are respected, never bypassed.** A disabled AirJump skill is a skip reason, not an override. Input locks are never touched. Movement and input writes are explicitly out of scope in the header comment.
- **Budgeted observability.** Every diagnostic path (rejects, skips, confirms, grants) counts through `Interlocked` and stops logging past 40 lines. Live sessions stay readable and the counts remain queryable.
- **Companion modules, each with one job.** `LegendaryMeleeGate` answers identity (right weapon, right affix). `MutationGameClock` answers time (one native clock). `AerialMomentumRework` answers airtime (last ground tick). File 2 orchestrates; it duplicates none of them.

## Further reading

- [Gameplay Modifications](../gameplay-mods/index.md) - exactly-once grants and victim-keyed tracking around policies like this.
- [Custom Equipment](../custom-equipment/index.md) - the weapons these affixes attach to.
- [Mutation example](mutation.md) - Contagion visuals, the rendering counterpart.
