# Mutation Example: Contagion Range Visual

Real project code from the Beyond the Dead research mod, presented as-is with one punctuation cleanup. This is the complete rendering file behind a reworked Contagion mutation: a world-space ring showing the spread radius, drawn with native graphics calls, faded over a fixed lifetime, and released on level change. The spread logic itself lives in a companion file - this page is the rendering half, where custom shapes meet engine lifetime rules.

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

## Further reading

- [Animations & Visual Effects](../animations-vfx/index.md) - budgets, anchors, and cleanup matrices.
- [Gameplay Modifications](../gameplay-mods/index.md) - the spread-logic side of this mutation.
- [Affix example](affix.md) - Pure Nail policy, the numbers counterpart.
