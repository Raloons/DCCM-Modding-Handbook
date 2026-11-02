# Dev Environment (status: live-observed pattern)

## Goal

Set up a .NET project that builds a deployable DCCM mod.

## Prerequisites

- Completed [Installation](01-installation.md).
- **.NET SDK 10.0** (reference toolchain used in the author's tree).
- The **DeadCellsCoreModding.MDK** NuGet package (reference: **1.0.1**).

## Steps

1. Create a class-library project targeting `net10.0` with nullable + implicit usings enabled.
2. Reference the MDK package. Minimal `.csproj` shape (names are yours to choose):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <ModName>HelloTitle</ModName>
    <ModType>mod</ModType>
    <ModMain>HelloTitle.HelloTitleMod</ModMain>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DeadCellsCoreModding.MDK" Version="1.0.1" />
  </ItemGroup>
</Project>
```

3. Write one class extending `ModBase` (see [Your First Mod](03-first-mod.md)).
4. Build with `dotnet build`. The build produces an `output/<ModName>/` folder containing the DLL plus `modinfo.json` (and `res.pak` only if you add assets).
5. Deploy by copying `output/<ModName>/` into `<Dead Cells>/coremod/mods/`.

## Why this works

The MDK build task generates the `modinfo.json` manifest and packages any declared assets. The framework discovers each subfolder of `coremod/mods/` at launch. Keeping the mod entry point (`ModMain`) stable lets the framework instantiate your `ModBase` subclass.

## Common failures

- Build warnings treated as unimportant — keep builds at **0 warnings / 0 errors**; warnings in hook code often mean a signature mismatch.
- Editing the built DLL in `coremod/mods/` instead of the source — always rebuild from source and redeploy the whole folder.
- Checking `bin/`, `obj/`, `*.pak`, or saves into git — the handbook `.gitignore` already excludes them.

## Safety / rollback

Builds are local and reversible: delete the deployed folder from `coremod/mods/` to uninstall. Never commit API keys, absolute paths, or save files.
