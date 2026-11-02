# Installation

Get a working game plus framework install you can mod safely. This page assumes you have never modded Dead Cells before. Every step explains what you are doing and why, so both first-time humans and AI assistants can follow it exactly.

## What are all these pieces?

Before touching anything, here is the cast of characters. You will see these words everywhere in this handbook:

- **Dead Cells (the game).** The game you bought on Steam. It lives in a folder on your disk, something like `C:\Program Files (x86)\Steam\steamapps\common\Dead Cells`. It runs fine on its own, and installing the framework does not modify the game's own files.
- **DCCM (Dead Cells Core Modding).** The community framework that lets mods plug into the game. It adds loading and hook infrastructure alongside the game without changing how the base game itself works. DCCM and its docs are here:
  - [DCCM GitHub](https://github.com/dead-cells-core-modding/core)
  - [DCCM docs](https://dead-cells-core-modding.github.io/docs/)
- **The modded launcher (`DeadCellsModding.exe`).** A special way of starting the game with the framework switched on. The normal Steam Play button starts the game *without* mods. This distinction is the single most common beginner confusion, so say it back to yourself: *mods only load under the modded launcher.*
- **`coremod/mods/`.** The folder inside your game install where built mods live. Each mod is its own subfolder. Adding a mod means copying a folder here; removing a mod means deleting that folder.
- **`coremod/logs/`.** Where the game writes a diary of what happened (`log_latest.log`). When something does not work, this diary is always step one.
- **Your saves.** Your run progress, stored under your Steam userdata folder. Mods (especially gameplay mods later) can interact with saves, so we back them up before anything else.

## Before you start

You need: a Steam copy of **Dead Cells** (these tutorials reference game version **v35**), an internet connection for downloads, and a few gigabytes of free disk space for backups. You need Windows for the reference toolchain used here.

## Step 1 - Check your game version

Open Steam, right-click Dead Cells, and confirm which version you have. DCCM releases support specific game versions, and mixing them is the most common installation failure.

Why this matters: a framework built for one version of the game may not understand another version's internals. Version-matching is not optional polish; it is the foundation everything else stands on. The reference pairing for these tutorials is game **v35** with DCCM **35.13.8**. If your versions differ, check the DCCM release notes for the pairing you need before continuing.

## Step 2 - Install DCCM

Follow the DCCM project's own installation instructions for your game version (linked above). Do not improvise this step: the framework must land in exactly the right place inside your game folder.

In everyday terms: you are installing the framework's loader files into the game folder. If files land in the wrong place, mods will not be found, and the resulting errors will look mysterious. When the install is correct, your game folder contains the modded launcher and a `coremod` folder.

Verify with your eyes, right now:

1. Open your Dead Cells game folder.
2. Confirm `DeadCellsModding.exe` exists next to the vanilla game files.
3. Confirm a `coremod` folder exists, containing `mods` and `logs` folders (they may be empty - empty is fine).

If any of the three is missing, stop. Re-read the DCCM install instructions. Do not continue to mods until all three exist, because every later step assumes them.

## Step 3 - Back up your saves (do this now, not later)

Find your Dead Cells save folder under your Steam userdata directory and copy the entire Dead Cells save folder somewhere safe, like `Documents\DeadCells-Save-Backup-<today's date>\`. Confirm the copy by comparing folder sizes or file counts: the backup must match the original.

An analogy that sticks: photocopying an important document before letting anyone borrow it. Title-screen mods barely touch saves, but gameplay mods later absolutely can, and swapping between modded and vanilla launches on the same run without a backup is how progress gets damaged. The habit starts here, on day one, when it is easy - so that it is automatic later when it matters.

How to know the backup worked: open the backup folder and confirm it contains the same files as the original (same names, same sizes). A backup you have not verified is a hope, not a backup.

## Step 4 - Run the modded launcher once, with no mods

Double-click `DeadCellsModding.exe` directly (not the Steam Play button). Reach the title screen, then close the game normally through its own menus.

Why bother launching with no mods? You just proved the framework itself works on your machine with zero mods involved. This gives you a clean baseline: if a later mod breaks something, you already know the launcher alone was fine, so the mod is the suspect - not your install. Check `coremod/logs/` afterward and confirm a fresh log file was written. That log file is where you will look first for every future problem.

## Common beginner failures

- **"I installed the mod but nothing happens."** You launched with the Steam Play button instead of `DeadCellsModding.exe`. The vanilla game does not know your mods exist. Always launch modded through the modded launcher.
- **"The framework installed but the game crashes on start."** Version mismatch between game and DCCM is the prime suspect. Confirm both versions against the DCCM release notes.
- **"I can't find my saves."** They live under Steam's userdata folder, not in the game folder. Search Steam's help pages for the save location if stuck, and back up the whole Dead Cells save folder, not individual files.
- **"I skipped the backup; can I do it later?"** Yes - do it now. It takes one minute and it is the cheapest insurance in this entire handbook.

## Safety and rollback

Removing everything you installed restores the exact original state: delete mod folders from `coremod/mods/` to uninstall mods. Your save backup restores your progress no matter what a modded session does. Keep that backup until you have made a newer one.

Next: [Dev Environment](02-dev-environment.md), where you set up the tools that turn your ideas into installable mods.
