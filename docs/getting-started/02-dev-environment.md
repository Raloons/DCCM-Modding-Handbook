# Dev Environment (status: live-observed pattern)

Set up the tools that turn your ideas into installable mods. This page assumes you have never built a .NET project before. Every piece of configuration is explained line by line: what it is, what it does, and what breaks if it is wrong.

## What are all these pieces?

- **.NET SDK (reference: 10.0).** A free toolkit from Microsoft that translates human-written C# code into programs computers can run. Think of it as the oven that bakes your recipe. You write the recipe (code); the SDK bakes it (builds it). Install it from Microsoft's .NET download page and confirm it afterward (Step 1 below).
- **C# (the language).** The programming language your mods are written in. If you have never coded: code is just precise instructions, and this handbook's examples are designed to be readable even on day one. You do not need to master C# before your first mod - you need to be able to read the example, change the safe parts (like text), and build.
- **A class-library project.** Your mod is not a standalone program with its own window. It is a *library*: a bundle of code the game loads. Think of the difference between a food truck (standalone program) and a new dish served through the restaurant's existing counter (library). Mods are dishes, not trucks.
- **The MDK package (`DeadCellsCoreModding.MDK`, reference: 1.0.1).** The Mod Development Kit: pre-written plumbing that teaches your project how to become a mod (generating the manifest, packaging assets). You reference it; you never edit it.
- **`modinfo.json` (the manifest).** A small description file the build generates: the mod's name, entry point, and type. The framework reads this to discover your mod. If it is missing or wrong, your mod is invisible - the game will not error, it just will not load it.
- **The output folder (`output/<ModName>/`).** The finished, servable dish: your compiled code plus the manifest (plus a `res.pak` file only if your mod ships art, audio, or data). This exact folder is what you copy into the game.

## Step 1 - Install the .NET SDK and prove it works

Install .NET SDK 10.0, then open a terminal (PowerShell on Windows) and run:

```powershell
dotnet --version
```

Reading that output: you just asked the oven to introduce itself. Expected output is a version number starting with `10.` (for example `10.0.100`). If the command is not recognized, the SDK did not install correctly or the terminal was opened before installation finished - close and reopen the terminal first, then reinstall if it still fails. Do not continue until this command prints a version: nothing else in this page works without it.

## Step 2 - Create the project file, line by line

Create a folder for your mod (example: `TitleScreenTag`), and inside it create `TitleScreenTag.csproj` with this content. Then read what every line means - there are no filler lines here:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <ModName>TitleScreenTag</ModName>
    <ModType>mod</ModType>
    <ModMain>TitleScreenTag.TitleScreenTagMod</ModMain>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DeadCellsCoreModding.MDK" Version="1.0.1" />
  </ItemGroup>
