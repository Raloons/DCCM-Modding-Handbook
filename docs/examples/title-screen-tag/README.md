# TitleScreenTag example

Minimal educational mod used by `getting-started/03-first-mod.md`.

- `TitleScreenTag.csproj` - build definition (MDK 1.0.1, `net10.0`).
- `TitleScreenTagMod.cs` - one orig-first hook, fail-open.

Build: `dotnet build -c Release`
Deploy: copy `bin/Release/net10.0/output/TitleScreenTag/` to `<Dead Cells>/coremod/mods/`
Launch: `DeadCellsModding.exe` only. Back up saves first.
