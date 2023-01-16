using Common;
using FezGame;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
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

            int codeModsCount = Mods.Count(mod => mod.IsCodeMod);
            int assetModsCount = Mods.Count(mod => mod.IsAssetMod);

            Logger.Log("HAT", $"Successfully loaded {Mods.Count} mods ({codeModsCount} code mods and {assetModsCount} asset mods)");
        }

        private void LogModLoadingState(Mod mod, bool loadState)
        {
            if (loadState)
            {
                var libraryInfo = "no library";
                if (mod.IsCodeMod)
                {
                    libraryInfo = $"library \"{mod.Info.LibraryName}\" ({mod.Components.Count} components)";
                }
                Logger.Log("HAT", $"Loaded mod \"{mod.Info.Name}\" with {mod.Assets.Count} assets and {libraryInfo}.");
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
        
        public void Initalize()
        {
            foreach(var mod in Mods)
            {
                mod.Initialize();
            }
            Logger.Log("HAT", "Mods initialization completed!");
        }

    }
}
