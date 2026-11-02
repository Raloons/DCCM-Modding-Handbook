# Gameplay Modifications

Change combat behavior - damage windows, cooldown effects, per-hit tracking - without destabilizing the run. This guide builds a complete victim-attributed kill-window system and a read-only per-hit probe with an analysis workflow, plus stacking rules and a two-session test protocol. All code is independently written teaching material.

**You will produce:** a `TutWindow` on-kill damage-window mod and a `TutProbe` per-hit recorder with an offline log-analysis script.
**You will verify:** exactly-once grants under duplicate delivery, correct attribution under simultaneous kills, and a clean removal reload.

## Prerequisites

- [Custom Equipment](../custom-equipment/index.md) Parts 1-4 (hooks, orig-first, fail-open).
- A disposable fixture with a repeatable combat encounter (same room, same enemies, ideally a training dummy or arena).

## Part 1 - Concepts

### Orig-first, fail-open, exactly-once

1. **Orig first.** The original implementation settles native state (timers, phases, loot tables) before you read it.
2. **Fail open.** Your exception path leaves native behavior intact. A broken bonus is a balance bug; a skipped `orig` is a crash.
3. **Exactly-once per event.** Retries, reloads, and duplicated callbacks happen. Ring-buffer processed event IDs and refuse replays so each event applies once however many times it is delivered.

### Authoritative state, not derived displays

Cooldown bars, displayed damage, and cached UI values are projections. They disagree with authority on edge ticks, across reloads, and under stacked effects. Read the native timer or live hit record your rule is defined against:

- Cooldown gates read remaining cooldown time, not recharge progress.
- Damage rules read the hit record the engine produced, never a recomputation.
- Window durations run on the native clock, never on tick counts or wall time.

### Attribute shared events to their source

Simultaneous kills, damage-over-time landing mid-swing, pickups during combat - key all effect state by source or victim identity with bounded maps, cleaned on entity death and level leave. Global counters and single "last event" slots corrupt overlapping events.

### Route damage through the native call

Bosses and special encounters carry caps, phases, and immunity windows you did not build and must not bypass or silently alter. Emit damage through the native damage path so numbers, crits, protections, and difficulty scaling behave like stock.

## Part 2 - Complete kill-window system

On kill: grant a 5-second damage window. Duplicate kill deliveries grant once. Simultaneous victims attribute separately.

```csharp
using HaxeProxy.Runtime;
using ModCore.Mods;

namespace TutGameplay;

public sealed class WindowGrant
{
    public string EventId = "";
    public long ExpiresAt; // native-clock timestamp
}

public class TutWindowMod(ModInfo info) : ModBase(info)
{
    private const int WindowSeconds = 5;
    private const int MaxVictims = 64;
    private const int RecentEvents = 256; // exactly-once ring

    private readonly Dictionary<string, int> victimKills = new();
    private readonly Dictionary<string, WindowGrant> activeWindows = new();
    private readonly Queue<string> recentQueue = new();
    private readonly HashSet<string> recentSet = new();
    private bool initialized;

    public delegate void OrigKill(object self, object victim, string eventId);
    public delegate void OrigHit(object self, object target, object hit);
    public delegate void OrigLevelLeave(object self);

    public override void Initialize()
    {
        if (initialized) return;
        initialized = true;
        var hooks = HashlinkHooks.Instance;
        hooks.CreateHook("tut.TutRules", "onKill", OnKill).Enable();
        hooks.CreateHook("tut.TutRules", "onHit", OnHit).Enable();
        hooks.CreateHook("tut.TutLevel", "onLeave", OnLevelLeave).Enable();
        Logger.Information("TutWindow: hooks attached");
    }

    private void OnKill(OrigKill orig, object self, object victim, string eventId)
    {
        orig(self, victim, eventId); // native kill settles first
        try
        {
            if (!AdmitEvent(eventId)) return; // duplicate: refuse
            string victimId = VictimKey(victim);
            if (victimKills.Count >= MaxVictims) return; // bound the map
            victimKills[victimId] = victimKills.TryGetValue(victimId, out int n) ? n + 1 : 1;
            activeWindows[victimId] = new WindowGrant
            {
                EventId = eventId,
                ExpiresAt = NativeNow(self) + WindowSeconds
            };
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TutWindow: grant skipped");
        }
    }

    private void OnHit(OrigHit orig, object self, object target, object hit)
    {
        orig(self, target, hit);
        try
        {
            // Illustrative: check whether attacker holds a live window
            // (native-clock comparison), then let the ALREADY-EXECUTED
            // native damage stand. Never post-adjust the number here;
            // windows modify FUTURE hits via the documented bonus path.
            _ = IsWindowLive(self);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TutWindow: window check skipped");
        }
    }

    private void OnLevelLeave(OrigLevelLeave orig, object self)
    {
        try
        {
            victimKills.Clear();
            activeWindows.Clear();
            recentQueue.Clear();
            recentSet.Clear();
        }
        finally { orig(self); }
    }

    private bool AdmitEvent(string eventId)
    {
        if (recentSet.Contains(eventId)) return false;
        recentSet.Add(eventId);
        recentQueue.Enqueue(eventId);
        if (recentQueue.Count > RecentEvents)
            recentSet.Remove(recentQueue.Dequeue());
        return true;
    }

    private bool IsWindowLive(object self)
    {
        long now = NativeNow(self);
        foreach (var grant in activeWindows.Values)
        {
            if (grant.ExpiresAt > now) return true;
        }
        return false;
    }

    private long NativeNow(object self)
    {
        // Illustrative: read the NATIVE clock, never wall time or ticks.
        throw new NotImplementedException("teaching sketch");
    }

    private string VictimKey(object victim)
    {
        // Illustrative: stable per-victim identity, never a counter.
        throw new NotImplementedException("teaching sketch");
    }
}
```

