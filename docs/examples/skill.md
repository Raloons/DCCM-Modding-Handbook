# Skill Example: Reserve Battery Entry Point

Real project code from the Beyond the Dead research mod, presented as-is. This is the complete entry-point file of the Reserve Battery survival skill: an activation gate, a use effect, an opt-in test fixture, asset loading, and registry delegation. It is the cooldown-gate pattern from the tutorials, running in production form. The runtime, rules, and registry it delegates to are included below as Files 2-4. The env-gated native fixtures file is test-only scaffolding and is described, not printed.

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

## File 2: ReserveBatteryRuntime - gate, effect, and cues

```csharp
using dc;
using dc.tool;
using dc.tool.atk;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using Serilog;

namespace BtdReserveBattery;

// Independent Reserve Battery runtime. No carrier entity, no projectiles, no
// timers of its own: activation gating + native cooldown reset + native
// re-cooldown, all through verified native calls. Every entry point is
// identity-guarded; anything that is not BTD_ReserveBattery runs vanilla.
public static class ReserveBatteryRuntime
{
    public delegate void OrigActivation(dc.tool.hero.HeroActiveSkillsManager self, int id, double ratio);
    public delegate void OrigUse(dc.tool.hero.activeSkills.BeheadedActiveSkillsManager self, int id, InventItem item);

    public static bool IsReserveBattery(InventItem? item) => item?._itemData?.id?.ToString() == ReserveBatteryRules.ItemId;

    // Gate BEFORE any native activation work: a rejected use must not consume
    // cooldown, kill entities, or play success effects (the denied cue remains).
    // Mirrors the verified native
    // order (onActiveSkill starts cooldown, then dispatches useSkillItem), so
    // the gate has to live here, not in Use.
    public static void Activation(OrigActivation orig, dc.tool.hero.HeroActiveSkillsManager self, int id, double ratio)
    {
        InventItem? item = null;
        try { item = self.hero.inventory.getActiveOn(id); }
        catch (Exception ex) { Log.Warning(ex, "ReserveBattery gate: slot read failed; vanilla passthrough"); orig(self, id, ratio); return; }
        if (!IsReserveBattery(item)) { orig(self, id, ratio); return; }
        string reason;
        try
        {
            if (!TryFindRefreshTarget(self, id, out _, out reason))
            {
                Log.Information("ReserveBattery activation refused ({Reason}); cooldown untouched", reason);
                PlayDeniedCue();
                return; // genuine no-op: orig never runs, no cooldown consumed
            }
        }
        catch (Exception ex)
        {
            // Fail-safe direction: an unreadable target also refuses, never fires.
            Log.Warning(ex, "ReserveBattery gate evaluation failed; activation refused");
            PlayDeniedCue();
            return;
        }
        orig(self, id, ratio);
    }

    // Effect runs inside the native activation, after the native cooldown start.
    // Never calls orig: Reserve Battery spawns nothing and must not hit the
    // group-3 spawnPowerSkill throw for its custom id.
    public static void Use(OrigUse orig, dc.tool.hero.activeSkills.BeheadedActiveSkillsManager self, int id, InventItem item)
    {
        if (!IsReserveBattery(item)) { orig(self, id, item); return; }
        try
        {
            dc.tool.hero.HeroActiveSkillsManager manager = self;
            if (!TryFindRefreshTarget(manager, id, out InventItem? target, out string reason) || target == null)
            {
                Log.Information("ReserveBattery use skipped ({Reason}); native cooldown stands", reason);
                return;
            }
            manager.resetCooldownForItem(target);
            bool legendary = false;
            try { legendary = item.hasAffix(ReserveBatteryRules.HighCapacityCellAffix.AsHaxeString()); } catch { }
            if (legendary)
            {
                // Native override path (HeroActiveSkill.startCooldown with a
                // non-null override): flat 25 s instead of the 30 s just started.
                manager.startCooldownForItem(item, ReserveBatteryRules.LegendaryCooldownSeconds);
            }
            Log.Information("ReserveBattery activated: refreshed {Target}, legendary={Legendary}",
                target._itemData?.id?.ToString(), legendary);
            PlaySuccessFlash(manager);
            PlayActivationCue();
        }
        catch (Exception ex)
        {
            // No retry: native state is authoritative; report and keep vanilla visuals.
            Log.Warning(ex, "ReserveBattery use failed after native cooldown start");
        }
    }

    // Native slot scan: the other equipped skill is whatever non-RB item sits
    // in a different active slot and still has a live HeroActiveSkill entry.
    internal static bool TryFindRefreshTarget(
        dc.tool.hero.HeroActiveSkillsManager manager, int activatorSlot,
        out InventItem? target, out string reason)
    {
        target = null;
        reason = "unreachable";
        int slots;
        try { slots = manager.hero.inventory.nbActives; }
        catch { slots = 2; }
        if (slots < 1) slots = 1;
        if (slots > 4) slots = 4;
        bool sawOther = false;
        for (int slot = 0; slot < slots; slot++)
        {
            if (slot == activatorSlot) continue;
            InventItem? candidate;
            try { candidate = manager.hero.inventory.getActiveOn(slot); }
            catch { continue; }
            if (candidate == null) continue;
            sawOther = true;
            if (IsReserveBattery(candidate)) continue; // never refresh (or be) a twin battery
            dc.tool.hero.HeroActiveSkill? skill = null;
            try { skill = manager.getActiveSkill(candidate); } catch { skill = null; }
            if (skill == null) continue;
            double remaining;
            try
            {
                var nativeSkill = skill.skill;
                if (nativeSkill == null) continue;
                // getNextUseCooldownF is recharge-cycle progress, including
                // zero at a just-started cooldown or a charge boundary. The
                // reset endpoint instead tests this actual remaining timer.
                remaining = nativeSkill.coolDownF;
            }
            catch { continue; }
            var decision = ReserveBatteryRules.Decide(true, false, true, remaining);
            if (!decision.Proceed) continue;
            target = candidate;
            reason = decision.Reason;
            return true;
        }
        reason = sawOther ? "other skill ready or ineligible" : "no other skill equipped";
        return false;
    }

    // Success-only blue pulse: a small elecNova pop centered on the Beheaded.
    // This presentation call creates no attack object. Native on-use affixes
    // remain part of the enclosing activation. Called only after a successful refresh -
    // the refused-activation and skipped-use paths return before reaching it.
    // Any failure is logged and swallowed: the refresh already applied.
    private static void PlaySuccessFlash(dc.tool.hero.HeroActiveSkillsManager manager)
    {
        try
        {
            var hero = manager.hero;
            var level = hero._level;
            if (level == null) return;
            double xPx = (hero.cx + hero.xr) * 24.0;
            double yPx = (hero.cy + hero.yr) * 24.0;
            try { yPx -= hero.hei * 0.5; } catch { }
            level.fx.elecNova(xPx, yPx, 36.0);
        }
        catch (Exception ex) { Log.Warning(ex, "ReserveBattery success pulse failed; refresh already applied"); }
    }

    private static void PlayActivationCue()
    {
        try
        {
            dynamic loader = dc.hxd.Res.Class.get_loader();
            var sound = loader.loadCache("sfx/active/active_flask_use.wav".AsHaxeString(), dc.hxd.res.Sound.Class) as dc.hxd.res.Sound;
            var audio = dc.Audio.Class.ME;
            if (sound != null && audio != null) sound.play(null, 1.0, audio.sfxActiveChanGroup, null);
        }
        catch (Exception ex) { Log.Warning(ex, "ReserveBattery activation cue failed"); }
    }

    private static void PlayDeniedCue()
    {
        try
        {
            dynamic loader = dc.hxd.Res.Class.get_loader();
            var sound = loader.loadCache("sfx/active/active_error_l.wav".AsHaxeString(), dc.hxd.res.Sound.Class) as dc.hxd.res.Sound;
            var audio = dc.Audio.Class.ME;
            if (sound != null && audio != null) sound.play(null, 1.0, audio.sfxActiveChanGroup, null);
        }
        catch (Exception ex) { Log.Warning(ex, "ReserveBattery denied cue failed"); }
    }
}
```

