# Your First Mod: TitleScreenTag (status: educational example)

Build a minimal mod that appends text to the title-screen build label. You will write one code file, understand every line of it, deploy it, and watch the game obey it. This is an independently written teaching example that demonstrates the core pattern of all DCCM modding: an orig-first, fail-open hook.

**You will produce:** a working `TitleScreenTag` mod deployed in your game.
**You will verify:** the hook attaches (log line), the label changes (visible on screen), and removal restores vanilla.

## What is a hook? (read this first)

Imagine the game is giving a speech, and you want to add one sentence at the end. You have two options: shove the speaker aside (rude, and the speech breaks), or let them finish and then say your sentence into the same microphone. A **hook** is the second option, made of code: it lets the original function run to completion, then runs your addition.

Two rules make hooks safe, and they govern every mod in this handbook:

1. **Orig first.** Call the original function before doing anything of your own. The game sets up its state; then you read or extend that settled state. Skipping the original is shoving the speaker aside.
2. **Fail open.** If your code hits a problem, it must step aside quietly (log a warning) and leave the original result intact. A broken decoration must never break the wall it hangs on.

With that mental picture, the code below will make sense line by line.

## Step 1 - Create the code file, line by line

In your `TitleScreenTag` project folder (next to the `.csproj` from [Dev Environment](02-dev-environment.md)), create `TitleScreenTagMod.cs`:

```csharp
using HaxeProxy.Runtime;
using ModCore.Mods;
using Serilog;

namespace TitleScreenTag;

public class TitleScreenTagMod(ModInfo info) : ModBase(info)
{
    private const string Suffix = "TitleScreenTag - Dev Build";

    private bool initialized;

    public delegate void OrigSetMiscTexts(object self);

    public override void Initialize()
    {
        if (initialized) return;
        initialized = true;
        HashlinkHooks.Instance
            .CreateHook("pr.TitleScreen", "setMiscTexts", SetMiscTexts)
            .Enable();
        Logger.Information("TitleScreenTag: hook attached");
    }

    private void SetMiscTexts(OrigSetMiscTexts orig, object self)
    {
        orig(self);
        try
        {
            dynamic screen = self;
            if (screen.build == null) return;
            string before = screen.build.text?.ToString() ?? "";
            string display = string.IsNullOrWhiteSpace(before)
                ? Suffix
                : before + " | " + Suffix;
            screen.build.set_text(display.AsHaxeString());
        }
        catch (Exception ex)
        {
            // Fail open: log and leave the stock label alone.
            Logger.Warning(ex, "TitleScreenTag: label append failed");
        }
    }
}
```

Now, what does this mean? Going top to bottom:

- The three `using` lines are address books: they tell the compiler where `ModBase`, `HashlinkHooks`, and `Logger` live so you can use their short names. Without them you would have to write full addresses everywhere.
- `namespace TitleScreenTag;` puts your code in a labeled box with your mod's name, so it cannot collide with the game's code or other mods' code. This must match the first half of `ModMain` in your project file.
- `public class TitleScreenTagMod(ModInfo info) : ModBase(info)` declares your mod itself: "TitleScreenTagMod is a ModBase (a loadable mod), and here is how it receives its info." This must match the second half of `ModMain`. If these names disagree with the project file, the framework knocks on a door that does not exist and your mod silently never loads.
- `private const string Suffix = ...` is your mod's text, stored in one place with a name. Changing the label later means editing this one line, not hunting through logic.
- `private bool initialized;` plus the `if (initialized) return;` guard mean "set up exactly once even if the framework calls `Initialize` twice." Double-attaching a hook would run your code twice per event - this two-line guard prevents a whole class of duplication bugs. Use it in every mod.
- `public delegate void OrigSetMiscTexts(object self);` describes the *shape* of the original function: what it takes in (the screen object) and that it returns nothing. Think of it as a mold the framework uses to hand you the original. The shape must match reality - a wrong shape here is the classic "hook never fires" bug.
- `HashlinkHooks.Instance.CreateHook("pr.TitleScreen", "setMiscTexts", SetMiscTexts)` is the tap on the phone line: "when the title screen sets its miscellaneous texts, also call my `SetMiscTexts`." The first two strings name *whose* function and *which* function - copy them exactly from the API docs. `.Enable()` switches the tap on; without it, everything is wired but nothing flows.
- `Logger.Information("TitleScreenTag: hook attached")` writes one line in the game's diary (`coremod/logs/`). This is your smoke signal: if this line exists, your mod loaded and its setup ran. If it is absent, nothing else matters yet - go fix loading first.
- Inside `SetMiscTexts`, `orig(self);` runs the original first (rule 1: let the speaker finish). Only then does your code read the label the game just set.
- `dynamic screen = self;` says "treat this object flexibly." The game's screen object has fields like `build` (the label); `dynamic` lets you reach them by name.
- `if (screen.build == null) return;` is a guard: "if there is no label here, walk away quietly." Screens change between game versions; this line keeps your mod alive when the label is absent instead of crashing.
- The `before`/`display` lines read the current text and build the new text: if the label is empty, show just your suffix; otherwise append your suffix after a separator. You *add* to the label; you never replace the game's own version text. Additive changes are always safer than replacements.
- `display.AsHaxeString()` is the translator: your text is in .NET's language and the game speaks its own string language. You must convert before handing text over - assigning a raw .NET string directly is a classic crash. Whenever .NET code gives text to the game, translate it.
- The `try`/`catch` around everything is rule 2 (fail open): any surprise becomes one warning line in the log while the stock label stays exactly as the game set it.

