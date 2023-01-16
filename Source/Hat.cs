using Common;
using FezGame;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HatModLoader.Source
{
    public class Hat
    {
        public static Hat Instance;

        public Fez Game;
        public List<Mod> Mods;

        public static string Version
        {
            get
            {
                const string version = "1.0";
#if DEBUG
                return $"v{version}-dev";
#else
                return $"v{version}";
#endif
            }
        }


        public Hat(Fez fez)
        {
            Instance = this;
            Game = fez;

            Logger.Log("HAT", $"HAT Mod Loader {Version}");
            LoadMods();
            
        }

        public void LoadMods()
        {
            Mods = new List<Mod>();

            // check if directory is there
            if (!Directory.Exists(Mod.GetModsDirectory()))
            {
                Logger.Log("HAT", LogSeverity.Warning, "Main mods directory not found. Creating and skipping mod loading process...");
                Directory.CreateDirectory(Mod.GetModsDirectory());
                return;
            }

            // load mods in directories
            foreach(var modDir in Mod.GetModDirectories())
            {
                bool loadingState = Mod.TryLoadFromDirectory(modDir, out Mod mod);
                if (loadingState)
                {
                    Mods.Add(mod);
                }
                LogModLoadingState(mod, loadingState);
            }

            // load mods packed into archives
            foreach (var modZip in Mod.GetModArchives())
            {
                bool loadingState = Mod.TryLoadFromZip(modZip, out Mod mod);
                if (loadingState)
                {
                    Mods.Add(mod);
                }
                LogModLoadingState(mod, loadingState);
            }

            // verify and remove duplicates
            var uniqueNames = Mods.Select(mod => mod.Info.Name).Distinct().ToList();
            foreach(var modName in uniqueNames)
            {
                var sameNamedMods = Mods.Where(mod => mod.Info.Name == modName).ToList();
                if(sameNamedMods.Count() > 1)
                {
                    sameNamedMods.Sort((mod1, mod2) => mod2.CompareVersions(mod1));
                    var newestMod = sameNamedMods.First();
                    Logger.Log("HAT", LogSeverity.Warning, $"Multiple instances of mod {modName} detected! Leaving only the newest version ({newestMod.Info.Version})");

                    foreach(var mod in sameNamedMods)
                    {
                        if (mod == newestMod) continue;
                        Mods.Remove(mod);
                    }
                }
            }

            int codeModsCount = Mods.Count(mod => mod.IsCodeMod);
            int assetModsCount = Mods.Count(mod => mod.IsAssetMod);

            var modsText = $"{Mods.Count} mod{(Mods.Count != 1 ? "s" : "")}";
            var codeModsText = $"{codeModsCount} code mod{(codeModsCount != 1 ? "s" : "")}";
            var assetModsText = $"{assetModsCount} asset mod{(assetModsCount != 1 ? "s" : "")}";

            Logger.Log("HAT", $"Successfully loaded {modsText} ({codeModsText} and {assetModsText})");
        }

        private void LogModLoadingState(Mod mod, bool loadState)
        {
            if (loadState)
            {
                var libraryInfo = "no library";
                if (mod.IsCodeMod)
                {
                    var componentsText = $"{mod.Components.Count} component{(mod.Components.Count != 1 ? "s" : "")}";
                    libraryInfo = $"library \"{mod.Info.LibraryName}\" ({componentsText})";
                }
                var assetsText = $"{mod.Assets.Count} asset{(mod.Assets.Count != 1 ? "s" : "")}";
                Logger.Log("HAT", $"Loaded mod \"{mod.Info.Name}\" ver. {mod.Info.Version} by {mod.Info.Author} ({assetsText} and {libraryInfo})");
            }
            else
            {
                string containerType = mod.IsZip ? "directory" : "archive";
                if (mod.Info.Name == null)
                {
                    Logger.Log("HAT", LogSeverity.Warning, $"Mod {containerType} \"{mod.DirectoryName}\" does not have a valid metadata file.");
                }
                else if (mod.Info.LibraryName.Length > 0 && !mod.IsCodeMod)
                {
                    var info = $"Mod \"{mod.Info.Name}\" has library name defined (\"{mod.Info.LibraryName}\"), but no such library was found in mod {containerType}.";
                    Logger.Log("HAT", LogSeverity.Warning, info);
                }
                else if (!mod.IsCodeMod && !mod.IsAssetMod)
                {
                    Logger.Log("HAT", LogSeverity.Warning, $"Mod \"{mod.Info.Name}\" is empty and will not be added.");
                }
            }
        }

        public void InitalizeAssemblies()
        {
            foreach (var mod in Mods)
            {
                mod.InitializeAssembly();
            }
            Logger.Log("HAT", "Assembly initialization completed!");
        }

        public void InitializeAssets()
        {
            foreach (var mod in Mods)
            {
                mod.InitializeAssets();
            }
            Logger.Log("HAT", "Asset injection completed!");
        }

        public void InitalizeComponents()
        {
            foreach(var mod in Mods)
            {
                mod.InitializeComponents();
            }
            Logger.Log("HAT", "Component initialization completed!");
        }

    }
}