### What to notice in File 2

- **The gate lives before native work, deliberately.** The comment records the verified native order (cooldown starts, then dispatch), which forces the gate into `Activation` rather than `Use`. Refusal is a genuine no-op: `orig` never runs, no cooldown is consumed, and the player hears a denied cue instead of silence.
- **The effect never calls `orig`.** Reserve Battery spawns nothing, so it must not reach the group-3 spawn path that throws for its custom ID. Skipping `orig` here is correct because there is no native behavior to preserve - only native calls to borrow (`resetCooldownForItem`, `startCooldownForItem`).
- **Remaining timer, not progress.** The slot scan reads `coolDownF` (the actual remaining timer) and explicitly rejects `getNextUseCooldownF` (recharge progress, zero at charge boundaries). This is the authoritative-state rule from the tutorials, with the wrong answer named so nobody "simplifies" it back.
- **No twin-battery loops.** The scan skips Reserve Battery candidates, and the rules file independently refuses them - the invariant is enforced at two layers.
- **Cues are best-effort and ordered.** Success flash and sound play only after a successful refresh; every cue failure is swallowed with a log line because the refresh already applied. Presentation must never be able to undo mechanics.

## File 3: ReserveBatteryRules - pure gate logic

```csharp
namespace BtdReserveBattery;

// Pure gameplay constants and activation-gate logic, shared by the
// native adapter and offline fixtures. No native calls here: testable offline.
//
// Approved design: Tactics-colored? No - Survival (green). 30 s base cooldown,
// 25 s with the High-Capacity Cell legendary affix. The base refresh has no
// damage, healing, stat scaling, or charges of its own; native affixes remain.
public static class ReserveBatteryRules
{
    public const string ItemId = "BTD_ReserveBattery";
    public const string HighCapacityCellAffix = "BTD_HighCapacityCell";
    public const double CooldownSeconds = 30.0;
    public const double LegendaryCooldownSeconds = 25.0;

    public static double CooldownFor(bool hasLegendaryAffix) =>
        hasLegendaryAffix ? LegendaryCooldownSeconds : CooldownSeconds;

    public enum Gate { Proceed, NoTarget, TargetReady, TargetIsReserveBattery, TargetHasNoSkill }

    // Affix eligibility follows Tonic (ExtraHeal) natively: RB carries the
    // same NoDamage tag, so every generation path (tryAddAffixes f@15657 group
    // 3/2 draws via the f@31711 predicate, tier affixes, forge rerolls,
    // checked addAffix) evaluates RB through the identical native tag rules.
    // Verified delta: Tonic additionally carries Heal + HasDuration; the only
    // affixes Tonic can roll that RB cannot are the six req-HasDuration
    // duration-end triggers (incompatible with an instant skill, excluded by
    // native requiredTags). Conversely GlobalShieldOnUse is RB-only because
    // its native forbiddenItem list names Tonic. These are two-sided deltas,
    // with no hand-written whitelist or blacklist anywhere in behavior.

    public readonly record struct GateDecision(Gate Outcome, string Reason)
    {
        public bool Proceed => Outcome == Gate.Proceed;
    }

    // Pure gate: the only refreshable target is a NON-Reserve-Battery skill
    // whose underlying OldSkill.coolDownF is still running (remainingF > 0).
    // getNextUseCooldownF is recharge progress, not this remaining timer.
    // Everything else
    // refuses activation, and refusal must never consume cooldown.
    public static GateDecision Decide(bool otherSlotOccupied, bool otherIsReserveBattery, bool otherHasSkillEntry, double otherCooldownRemainingF)
    {
        if (!otherSlotOccupied) return new GateDecision(Gate.NoTarget, "other slot empty");
        if (otherIsReserveBattery) return new GateDecision(Gate.TargetIsReserveBattery, "no twin-battery loops");
        if (!otherHasSkillEntry) return new GateDecision(Gate.TargetHasNoSkill, "no native skill entry");
        if (!(otherCooldownRemainingF > 0.0)) return new GateDecision(Gate.TargetReady, "other skill ready");
        return new GateDecision(Gate.Proceed, "refresh");
    }

    // Native charge model mirror (HeroActiveSkill.get_usesLeft, f@4622):
    // usesLeft = maxF <= F ? 0 : int((maxF - F) / cdPerUse). Resetting the timer
    // (coolDownF = 0) therefore restores exactly the legitimate maximum.
    public static int UsesLeft(double maxF, double coolDownF, double cdPerUseF)
    {
        if (!(maxF > 0) || !(cdPerUseF > 0)) return 0;
        if (maxF <= coolDownF) return 0;
        return (int)((maxF - coolDownF) / cdPerUseF);
    }
}
```

