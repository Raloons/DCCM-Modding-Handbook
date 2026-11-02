# Custom Equipment

Build a complete custom melee weapon mod from an empty folder to a verified in-game item. This is a long guide by design: it covers project setup, the data row, the behavior code, the icon, deployment, verification, and removal. Every example is independently written for teaching. Match delegate and API shapes to the real DCCM API docs - never guess a signature.

**You will produce:** a `TutBlade` mod (melee that emits a short projectile each swing and tracks per-victim hits), its data row, its icon cell, and a written verification record.
**You will verify:** clean build, title-screen load, spawn, swing, save, reload, removal, reload-again - each with expected evidence listed.

## Prerequisites

- [Installation](../getting-started/01-installation.md) and [Dev Environment](../getting-started/02-dev-environment.md) complete.
- [Your First Mod](../getting-started/03-first-mod.md) built and deployed at least once (you know the deploy folder and log location by heart).
- A disposable copied test save. Back up your live saves first and do not touch them again until the removal test passes.

## Part 1 - Concepts

### The factory dispatches, your subclass behaves

 mods rarely build items from scratch. The working pattern is:

1. The game's item factory decides *what* to spawn from data rows.
2. Your mod subclasses a stock item class close to what you want (a fast melee for a fast custom melee, a bow base for a custom ranged weapon, an active-skill base for a custom skill) and overrides only the steps you change.
3. Every step you do not change stays on the native path by calling `orig`. That keeps damage numbers, sounds, crits, saves, and unlocks behaving exactly like stock.

Subclass scope matters: hook YOUR subclass (`tut.TutBlade`), never the stock base class. A hook on the base changes every weapon in the game, including stock ones, and makes your mod uninstallable in practice.

### Melee, skills, and mutations hook different moments

- **Melee:** swing start (emit effects), hit resolution (count, mark, bank), swing end (cleanup, release). Per-swing state must reset every swing or it leaks into the next one.
- **Active skills:** skill use (gate check), cooldown start/refresh (timers). Gate on the authoritative remaining-cooldown timer, never on derived UI progress.
- **Mutations:** kill, damage-taken, dodge, loot-pickup events. Attribute everything per victim or per attacker; shared global slots corrupt simultaneous events.

### Data rows and code are separate layers

Spawning needs both: a row describing the item and code implementing it. Rules:

- Add rows; never edit stock rows in place. Your removal must restore vanilla exactly.
- Namespace every custom ID (`TUT_DemoBlade`, `TUT_DemoSkill`). Bare English words collide with stock or other mods.
- Code must fail closed on unknown IDs: missing row means do nothing, never throw into the factory.

### Icons are a shared sheet - claim one cell

Item icons live in a shared sheet. Claim one unused cell, record its coordinates, touch nothing else. One mod ships each sheet it modifies, and two mods shipping the same sheet conflict. Check legibility at real in-game size.

## Part 2 - Project setup

Create this exact tree (names are yours; the shape is what matters):

```
TutBlade/
  TutBlade.csproj
  TutBladeMod.cs
  data/
    tutblade-row.json        <-- your authored row source (see Part 3)
  assets/
    icons/
      tutblade-24px.png      <-- your original 24px icon (see Part 5)
  verify/
    checklist.md             <-- your written verification record
```

Minimal `.csproj` (same shape as the handbook example):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <ModName>TutBlade</ModName>
    <ModType>mod</ModType>
    <ModMain>TutBlade.TutBladeMod</ModMain>
    <Authors>Tutorial example</Authors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DeadCellsCoreModding.MDK" Version="1.0.1" />
  </ItemGroup>
</Project>
```

Build and confirm zero warnings:

```powershell
dotnet build -c Release
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. Treat any warning in hook code as a defect - it almost always means a signature mismatch that will fail silently at runtime. The build produces `bin/Release/net10.0/output/TutBlade/` containing the DLL and `modinfo.json`.

## Part 3 - Author the data row

Write the row from the documented schema shape (copy the shape of an analogous stock row from DCCM docs or templates), changing only your own values:

