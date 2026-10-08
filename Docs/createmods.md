# Create your own HAT modifications

## Basic mod architecture

Start with [HatModTemplate](https://github.com/FEZModding/HatModTemplate) for a code mod, an asset mod, or a mod containing both. It provides metadata, a sample component, game references, and packaging targets for HAT 3.

Start by creating a mod's directory within `FEZ/Mods` directory. You can name it whatever you'd like, as the mod loader doesn't actually use it for mod identification, but it would be nice if it at least contained the actual mod's name to avoid confusion.

Mod loader expects `Metadata.xml` file in the mod's directory. Create one in a directory you've just made. Its content should look roughly like this:

```xml
<Metadata>
   <Name>YourModName</Name>
   <Description>Short description of your mod.</Description>
   <Author>YourName</Author>
   <Version>1.0</Version>
   <LibraryName>YourModName.dll</LibraryName>
   <Entrypoint>YourModName.ModComponent</Entrypoint>
   <Dependencies>
      <DependencyInfo Name="HAT" MinimumVersion="3.0"/>
   </Dependencies>
</Metadata>
```

`Name` is required and identifies your mod without regard to letter case. If multiple mods have the same name, HAT chooses the one with the highest version.

`Version` is also required. HAT uses .NET's `System.Version`, which accepts two to four numeric components, such as `1.0` or `1.2.3`. Prerelease suffixes such as `beta` are not supported. Omitted components affect comparison: `1.0` sorts below `1.0.0`.

`LibraryName` is used to determine a DLL library with C# assembly the mod loader will load. The library should end with `.dll` extension and should be placed in your mod's directory. This tag is optional, as your mod doesn't have to add any new logic.

`Entrypoint` names the fully qualified component class HAT should create. It must be public, non-abstract, derived from `GameComponent`, and have a public constructor accepting `Game`. If omitted, HAT creates every public, non-abstract `GameComponent` in the assembly.

`Dependencies` lists the mods and minimum versions your mod requires. Code mods must declare a HAT dependency with `MinimumVersion="3.0"` or newer; the installed HAT version must satisfy that requirement. Asset mods may omit the HAT dependency entirely. Declared dependencies are checked and loaded before your mod.

For an asset mod without code, omit `LibraryName` and `Entrypoint` from the metadata.

All other fields are purely informational.

## Creating asset mod

To add assets or override existing ones, create an `Assets` directory within your mod's directory and preserve the game asset paths beneath it. HAT accepts `.xnb` files and uses [FEZRepacker](https://github.com/Krzyhau/FEZRepacker) to convert supported source formats, such as PNG textures, when loading the mod. You can also place an `Assets.pak` file beside `Metadata.xml`.

The template copies `Assets/` and `Assets.pak` when building a code mod. For an asset mod without code, add your assets and run:

```sh
dotnet msbuild -t:PackageAssetsOnly
```

This produces `out-assets/` with metadata and assets, clearing the code-only `LibraryName` and `Entrypoint` fields. It requires the .NET 10 SDK, but no game references or C# compilation.

As an example, here's an instruction on how to change Gomez's house background plane.

1. Use FEZRepacker to unpack game's `Other.pak` archive.
2. Find `background planes/gomez_house_a.png` file and copy it.
3. Edit the image however you'd like.
4. Put the edited PNG at `[Your mod]/Assets/background planes/gomez_house_a.png`.
5. Start FEZ. HAT converts the PNG and replaces the background plane texture.

A small note regarding music files: since they're normally stored in a separate `.pak` archive (`Music.pak`) and handled by a separate subsystem, music files are organized in a root directory. It is **not** the case for HAT mods, and instead it looks for OGG files (audio format used by music in this game) in `[Your mod]/Assets/Music` directory, then uses a path relative to this directory to identify the music file. For example, in order to replace `villageville\bed` music file, your new music file needs to be located at `[Your mod]/Assets/Music/villageville/bed.ogg`.

### Adding custom text

Place `ModText.xnb` in `[Your mod]/Assets/Resources` to add localized text without replacing FEZ's entire text resource. The XNB must contain a `Dictionary<string, Dictionary<string, string>>`: the outer keys are two-letter language codes, and the empty string is the fallback language. For example, the equivalent data for one key is `{ "": { "MY_MOD_INTRO": "Hello!" }, "fr": { "MY_MOD_INTRO": "Bonjour !" } }`.

HAT checks merged ModText entries before FEZ's `StaticText`, `GameText`, and `CreditsText` entries. New keys work as well as overrides of existing keys. If multiple mods define the same key in the same language, the mod loaded later wins. A missing active-language entry falls back to the empty-string entry; if neither exists, FEZ's original lookup runs.

Code mods can call `HatModLoader.Source.Hat.Instance.TextResources.TryGetString("MY_MOD_INTRO", out var dialogue)` to read a custom key. Pass `fallbackOnly: true` to read only the empty-string language. HAT's own menu keys are `HatMods`, `HatChooseWorld`, and `HatNoMods`, which mods can translate or override.

Code mods can register dynamic text through `TextResourceManager`, including mods without text assets:

```csharp
var text = HatModLoader.Source.Hat.Instance.TextResources;
text.Set("MY_MOD_MESSAGE", $"{player} sent you {item}");
DotService.Say("MY_MOD_MESSAGE", true, true);

// MY_MOD_MESSAGE_FORMAT is a localized ModText resource containing
// a template such as "{0} sent you {1}".
text.SetFormatted("MY_MOD_MESSAGE", "MY_MOD_MESSAGE_FORMAT", player, item);
```

Runtime entries take precedence over asset resources; setting the same runtime key again replaces its value. `SetFormatted` resolves its template from merged ModText and HAT resources at lookup time, so language changes and asset reloads apply to existing entries. Raw lookups use the fallback-language template. Templates refer to resource keys, not other runtime entries; a missing template resumes normal lookup of the message key. Arguments use .NET composite formatting, and invalid templates throw `FormatException` during lookup.

Runtime definitions persist for the lifetime of the text manager and survive asset reloads. Update a dynamic string by setting the same key again. Use a mod-specific prefix to avoid collisions, and use distinct keys for messages that may be displayed at the same time.

The legacy `@` literal-text syntax is unsupported. Looking up a tag starting with `@` throws `NotSupportedException`; register the text with `Set` or `SetFormatted` and pass its key instead. Registered text values can still contain `@`.

## Creating custom logic mod

HAT 3 code mods target .NET 10. Code mods built for earlier HAT versions must be rebuilt against the assemblies from a HAT 3 installation and declare the HAT `3.0` dependency in their metadata.

Use [HatModTemplate](https://github.com/FEZModding/HatModTemplate):

1. Install HAT 3 into FEZ and install the .NET 10 SDK.
2. Create a repository from the template. Rename `HatModTemplate.csproj`, the namespace in `ModComponent.cs`, and the matching `Name`, `LibraryName`, and `Entrypoint` values in `Metadata.xml`.
3. Copy `UserProperties.xml.template` to `UserProperties.xml`. Set `FezDir` to the installed game's directory containing `HAT.dll`, `FezEngine.dll`, and `FNA.dll`. The local configuration file is ignored by Git.
4. Add your logic to `ModComponent`, which derives from `GameComponent`. Use `DrawableGameComponent` when your component needs to draw.
5. Build or publish from the repository root:

```sh
dotnet build -c Debug
dotnet publish -c Release -p:ContinuousIntegrationBuild=true
```

Both commands copy the mod DLL, its `.deps.json`, metadata, and assets to `ModOutputDir` configured in `UserProperties.xml`, or `out/` if unset. Copy that folder's contents to a directory under `FEZ/Mods`, or configure `ModOutputDir` to build there directly. Restart FEZ to load code changes.

The template references the installed game's `HAT.dll`, `FezEngine.dll`, `FNA.dll`, and other game dependencies with `<Private>false</Private>`. Use those same installed assemblies when adding references so your mod matches the game's assembly versions, including FNA.

HAT creates the entrypoint component before the game's services are initialized, then injects it during component loading. Access services during component initialization or updates rather than in the constructor. To add more components, create them through `ServiceHelper.AddComponent`, or omit `Entrypoint` to let HAT discover all public components.

For help, you can see an example of already functioning custom logic mod: [FEZUG](https://github.com/Krzyhau/FEZUG).

## Distributing your mod

HAT loads ZIP archives as well as directories. Pack the contents of your output folder with `Metadata.xml` at the ZIP root. Users can put the archive directly in `FEZ/Mods`.

For code mods, include the mod DLL, its `.deps.json`, required third-party DLLs, and any native libraries in their expected `runtimes/` paths, along with metadata and assets. Keep game and HAT assemblies out of the package. Clear stale output files before packaging an updated mod; the template's copy targets do not remove deleted files.