### What to notice in File 3

- **Pure and offline-testable.** No native calls, so fixtures verify the gate matrix without the game. The runtime (File 2) handles the engine; this file handles the decisions.
- **Named refusal reasons.** Every refusal carries a `Reason` string that the runtime logs. Debugging a refused activation means reading one word, not reproducing the session.
- **No whitelists.** Affix eligibility is derived from native tag rules with documented two-sided deltas, not a hand-maintained list that rots.

## File 4: ReserveBatteryItemRegistry - the data contract, enforced

```csharp
using dc;
using ModCore.Utilities;
using Serilog;

namespace BtdReserveBattery;

// Same retain/reinsert policy as CustomMutationRegistry / LandmineItemRegistry.
// Never synthesize rows; never touch other items' rows.
public static class ReserveBatteryItemRegistry
{
    private static object? canonical;
    // Snapshot of every affix id in the merged CDB (vanilla + BTD rows),
    // captured while the CDB root is in hand; the title-only Tonic-parity
    // probe enumerates this instead of a hand-written list.
    internal static readonly List<string> AffixIds = new();
    public static void AfterCdb(_Data_ cdb)
    {
        dynamic index = ((dynamic)cdb).item;
        dynamic row = index.byId.get(ReserveBatteryRules.ItemId.AsHaxeString());
        if (row == null && canonical != null)
        {
            row = canonical!;
            index.byId.set(ReserveBatteryRules.ItemId.AsHaxeString(), row);
            ((dc.hl.types.ArrayDyn)(object)index.all).push(row);
        }
        if (row == null) { Log.Information("ReserveBattery initial CDB row absent; foundation-only build"); return; }
        if ((int)row.group != 3 || (double)row.castCD != ReserveBatteryRules.CooldownSeconds || row.tier1.ToString() != "Survival")
            throw new InvalidOperationException("ReserveBattery CDB contract: group 3, castCD 30, tier1 Survival required");
        int affixCount = 0;
        dynamic affixes = row.legendAffixes;
        int alleged = (int)affixes.length;
        for (int i = 0; i < alleged; i++)
        {
            string? a = null;
            try { a = affixes.getDyn(i).affix?.ToString(); } catch { }
            if (a == ReserveBatteryRules.HighCapacityCellAffix) affixCount++;
        }
        if (affixCount != 1) throw new InvalidOperationException(
            $"ReserveBattery CDB row must grant {ReserveBatteryRules.HighCapacityCellAffix} exactly once (found {affixCount} in {alleged} entries)");
        int count = 0;
        foreach (dynamic candidate in (dc.hl.types.ArrayDyn)(object)index.all)
            if (candidate.id?.ToString() == ReserveBatteryRules.ItemId) count++;
        if (count != 1) throw new InvalidOperationException("ReserveBattery CDB row must occur exactly once");
        try
        {
            AffixIds.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (dynamic aff in (dc.hl.types.ArrayDyn)(object)((dynamic)cdb).affix.all)
            {
                string? id = aff.id?.ToString();
                if (!string.IsNullOrEmpty(id) && seen.Add(id)) AffixIds.Add(id);
            }
        }
        catch (Exception ex) { Log.Warning(ex, "ReserveBattery affix catalog snapshot failed"); }
        Log.Information("ReserveBattery CDB row PRESENT id={Id} group={Group} castCD={Cd} tier1={Tier}; vanilla untouched",
            row.id.ToString(), (int)row.group, (double)row.castCD, row.tier1.ToString());
    }
}
```

### What to notice in File 4

- **Contracts throw; probes log.** A violated row contract (wrong group, wrong cooldown, missing affix, duplicated row) throws `InvalidOperationException` - loud, immediate, impossible to play past. This is deliberate fail-*closed*: corrupt data must never reach gameplay. Contrast with behavior code, which fails open.
- **Retain and reinsert, never synthesize.** If the row vanishes on a later load (reload semantics), the canonical snapshot is restored. The registry repairs presence; it never invents content.
- **Exactly-once row check.** The full-catalog scan proves the row occurs precisely once - catching double-registration that a byId lookup alone would miss.

The unprinted fifth file, `ReserveBatteryNativeFixtures`, is env-gated test scaffolding (the `BTD_RESERVEBATTERY_CHECKS` title hook from File 1) that proves the runtime against synthetic natives. Test-only, dormant by default, and not needed to understand the skill.

## Further reading

- [Gameplay Modifications](../gameplay-mods/index.md) - gates, native timers, and exactly-once discipline.
- [Custom Equipment](../custom-equipment/index.md) - skill lifecycle alongside weapons.
- [Game Data](../game-data/index.md) - the registry pattern behind `AfterCdb`.
