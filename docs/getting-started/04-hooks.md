# How Hooks Work

You have been attaching hooks since [Your First Mod](03-first-mod.md). This page explains what actually happens when you do: how the framework intercepts the game, the four ways to hook, the call-chain rules that decide whose code runs in what order, and the sharp edges (disable semantics, threading, Harmony nesting). Everything here describes real framework behavior — condensed from the DCCM docs, the framework source, and its test suite.

## What hooking actually does

A DCCM hook is a **native detour of a Hashlink function entry point**. When you hook a game function, the framework patches the first bytes of that function's JIT-compiled native code so execution jumps into managed C# code first, then (usually) back into the original. The core even hooks the Hashlink JIT itself and reserves padding around function entries so these detours always have room.

Plain version: the framework rewires the doorway itself, not the nameplate on the door. That is why assigning a proxy field does nothing:

```csharp
dc.Boot.Class.initRes = Hook_InitRes; // only changes the C# wrapper field
```

Haxe bytecode calls `Boot.initRes` directly. It never reads the C# proxy field, so swapping that field changes nothing observable. The generated event (`Hook__Boot.initRes += ...`) works because it resolves the function's index and detours the real entry point. If your hook seems attached but never fires, a field assignment somewhere is the first suspect.

## The four ways to hook

| Mechanism | How you use it | When to reach for it |
|---|---|---|
| Generated typed events | `Hook_Beheaded.addMoney += handler;` | Default choice. Most ergonomic, compile-time checked |
| Runtime registration | `HashlinkHooks.Instance.CreateHook("tool.$Weapon", "create", handler)` | The typed event is missing, or you resolve functions yourself |
| HarmonyX | `[HarmonyPatch(typeof(Bounds), nameof(Bounds.load))]` | You need stage separation or priority among patches |
| Raw native | `NativeHooks.Instance.CreateHook(addr, handler)` | Advanced and internal only. Raw native ABI |

**Generated typed events.** For every game object type, the toolchain generates a static `Hook_<Type>` class holding `orig_<member>` delegate types (the original's shape), `hook_<member>` delegate types (your handler's shape: original first, then the real parameters), and event accessors. Instance methods insert `self` right after `orig`; static methods have no `self`; constructors appear as `__constructor__`:

```csharp
Hook__LogoSplashscreen.__constructor__ += Hook_LogoSplashscreen_onConstructor;

private void Hook_LogoSplashscreen_onConstructor(
    Hook__LogoSplashscreen.orig___constructor__ orig,
    LogoSplashscreen self,
    Ref<double> delay)
{
    orig(self, delay);
    // ...
}
```

These classes are *generated artifacts*, not hand-maintained source: thousands of types and tens of thousands of events, whatever the current proxy build contains. They can lag or be absent in a given build.

**Runtime registration.** Looks the function up by Hashlink type and method name at runtime, so it does not depend on the generated class existing:

```csharp
public delegate Weapon orig_create(Hero hero, InventItem item);

public static Weapon Hook_create(orig_create orig, Hero hero, InventItem item)
{
    if (WeaponCreateMap.TryGetValue(item._itemData.id.ToString(), out var creator))
        return creator(hero, item);

    return orig(hero, item);
}

// registration
HashlinkHooks.Instance
    .CreateHook("tool.$Weapon", "create", Hook_create)
    .Enable();
```

`CreateHook` returns a handle with `Enable()` / `Disable()`. There is also an overload that takes an already-resolved function, which skips the string lookup.

**Signature rules (all four paths).** The first parameter of your delegate must be the `orig` delegate type. The rest must match the target exactly, in order, including `self` placement. By-reference arguments use `HaxeProxy.Runtime.Ref<T>` and writing `.Value` writes the native slot. Return types must match: if you never call `orig`, your return value *is* the result.

## Chain semantics: order matters most

Each hooked function owns one hook manager holding a list of subscribed delegates. The call chain is built inside-out: the first registered hook wraps vanilla directly, and each later hook wraps the previous one. Consequences:

