# Your First Mod: HelloTitle (status: educational example)

## Goal

Build and deploy a minimal title-screen mod that appends text to the existing build label — without replacing game text, assets, or saves.

This is an independently written teaching example. It demonstrates the *pattern* (orig-first hook + fail-open guard) without copying any project or game code.

## Prerequisites

- [Installation](01-installation.md) and [Dev Environment](02-dev-environment.md).
- Full example source lives at `docs/examples/hello-title/`.

## Steps

1. Create the mod class:

```csharp
using HaxeProxy.Runtime;
using ModCore.Mods;
using Serilog;

namespace HelloTitle;

public class HelloTitleMod(ModInfo info) : ModBase(info)
{
    private bool initialized;
    public delegate void OrigSetMiscTexts(object self);

    public override void Initialize()
    {
        if (initialized) return;
        initialized = true;
        HashlinkHooks.Instance
            .CreateHook("pr.TitleScreen", "setMiscTexts", SetMiscTexts)
            .Enable();
        Logger.Information("HelloTitle: hook attached");
    }

    private void SetMiscTexts(OrigSetMiscTexts orig, object self)
    {
        orig(self); // always run the original first
        try
        {
            dynamic screen = self;
            if (screen.build == null) return;
            string before = screen.build.text?.ToString() ?? "";
            string display = string.IsNullOrWhiteSpace(before)
                ? "HelloTitle - Dev Build"
                : before + " | HelloTitle - Dev Build";
            screen.build.set_text(display.AsHaxeString());
        }
        catch (Exception ex)
        {
            // Fail open: log and leave the stock label alone.
            Logger.Warning(ex, "HelloTitle: label append failed");
        }
    }
}
```

2. Build: `dotnet build -c Release`.
3. Deploy the `output/HelloTitle/` folder into `<Dead Cells>/coremod/mods/`.
4. Launch via `DeadCellsModding.exe` (never the vanilla Play button for modded testing).
5. Verify on the title screen: the bottom-left label gains the `| HelloTitle - Dev Build` suffix. Check `coremod/logs/` for the `hook attached` line.
6. Uninstall by deleting the deployed folder; the vanilla label returns on next title entry.

## Why this works

- `Initialize()` runs once under the modded launcher; the hook attaches there, not in a constructor.
- The hook calls `orig(self)` first so native layout/text still runs, then appends — additive changes are the safest first experiment.
- `AsHaxeString()` marshals the .NET string into the engine's string type; raw .NET strings must not be assigned directly.
- The `try/catch` keeps a cosmetic failure from breaking the title screen (fail-open).

## Common failures

- Hook name typo (`TitleScreen` vs `pr.TitleScreen`, method name casing) — the hook silently never fires. Compare against DCCM docs and log on attach.
- Forgetting `orig(self)` — the native label/layout never runs; always forward exactly once.
- Assigning a .NET string without marshaling — use the framework's conversion helper.
- Testing on the vanilla exe — the framework never loads there.

## Safety / rollback

This mod touches no saves, no CDB rows, and no assets. Rollback is deleting one folder. Back up saves anyway before any modded launch — it builds the habit for gameplay mods where saves *do* matter.

## Further reading

- Full project: `docs/examples/hello-title/`
- Next (planned): Custom Equipment → your first weapon-subclass mod.