| Field family | Your value | Rule |
|---|---|---|
| Identity | `TUT_DemoBlade` | Unique, namespaced, never reused for anything else |
| Display | Your name and description text | Plain text; no markup the game font cannot render |
| Stats | Your damage, speed, tier values | Start from the stock base's values, change one thing |
| Economy | Price, rarity, drop weight | Conservative first: always available in test fixture |
| Presentation | Your icon cell reference | Must resolve to the cell you actually painted |
| References | Affix pools, loot tables | Only pools that exist; never retarget stock references |

Validation before launch (do this with a script, not by eye):

1. Your ID appears exactly once across your files.
2. Every reference you make resolves to something that exists.
3. No stock file was modified (diff your whole data source against baseline).
4. Unknown fields: none. If the schema has a field you do not understand, leave the stock default. An invented value in an unknown field is a time bomb.

## Part 4 - Behavior code

Full illustrative mod. Delegate shapes are placeholders - match them to the real API:

```csharp
using HaxeProxy.Runtime;
using ModCore.Mods;

namespace TutBlade;

public class TutBladeMod(ModInfo info) : ModBase(info)
{
    private bool initialized;

    // Victim-keyed hit state. Bounded, swept on level leave.
    private readonly Dictionary<string, int> hitCounts = new();
    private const int MaxTracked = 64;

    // Active projectiles you own. Capped, released on swing end,
    // level leave, and mod unload.
    private readonly List<object> liveProjectiles = new();
    private const int MaxProjectiles = 8;

    public delegate void OrigSwing(object self);
    public delegate void OrigHit(object self, object target);
    public delegate void OrigSwingEnd(object self);
    public delegate void OrigLevelLeave(object self);

    public override void Initialize()
    {
        if (initialized) return;
        initialized = true;
        var hooks = HashlinkHooks.Instance;
        hooks.CreateHook("tut.TutBlade", "onSwing", OnSwing).Enable();
        hooks.CreateHook("tut.TutBlade", "onHit", OnHit).Enable();
        hooks.CreateHook("tut.TutBlade", "onSwingEnd", OnSwingEnd).Enable();
        hooks.CreateHook("tut.TutLevel", "onLeave", OnLevelLeave).Enable();
        Logger.Information("TutBlade: hooks attached");
    }

    private void OnSwing(OrigSwing orig, object self)
    {
        ResetSwingState(); // per-swing state resets EVERY swing
        orig(self);        // native animation, stamina, cycle
        try { EmitProjectile(self); }
        catch (Exception ex)
        {
            // Fail open: broken cosmetic never breaks the swing.
            Logger.Warning(ex, "TutBlade: projectile skipped");
        }
    }

    private void OnHit(OrigHit orig, object self, object target)
    {
        orig(self, target); // native damage pipeline runs unchanged
        try
        {
            string id = VictimKey(target);
            if (hitCounts.Count >= MaxTracked) return;
            hitCounts[id] = hitCounts.TryGetValue(id, out int n) ? n + 1 : 1;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TutBlade: hit tracking skipped");
        }
    }

    private void OnSwingEnd(OrigSwingEnd orig, object self)
    {
        orig(self);
        try { ReleaseProjectiles("swing-end"); }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TutBlade: swing-end cleanup skipped");
        }
    }

    private void OnLevelLeave(OrigLevelLeave orig, object self)
    {
        try
        {
            ReleaseProjectiles("level-leave");
            hitCounts.Clear();
        }
        finally { orig(self); }
    }

    private void ResetSwingState()
    {
        // Per-swing temporaries reset here. Per-victim totals in
        // hitCounts survive across swings but never across levels.
    }

    private void EmitProjectile(object self)
    {
        if (liveProjectiles.Count >= MaxProjectiles) return; // hard cap
        // Illustrative: spawn via the documented spawn path, register
        // the handle in liveProjectiles, route its hit through the
        // native damage call so numbers, crits, and protections match
        // stock behavior.
        throw new NotImplementedException("teaching sketch: wire to real API");
    }

    private void ReleaseProjectiles(string reason)
    {
        // Illustrative: despawn every handle, then clear the list.
        // Log the count at debug level for leak hunting.
        liveProjectiles.Clear();
    }

    private string VictimKey(object victim)
    {
        // Illustrative: stable per-victim identity. Never a global counter.
        throw new NotImplementedException("teaching sketch");
    }
}
```

