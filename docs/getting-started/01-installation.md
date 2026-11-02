# Installation (status: live-observed pattern)

## Goal

Get a working game + framework install you can mod safely.

## Prerequisites

- Owned Steam copy of **Dead Cells** (reference version for these tutorials: **v35** line).
- **Dead Cells Core Modding (DCCM)** framework compatible with that game version (reference: **35.13.8** in the author's research tree).
- Windows machine with enough disk for a backup of your saves.

## Steps

1. Install Dead Cells via Steam. Note the exact game version.
2. Install DCCM per its docs into your Dead Cells folder:
   - [DCCM GitHub](https://github.com/dead-cells-core-modding/core)
   - [DCCM docs](https://dead-cells-core-modding.github.io/docs/)
3. Confirm the modded launcher exists: `DeadCellsModding.exe` next to the vanilla game files.
4. Locate the mod folders created by the framework (typical layout):
   - `<Dead Cells>/coremod/mods/` — where built mods are deployed.
   - `<Dead Cells>/coremod/logs/` — where mod logs go.
5. Back up your saves **before** installing any mod. Dead Cells saves live under your Steam userdata folder — copy the whole Dead Cells save folder somewhere safe.

## Why this works

DCCM only loads under the modded launcher. The vanilla Steam Play button bypasses the framework, so mods (and their save interactions) are only active when you deliberately launch modded.

## Common failures

- Launching the vanilla exe with mods deployed and wondering why nothing loads — use `DeadCellsModding.exe`.
- Mixing framework and game versions — check DCCM release notes for the supported game line.
- Skipping the save backup — title-screen mods are low-risk, but gameplay mods touch saves. Back up every time.

## Safety / rollback

Removing mods from `coremod/mods/` restores the vanilla title screen on next title re-entry. If a run save misbehaves after modded play, restore your backup copy and do not swap between modded and vanilla launches on the same run without backing up first.
