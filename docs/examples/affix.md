# Affix Example: Pure Nail Policy

Real project code from the Beyond the Dead research mod, presented as-is with one punctuation cleanup. This is the complete policy file behind a custom pogo affix: geometry tests, bounce confirmation, and jump-restore rules as pure logic with no game calls. The behavior code that reads these values lives in a companion file - this page is about where tuning values belong.

```csharp
// Pure Nail pure policy (M80.08) - Pogo Master numbers and geometry. No
// game-proxy dependency; standalone tests verify without GameProxy.
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

## Further reading

- [Gameplay Modifications](../gameplay-mods/index.md) - exactly-once grants and victim-keyed tracking around policies like this.
- [Custom Equipment](../custom-equipment/index.md) - the weapons these affixes attach to.
- [Mutation example](mutation.md) - Contagion visuals, the rendering counterpart.