## Step 2 - Build it clean

```powershell
dotnet build -c Release
```

Same idea as last page: run the build (see [Dev Environment](02-dev-environment.md) Step 3). You need `Build succeeded. 0 Warning(s) 0 Error(s)`. A warning here - especially about the delegate or the hook call - means your mold does not match the real function, and the hook will silently never fire in game. Fix warnings now; they are ten times harder to diagnose from inside the game.

## Step 3 - Deploy it

```powershell
Copy-Item -Recurse bin/Release/net10.0/output/TitleScreenTag '<Dead Cells>/coremod/mods/'
```

The short version: copy the whole finished folder into the game's mod folder (same as Dev Environment Step 4). Confirm `<Dead Cells>/coremod/mods/TitleScreenTag/` contains `TitleScreenTag.dll` and `modinfo.json` before launching - if this listing is wrong, no code on Earth will make the mod load.

## Step 4 - Launch and verify (two proofs)

Launch **only** via `DeadCellsModding.exe`. Never the vanilla Steam Play button for modded testing - the vanilla game does not load the framework, so your mod will be absent and you will debug a problem that does not exist.

**Proof 1 - the diary.** Open `coremod/logs/log_latest.log` and search for `TitleScreenTag: hook attached`. Found it? Your mod loaded and its setup ran. Absent? Stop - the problem is loading (folder placement, manifest, entry-point names), not your hook logic. Do not touch the code until this line exists.

**Proof 2 - the screen.** Reach the title screen and look at the bottom-left label. It should read the game's version text followed by `| TitleScreenTag - Dev Build`. Seen it? Your hook fired, read the label, translated your text, and set it. The label is unchanged in meaning (game version still there) with your addition appended - the additive ideal.

If Proof 1 passes but Proof 2 fails: the hook is attached but not firing or erroring. Search the log for `TitleScreenTag: label append failed` - the fail-open warning tells you exactly which side broke. A missing label object (`build == null`) means the screen layout differs from what the example expects; a translation error means the string handoff needs attention.

## Step 5 - Uninstall and confirm vanilla

Delete `<Dead Cells>/coremod/mods/TitleScreenTag/`, launch, and confirm the bottom-left label is back to exactly the game's own text.

Why this step earns its place: you just proved the complete round trip: install, effect, clean removal. Every future mod you build should pass this same round trip before you call it done. The uninstall test is not extra credit; it is half the feature.

## Step 6 - Experiments (do these - this is where understanding forms)

Change one thing at a time, rebuild, redeploy, and observe. Each experiment teaches one mechanism:

1. Change `Suffix` to your name. Rebuild, relaunch. Visible? You just proved the text flows from that one line through translation to the screen.
2. Move `orig(self);` to *after* your code. Observe, then move it back. (You will likely see wrong or missing text - that is the lesson: order matters because the original sets up the state you extend.)
3. Comment out `.Enable()`. Launch and check the log: the attach line is gone and nothing happens. That is what an unwired hook looks like - remember it, because you will see it again by accident someday.
4. Break the hook name strings deliberately (one letter), then fix them. The silent nothing that results is the classic name-typo failure. Now you recognize it on sight.

## Common beginner failures

- **No attach line in the log.** Loading problem, not logic problem. Re-check folder placement (`mods/TitleScreenTag/` with DLL plus manifest), `ModMain` names matching the code, and that you launched via `DeadCellsModding.exe`.
- **Attach line present, label unchanged, no warning.** The hook name strings do not match reality. Copy them character-for-character from the API docs.
- **Attach line present, label unchanged, warning present.** Read the warning - it names the failure. Missing label object means layout drift; translation errors mean string-handoff trouble.
- **Crash on title screen.** Something outside the `try` block threw, or `orig` was skipped. Your fail-open net only covers code inside the `try` - keep every touch of game objects inside it.
- **It worked, then stopped after I edited.** You deployed the Debug build, edited Release expectations, or copied the DLL without its folder. Rebuild Release, redeploy the whole folder, relaunch.

## Safety and rollback

This mod touches no saves, no data rows, and no assets: it reads one label and writes it back with an addition. Rollback is deleting one folder. Keep backing up saves anyway - the habit you build on this harmless mod is what protects you on the dangerous ones later.

## Further reading

- Full project source: `docs/examples/title-screen-tag/` (this page explains that code line by line).
- [Custom Equipment](../custom-equipment/index.md) - your first real item, using this exact hook discipline.
- [Debugging & Testing](../debugging-testing/index.md) - the log-first habit, formalized.
