# Create your own HAT modifications

## Basic mod architecture

Start by creating a mod's directory within `FEZ/Mods` directory. You can name it whatever you'd like, as the mod loader doesn't actually use it for mod identification, but it would be nice if it at least contained the actual mod's name to avoid confusion.

Mod loader expects `Metadata.xml` file in the mod's directory. Create one in a directory you've just made. Its content should look roughly like this:

```xml
<Metadata>
   <Name>YourModName</Name>
   <Description>Short description of your mod.</Description>
   <Author>YourName</Author>
   <Version>1.0</Version>
   <LibraryName></LibraryName>
   <Dependencies>
      <DependencyInfo Name="HAT" MinimumVersion="1.0"/>
   </Dependencies>
</Metadata>
```

`Name` tag is required and is treated as an unique case-sensitive identifier of your mod - mod loader will load only one mod with the same name (it'll choose the one with the most recent version).

`Version` tag is also required. Mod loader compares two version strings by putting them in an alphanumberical order, however, each number is treated as a separate token, which order is determined by numberical value (this means `1.2beta` will be treated as older version to `1.11`).

`LibraryName` is used to determine a DLL library with C# assembly the mod loader will load. The library should end with `.dll` extension and should be placed in your mod's directory. This tag is optional, as your mod doesn't have to add any new logic.

`Dependencies` is a list of `DependencyInfo` tags. If your mod requires a specific version of HAT mod loader or relies on another mod, your can use these tags to prevent mod loader from loading this mod if given dependencies aren't present. It's entirely optional.

All other fields are purely informational.

## Creating asset mod

If you want to add new assets or override existing ones, create `Assets` directory within your mods directory. All valid files within it will be loaded as game assets with path relative to the `Assets` directory. Currently, the only supported format is `.xnb`, but in the future, a conversion from popular file formats will be implemented, allowing much easier modding process (for isntance, PNG files will be automatically converted to Texture2D assets). As of right now, there isn't really a good way of creating `.xnb` assets and you have to rely on [FEZRepacker](https://github.com/Krzyhau/FEZRepacker).

As an example, here's an instruction on how to change Gomez's house background plane. Keep in mind that right now this process is unnecessarily convoluted and will definitely be simplified with the next FEZRepacker update:

1. Use FEZRepacker to unpack game's `Other.pak` archive.
2. Find `background planes/gomez_house_a.png` file and copy it.
3. Create directory, name it something like `Export`. In it, create `background planes` directory and put a copy of PNG file there.
4. Edit the image however you'd like.
5. Use FEZRepacker to pack the `Export` directory into a PAK package.
6. Use FEZRepacker to unpack previously packed PAK file into XNB files. You should have a modified `gomez_house_a.xnb` file.
7. In your mod's `Assets` directory, create `background planes` directory and put your XNB file there.
8. From now on Gomez's house should have your modified texture.

## Creating custom logic mod

Mod loader loads library file given in metadata as an assembly, then attempts to create instances of every public class inheriting from game's `IGameComponent` interface before initialization (before any services are created). After the game has been initialized (that is, as soon as all necessary services are initiated), it adds created instances into the list of game's components and initializes them, allowing their `Update` and `Draw` (use `DrawableGameComponent`) to be properly executed within the game's loop.

In order to create a HAT-compatible library, start by creating an empty C# library project. Then, add `FEZ.exe`, `FezEngine.dll` and all other needed game's dependencies as references - make sure to set "Copy Local" to "False" on all of those references, otherwise you will ship your mod with copies of those files.

Once you have your project done, create a public class inheriting from either `GameComponent` or `DrawableGameComponent` and add your logic there. Once that's done, build it and put it in the mod's directory.

For help, you can see an example of already functioning custom logic mod: [FEZUG](https://github.com/Krzyhau/FEZUG).

## Distributing your mod

Mod loader is capable of loading ZIP archives the same way directories are loaded. Simply pack all contents of your mod's directory into a ZIP file. In order for other people to use it, they simply need to put the archive in the `FEZ/Mods` directory and it should work right off the bat.