</Project>
```

What does this mean, line by line?

- `<Project Sdk="Microsoft.NET.Sdk">` - "This is a standard .NET project." The Sdk part gives you all the normal build behavior for free.
- `<TargetFramework>net10.0</TargetFramework>` - "Bake this with .NET 10." This must match your installed SDK's major version and the framework's expectation. Wrong version here means the game may refuse to load the result.
- `<ImplicitUsings>enable</ImplicitUsings>` - "Let me skip repetitive boilerplate at the top of code files." Pure convenience; it changes nothing about what your mod can do.
- `<Nullable>enable</Nullable>` - "Warn me when I might be using something empty." This turns whole categories of crashes into build warnings you can fix before ever launching the game. Keep it on.
- `<ModName>TitleScreenTag</ModName>` - "My mod is called TitleScreenTag." This name becomes the output folder name and how the framework identifies your mod. Use one word, no spaces.
- `<ModType>mod</ModType>` - "This is a regular mod." (The toolchain supports other types; yours is `mod`.)
- `<ModMain>TitleScreenTag.TitleScreenTagMod</ModMain>` - "When you load my mod, start with the `TitleScreenTagMod` class inside the `TitleScreenTag` group." This is the front door of your mod. If this name does not exactly match your code file's names, the framework cannot find the door and your mod silently does nothing.
- `<PackageReference ... MDK ... />` - "Plug in the Mod Development Kit, version 1.0.1." This is the plumbing that generates your manifest and packages assets. Exact version matters: different MDK versions can generate different manifests.

## Step 3 - Build, and read the output like a checklist

In the mod folder, run:

```powershell
dotnet build -c Release
```

In plain language: you told the oven to bake the dish (`build`), using the final recipe (`-c Release` means the optimized release version, not the draft debug version you would use while developing).

Expected output ends with:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

How to read those numbers: the translation from code to runnable mod worked with zero complaints. The numbers matter enormously:

- **Errors (must be 0):** the bake failed; nothing usable was produced. Read the error text - it names the file and line number. Fix and rebuild.
- **Warnings (must be 0):** the bake worked, but the oven is uneasy. In modding, warnings in hook code almost always mean a name or signature mismatch that will fail *silently* in game (your hook simply never fires, with no crash to point at the cause). Treat every warning as a bug. Fix and rebuild.

After a clean build, confirm the finished dish exists:

```
bin/Release/net10.0/output/TitleScreenTag/
  TitleScreenTag.dll      <-- your compiled mod
  modinfo.json        <-- the generated manifest
```

Open `modinfo.json` and confirm the mod name and entry point match what you wrote in the project file. If they do not match, the build used a different project file than the one you are reading - check you are in the right folder.

## Step 4 - Deploy (copy the dish to the counter)

Copy the entire `output/TitleScreenTag/` folder into the game's mod folder:

```powershell
Copy-Item -Recurse bin/Release/net10.0/output/TitleScreenTag '<Dead Cells>/coremod/mods/'
```

Put simply: replace `<Dead Cells>` with your real game folder. You are placing your finished dish on the extension counter from the Installation page. Note what you copy: the *whole folder*, not just the DLL. The framework discovers mods by scanning subfolders of `coremod/mods/` - a lone DLL dropped in the wrong place is invisible.

Verify with your eyes: `<Dead Cells>/coremod/mods/TitleScreenTag/` now contains `TitleScreenTag.dll` and `modinfo.json`. If the folder is there but the game never loads it, come back and re-check this exact listing first - nine times out of ten the folder structure is the problem, not the code.

## Step 5 - Uninstall (know the exit before you need it)

Delete `<Dead Cells>/coremod/mods/TitleScreenTag/`. That is the entire uninstall procedure. Launch the game and confirm everything is exactly as before.

Why learn the exit before you need it? Mods installed this way leave no residue when their folder is removed (as long as they only added content and never overwrote stock files - which is why this handbook always teaches additive mods). Knowing the exit makes experimenting fearless: at any point, one deleted folder returns you to vanilla.

## Common beginner failures

- **`dotnet` is not recognized.** The SDK is not installed, or the terminal predates the install. Reopen the terminal; if still failing, reinstall the SDK.
- **Build errors about the MDK package.** No internet during first build (the package must download once), or the version number is wrong. Check your connection and the exact version string.
- **Build succeeds but the mod never loads.** The `ModMain` name does not match the code, or you deployed the wrong folder (Debug instead of Release, or the DLL without its folder). Re-check Steps 2 through 4 in order.
- **Warnings ignored, hook silently dead.** Go back to zero warnings. The warning text is the diagnosis - read it fully before changing anything.
- **Checking `bin/`, `obj/`, or `*.pak` files into git.** These are baked outputs, not ingredients. The handbook's `.gitignore` already excludes them - if git shows them as new files, your ignore file is missing or in the wrong folder.

## Safety and rollback

Everything on this page is reversible: builds happen in your project folder (not in the game), deployment is a copy you can delete, and uninstall is one deleted folder. The one thing to protect remains your saves - keep that backup current.

Next: [Your First Mod](03-first-mod.md), where you write actual code and watch the game obey it.
