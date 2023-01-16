# HAT

## Overview

**HAT** is a [MonoMod](https://github.com/MonoMod/MonoMod)-based mod loader for FEZ, currently in development. When patched into the FEZ instance, it can be used to dynamically load game modifications on the game launch. The mods themselves can add/override assets or inject its own logic.

## Building

1. Clone repository.
2. Copy all dependencies listed in `Dependencies` directory and paste them into said directory.
3. Build it. idk. it should work.

## Installing mod loader

1. Download [MonoMod](https://github.com/MonoMod/MonoMod/releases) (for .NET 4.5.2) and unpack it in the game's directory.
2. Put FEZ.HAT.mm.dll in the game's directory.
3. Run command `MonoMod.exe FEZ.exe` (or drag `FEZ.exe` onto `MonoMod.exe`). This should generate new executable file called `MONOMODDED_FEZ.exe`.
4. Run `MONOMODDED_FEZ.exe` and enjoy modding!

In the future, this process will be automated by a custom-made installer/mod manager (something like Olympus for Celeste's Everest).

## Adding mods

1. On first HAT launch, "Mods" directory should be created in the executable's directory. If not, create it.
2. Put a mod in this directory
3. Start the game.

It's that simple!

## Creating your own mod

1. Create a mod's directory. You can name it whatever, but it would be nice if it at least contained the actual mod's name to avoid confusion.

2. In your mod's directory, create `Metadata.xml` file. Its content should look roughly like this:

```xml
<Metadata>
   <Name>YourModName</Name>
   <Description>Short description of your mod.</Description>
   <Author>YourName</Author>
   <Version>1.0</Version>
   <LibraryName></LibraryName>
</Metadata>
```

3. If you want to add new assets or override existing ones, create `Assets` directory within your mods directory. All valid files within it will be loaded as game assets with path relative to the `Assets` directory. Currently, the only supported format is `.xnb`. As of right now, there isn't really a good way of creating `.xnb` assets and you have to rely on [FEZRepacker](https://github.com/Krzyhau/FEZRepacker).

4. If you want to append a library with a custom logic, put it in your mod's directory and put its name with extension into the `LibraryName` property in mod's metadata. The library will be loaded only if its extension ends with `.dll`.

## Creating custom logic

Mod loader loads library file given in metadata as an assembly, then attempts to create instances of every public class inheriting from game's `IGameComponent` interface before initialization (before any services are created). After the game has been initialized, it adds created instances into the list of game's components and initializes them, allowing their `Update` and `Draw` (use `DrawableGameComponent`) to be properly executed within the game's loop.

In order to create a HAT-compatible library, start by creating an empty C# library project. Then, add `FEZ.exe`, `FezEngine.dll` and all other needed game's dependencies as references - make sure to set "Copy Local" to "False" on all of those references, otherwise you will ship your mod with copies of those files.

Once you have your project done, create a public class inheriting from either `GameComponent` or `DrawableGameComponent` and add your logic there. Once that's done, build it and put it in the mod's directory.

For help, you can see an example of already functioning mod: [FEZUG](https://github.com/Krzyhau/FEZUG).