Design notes for implementers (human or AI):

- `AdmitEvent` is the entire exactly-once story: a bounded ring, O(1) lookup, no growth. Every event-consuming feature in your mod should share one such gate, not reinvent it.
- Windows are checked on future hits, never retrofitted onto the hit that triggered them. Retrofitting means recomputing damage, which drops caps and protections.
- `OnLevelLeave` clears all four structures. Windows must never cross levels: a 5-second buff earned at a door should not be spendable two rooms later unless the design explicitly says so (and then the design must say how save/load mid-window works).

## Part 3 - Read-only per-hit probe and analysis

Probes measure; they never modify. A probe that changes damage to measure it invalidates its own data.

Recorder sketch (bounded, log-budgeted):

```csharp
private void OnHitRecord(OrigHit orig, object self, object target, object hit)
{
    orig(self, target, hit);
    try
    {
        if (samplesThisSession >= SampleCap) return; // hard cap
        samplesThisSession++;
        // Illustrative fields: attacker key, victim key, event id,
        // displayed amount, native amount, timestamp. One line per hit,
        // machine-parseable, no prose.
        Logger.Information(
            "TutProbe: hit atk={Atk} vic={Vic} ev={Ev} disp={D} nat={N} t={T}",
            AttackerKey(self), VictimKey(target), EventKey(hit),
            DisplayedAmount(hit), NativeAmount(hit), NativeNow(self));
    }
    catch (Exception ex)
    {
        Logger.Warning(ex, "TutProbe: sample skipped");
    }
}
```

Offline analysis (PowerShell sketch over the copied log file - analyze copies, never live logs):

```powershell
$lines = Select-String 'TutProbe: hit' session-copy.log | ForEach-Object { $_.Line }
$lines.Count                       # total sampled hits
($lines | Select-Object -Unique).Count  # distinct lines: duplicates visible here
# Parse atk/vic/ev fields per line, group by event id,
# and confirm each event id appears exactly once.
```

Analysis questions the probe must answer before you claim anything:

1. Does each event ID appear exactly once (no double delivery, no drops)?
2. Do displayed and native amounts agree (if not, which rule explains the gap)?
3. Under simultaneous kills, does each victim carry its own attribution?
4. Across save and reload, do counters resume without duplication or reset?

## Part 4 - Stacking and encounter rules

- Combined effects multiply in surprising orders. Test each effect alone, then each pair, then all together. Record which order the engine applies - that order is part of your design.
- Never tune numbers against one encounter. Validate in the training fixture, a normal room, and a boss encounter with different protections. A value that is fair in one can be broken in another.
- Document the intended stacking behavior in the mod description so players (and future you) can tell a bug from a design.

## Part 5 - Two-session test protocol

**Session A (mechanics):** fresh disposable fixture. Trigger 20 kills including 5 forced-simultaneous pairs. Expected: 20 grants, 0 duplicates in the probe log, per-victim counts exact, no errors. Save, exit.

**Session B (persistence):** reload Session A's save. Expected: counters resume, no re-grants for pre-save kills, windows expired across the save boundary behave per design. Remove the mod, reload: graceful handling, run intact. Restore backup, reinstall: full function.

Record both sessions (seed, counts, hashes) in your verification file. A claim without the record is a rumor.

## Part 6 - Troubleshooting matrix

| Symptom | Likely cause | Fix |
|---|---|---|
| Double grants for one kill | Duplicate delivery, no gate | Add the `AdmitEvent` ring; confirm with probe |
| Victim B credited to victim A | Shared/global key | Re-key by victim identity |
| Window works in arena, not in boss | Boss protection or phase rule | Route through native damage; redesign, never bypass |
| Cooldown gate misfires on some ticks | Reading UI progress | Read the authoritative timer |
| Probe log is gigabytes | No sample cap | Add `SampleCap` and per-line machine format |
| Numbers differ after reload | State not re-anchored to native clock | Re-derive windows from native clock on load |

## Safety and rollback

- Gameplay mods touch run state and saves: disposable copies, hash-verified backup and restore every session.
- Never promise balance from one observation; scaling, tiers, and stacking change the number.
- Rollback is mod-folder removal plus save restore. Prove it in Session B before any live-run testing.

## Further reading

- [Custom Equipment](../custom-equipment/index.md) - the items these rules empower.
- [Animations & VFX](../animations-vfx/index.md) - showing windows and procs.
- [Game Data](../game-data/index.md) - the numbers behind the rules.
- [Debugging & Testing](../debugging-testing/index.md) - fixtures and proof discipline.
