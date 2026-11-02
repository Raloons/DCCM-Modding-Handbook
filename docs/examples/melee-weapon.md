# Melee Weapon Example: Waveblade Entry Point

Real project code from the Beyond the Dead research mod, presented as-is with one punctuation cleanup. This is the complete entry-point file of the Waveblade custom melee weapon: factory hook, swing hook, asset-pack loading, and a data-row probe. Its companions (`WavebladeCombat`, item registry, art) live in the same mod but are not shown here - this page is about how a weapon mod opens.

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

## Further reading

- [Custom Equipment](../custom-equipment/index.md) - the full weapon-building tutorial this file compresses.
- [Game Data](../game-data/index.md) - the row-presence discipline behind the probe.
- [Your First Mod](../getting-started/03-first-mod.md) - the hook basics in beginner form.