1. **Last registered runs first.** Subscribe A, then B, and calls flow B, then A, then vanilla.
2. **`orig` points inward, not necessarily at vanilla.** B's `orig` calls A; A's `orig` calls vanilla. If another mod hooked earlier, your `orig` runs that mod first.
3. **Skipping `orig` suppresses everything inside**, including earlier mods' hooks and vanilla. An outer hook cannot be suppressed by an inner one.
4. **Adding or removing a hook rebuilds the chain** on the next call. Subscribing the same handler twice is a no-op (duplicates are deduplicated), and disabling then re-enabling moves a hook to the outermost position.
5. **There is no priority on event hooks.** Cross-mod order depends on load order, which is a poor contract to rely on. If ordering matters, say so in your testing, not your assumptions.

## Disable is logical, not physical

`Disable()` removes your delegate from the chain list. The native detour, the callback, the manager, and the handle all stay alive for the rest of the process: once a function has been hooked, its entry stays detoured even with zero active hooks (calls just flow straight through to the original). One refinement: a hook created disabled and never enabled never diverts execution at all, but the moment it is enabled once, disabling cannot restore that pristine state. Prefer `-=` event removal for temporary hooks, since it leaves no handle retained.

## Threading and marshaling essentials

- **Treat hooks as main-thread only.** The hook lists are plain unsynchronized collections. Never attach, remove, enable, or disable hooks from a background thread.
- **You speak proxy types; the framework handles the rest.** Non-primitive arguments are boxed and adapted internally, `Ref<T>` arrives as a native pointer you read and write normally, and null objects and strings pass through as null.
- **Catch your own exceptions in optional hooks.** Exceptions do cross the managed-to-Hashlink boundary with type, message, and a stitched stack trace preserved — but an unhandled one can still break the Haxe call it interrupted. Required-path hooks should fail loudly; cosmetic hooks should log and swallow.

## HarmonyX nesting (not merging)

Harmony patches and event hooks are *nested*, not merged into one priority-sorted chain. A call flows: native detour, framework callback, Harmony replacement (prefixes first), then the processor that runs the whole event-hook chain (last registered inward to vanilla), then Harmony postfixes. Practical fallout:

- `[HarmonyPriority]` orders Harmony patches against each other only. A prefix always runs before every event hook; a postfix always runs after them and vanilla.
- A prefix returning false skips the processor body, so event hooks and vanilla never run.
- An event hook that skips `orig` suppresses vanilla but does *not* suppress Harmony postfixes.

## Versioning caution

Because typed hook classes are generated per proxy build, they can be missing or stale for any given function. Guard optional typed hooks, or prefer `CreateHook` with a try/catch fallback: it resolves by name at runtime and keeps working across builds where the generated class does not. Either way, the modding kit and proxy build must match the game version for typed hooks to be trustworthy.

## Practical rules

1. Never use proxy field assignment to intercept a Haxe call. Use events or `CreateHook`.
2. Call `orig` unless replacing the function is the explicit intent — otherwise earlier mods and vanilla are suppressed.
3. Remember `orig` may call another mod first, not vanilla directly.
4. Do not rely on cross-mod event-hook ordering. Last registered is outermost; load order is the only order.
5. Use Harmony prefixes/postfixes for stage separation and priority, remembering they wrap the event chain.
6. Remove event subscriptions with `-=` and disable handles when done, knowing the native detour remains.
7. Keep handlers small and exception-safe. They run on the game thread, often on hot paths.
8. Never re-enter the same hooked function from inside its handler; it recurses through the chain.
9. Create and mutate hooks on the main thread only.
10. For cross-version mods, prefer guarded `CreateHook` over bare typed-hook usage.

## Further reading

- [Your First Mod](03-first-mod.md) - the hook pattern in action, line by line.
- [Gameplay Modifications](../gameplay-mods/index.md) - orig-first discipline, exactly-once delivery, authoritative state.
- [Debugging & Testing](../debugging-testing/index.md) - what to check when a hook never fires.
