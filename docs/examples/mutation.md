# Mutation Example: Twin Daggers Policy

Real project code from the Beyond the Dead research mod, presented as-is with one punctuation cleanup. This is the complete policy file behind a legendary-affix redesign: no game calls, no hooks, just the numbers and IDs the behavior code consumes. It is the smallest file in this section and arguably the most instructive - proof that the best place for tuning values is a plainly readable constants file, not buried logic.

```csharp
// Twin Daggers pure policy (M80.07/M80.18) - Relentless Blades numbers. No
// game-proxy dependency; standalone tests verify without GameProxy.
namespace BtdMutations
{
    public static class TwinDaggersPolicy
    {
        public const string ItemId = "DualDaggers";
        public const string ForcedAffixId = "RunSpeedOnCrit";
        // Permanent redesign (M80.18): every fresh legendary-dagger combo
        // begins at its second strike. Native _cycle is 0-based
        // (set_cycle writes _cycle directly; incrementCycle/isLastCycle
        // drive the 3-strike loop), so forcing 1 skips exactly strike one.
        // Mid-combo executions (cycle 1/2) pass through untouched.
        public const int AdvanceCycle = 1;
        public const int FreshComboCycle = 0;
    }
}
```

## What to notice

**Policy separated from machinery.** Item ID, affix ID, and cycle numbers live here; the hook and dispatch code that reads them lives elsewhere. Rebalancing means editing four constants in one file, then re-running the standalone tests - never touching behavior code. When a design argument happens ("should the combo start at strike two?"), it happens over this file.

**The comment carries the reasoning.** The block comment records *why* forcing cycle 1 is correct (0-based native cycle, 3-strike loop, first strike skipped, mid-combo untouched). Six months later, nobody has to reverse-engineer the intent from the number alone. Numbers without reasoning rot; numbers with reasoning survive.

**No game dependency, so it is testable alone.** The header comment states the key property: standalone tests verify this without the game proxy. Pure policy files can be unit-tested in milliseconds with no fixture, no launch, and no save - which is why the redesign could iterate quickly while behavior code stayed frozen.

**IDs are exact strings.** `DualDaggers` and `RunSpeedOnCrit` must match the data rows character for character. A policy file concentrates every magic string in one searchable place instead of scattering them through hooks.

## Further reading

- [Gameplay Modifications](../gameplay-mods/index.md) - victim attribution and stacking rules around policies like this.
- [Custom Equipment](../custom-equipment/index.md) - mutations as event-driven equipment.
- [Game Data](../game-data/index.md) - the rows these IDs must match.