Design notes an AI implementer should preserve:

- `OnLevelLeave` runs cleanup *before* `orig` in a `try/finally`: even if cleanup throws, the level transition proceeds and the `finally` guarantees `orig` runs.
- `MaxProjectiles` and `MaxTracked` make worst-case memory provable by reading the code.
- `ResetSwingState` exists as a named step so future per-swing fields have exactly one place to reset - the classic leak source is a second per-swing field added later with no reset.

## Part 5 - Icon pipeline

1. Create original 24px art (your pixels only, transparent background where the sheet format requires it).
2. Paint it into your claimed unused cell and no other cell.
3. In-game check at real size and on both light backgrounds (inventory) and dark backgrounds (world drop): if it is mud at 24px, simplify shapes, not colors.
4. Record the cell coordinates in `verify/checklist.md` next to the row ID.

## Part 6 - Deploy and verify

Deploy:

```powershell
Copy-Item -Recurse bin/Release/net10.0/output/TutBlade '<Dead Cells>/coremod/mods/'
```

Launch ONLY via `DeadCellsModding.exe`. Verify in order, writing each result into `verify/checklist.md`:

| Step | Action | Expected evidence |
|---|---|---|
| 1 | Launch, reach title | `TutBlade: hooks attached` in `coremod/logs/`, no errors |
| 2 | Spawn the item via console | Item appears with your name, icon, and stats |
| 3 | Equip and swing 10 times | 10 swings, 10 projectiles max 8 live, no errors, swing feel unchanged |
| 4 | Hit 3 different enemies | 3 victim entries, counts correct, no cross-talk |
| 5 | Change levels | Projectile pool empty, hit map cleared, no errors |
| 6 | Save, exit, reload | Item persists, works, counts sane |
| 7 | Remove mod, reload same save | Graceful missing-content handling, run intact |
| 8 | Restore backup, reinstall | Full function back |

Hash-verify save backups around the session:

```powershell
Get-FileHash -Algorithm SHA256 backup/*.sav | Format-Table
```

Compare before and after: only the expected run-progress bytes may differ; anything else means your test leaked outside its fixture.

## Part 7 - Troubleshooting matrix

| Symptom | Likely cause | Fix |
|---|---|---|
| Hook never fires, no log line | Wrong class or method name in `CreateHook` | Copy names from API docs; log on attach and confirm the line exists |
| Swing works, no projectile | Exception swallowed in `EmitProjectile` | Read the warning in the log; fix the spawn path arguments |
| Projectile count grows forever | Missing release path (swing end or level leave) | Add the missing release; re-run steps 3-5 |
| Victim B shows victim A's count | Global or shared key | Re-key by victim identity; clear map on level leave |
| Stock weapons changed behavior | Hooked the base class, not your subclass | Retarget hooks to `tut.TutBlade` only |
| Icon wrong or blank | Cell coordinates mismatch or sheet conflict | Re-verify cell; ensure no other mod ships the same sheet |
| Save corrupt after removal | Non-graceful missing content | Redesign for graceful absence; never hand-edit the save |

## Safety and rollback

- Disposable copied saves only. Live progression stays untouched until the full 8-step loop passes twice.
- Uninstall is deleting the deployed folder. A clean additive design restores vanilla exactly.
- Never test on a run you care about, never sync experiments to cloud, never hand-edit saves.

## Further reading

- [Your First Mod](../getting-started/03-first-mod.md) - orig-first and fail-open basics.
- [Animations & VFX](../animations-vfx/index.md) - presenting this weapon's effects.
- [Game Data](../game-data/index.md) - packaging the row this weapon needs.
- [Debugging & Testing](../debugging-testing/index.md) - fixtures and proof discipline.
