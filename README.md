# HAT - Simple mod loader for FEZ

![Thumbnail](Docs/thumbnail.png)

## Overview

**HAT** is a [MonoMod](https://github.com/MonoMod/MonoMod)-based mod loader for FEZ, currently in development. Its main purpose is to make process of FEZ modding slightly easier for end user.

When patched into the FEZ instance, it can be used to dynamically load game modifications on the game launch. Correctly prepared mods can add/override game assets or inject its own logic through custom-made plugin.

## Installing mod loader

1. Download latest `HAT.zip` from Release tab and unpack it in the game's directory (next to the `FEZ.exe`).
2. Run `hat_install.bat` (for Windows) or `hat_install.sh` (for Linux). This should generate new executable file called `MONOMODDED_FEZ.exe`.
3. Run `MONOMODDED_FEZ.exe` and enjoy modding!

In the future, this process will be automated by a custom-made installer/mod manager (something like Olympus for Celeste's Everest).

## Adding mods

1. On first HAT launch, `Mods` directory should be created in the executable's directory. If not, create it.
2. Download the mod's archive and put it in this directory.
3. Start the game with `MONOMODDED_FEZ.exe` and enjoy your mod!

It's that simple!

## Building HAT

1. Clone repository.
2. Edit `UserProperties.xml` to set up dependencies:

* Remove the property `UserPropertiesNotSetUp`.
* Set `FezDir` to FEZ's directory (where FEZ.exe and other binaries are located).
* _Optional but recommended_: To prevent git from tracking your updated `UserProperties.xml`, run the command `git update-index --skip-worktree UserProperties.xml`.
* Additionally, change `CopyOverHATToFez` flag if you want to copy HAT binary and all its dependencies to the FEZ directory once built.

3. Build it, and then follow the installation instructions to test it.

## "Documentation"

* [Create your own HAT modifications](/Docs/createmods.md)
* [Additional HAT behaviour](/Docs/additional.md)

## Mods created for HAT

* [FEZUG](https://github.com/Krzyhau/FEZUG) - a power tool for speedrun practicing and messing with the game
* [FezSonezSkin](https://github.com/Krzyhau/FezSonezSkin) - mod replacing Gomez skin with Sonic-like guy seen in Speedrun Mode thumbnail
* [FezMultiplayerMod](https://github.com/FEZModding/FezMultiplayerMod) - mod adding multiplayer functionalities to FEZ
