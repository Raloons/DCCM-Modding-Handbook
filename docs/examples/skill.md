# Skill Example: Reserve Battery Entry Point

Real project code from the Beyond the Dead research mod, presented as-is. This is the complete entry-point file of the Reserve Battery survival skill: an activation gate, a use effect, an opt-in test fixture, asset loading, and registry delegation. It is the cooldown-gate pattern from the tutorials, running in production form.

```csharp
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using Serilog;

namespace BtdReserveBattery;

public sealed class ReserveBatteryMod(ModInfo info) : ModBase(info), IOnAfterLoadingAssets, IOnAfterLoadingCDB
{
    private bool initialized;
    public override void Initialize()
    {
        if (initialized) return;
        initialized = true;
        var hooks = HashlinkHooks.Instance;
        // Activation gate (native cooldown consumed only on success).
        hooks.CreateHook("tool.hero.HeroActiveSkillsManager", "onActiveSkill", ReserveBatteryRuntime.Activation).Enable();
        // Effect: native reset of the other skill; skip the native spawn path.
        hooks.CreateHook("tool.hero.activeSkills.BeheadedActiveSkillsManager", "useSkillItem", ReserveBatteryRuntime.Use).Enable();
        if (Environment.GetEnvironmentVariable("BTD_RESERVEBATTERY_CHECKS") == "1")
            hooks.CreateHook("pr.TitleScreen", "setMiscTexts", ReserveBatteryNativeFixtures.Title).Enable();
        Logger.Information("ReserveBattery 3 hooks attached (activation-gate, use-effect, fixtures-opt-in); identity-gated, fail-open");
    }

    void IOnAfterLoadingAssets.OnAfterLoadingAssets()
    {
        FsPak.Instance.FileSystem.loadPak(Info.ModRoot!.GetFilePath("res.pak").AsHaxeString());
    }

    void IOnAfterLoadingCDB.OnAfterLoadingCDB(dc._Data_ cdb) => ReserveBatteryItemRegistry.AfterCdb(cdb);
}
```

## What to notice

**Gate and effect are separate hooks.** `onActiveSkill` decides whether the skill may fire (the gate: native cooldown is consumed only on success, so a refused activation costs the player nothing). `useSkillItem` performs the effect (resetting the other skill's cooldown through the native reset call). Splitting the decision from the action is what makes the skill safe to refuse.

**The fixture hook is env-gated.** The third hook (a title-screen tap used by native test fixtures) attaches only when the `BTD_RESERVEBATTERY_CHECKS` environment variable is `1`. In normal play that variable is unset, so the hook - and its test-only behavior - does not exist. Default-dormant test code is the discipline that keeps diagnostics from leaking into gameplay.

**Registry owns the data.** `OnAfterLoadingCDB` is a one-line delegation to `ReserveBatteryItemRegistry.AfterCdb`. The entry point wires seams; a dedicated registry module owns rows. When a data bug appears, there is exactly one place to look.

**Fail-open by construction.** The log line says it outright: identity-gated (the effect checks it is acting on the right skill, not any skill) and fail-open (any problem leaves native behavior intact).

## Further reading

- [Gameplay Modifications](../gameplay-mods/index.md) - gates, native timers, and exactly-once discipline.
- [Custom Equipment](../custom-equipment/index.md) - skill lifecycle alongside weapons.
- [Game Data](../game-data/index.md) - the registry pattern behind `AfterCdb`.
