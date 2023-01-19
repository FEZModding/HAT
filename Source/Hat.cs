using Common;
using FezGame;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static HatModLoader.Source.Mod;

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
                return $"{version}-dev";
#else
                return $"{version}";
#endif
            }
        }


        public Hat(Fez fez)
        {
            Instance = this;
            Game = fez;

            Logger.Log("HAT", $"HAT Mod Loader {Version}");
            PrepareMods();
        }

        public void PrepareMods()
        {
            Mods = new List<Mod>();

            LoadMods();

            if(Mods.Count == 0)
            {
                Logger.Log("HAT", $"No mods have been found in the directory.");
                return;
            }

            RemoveDuplicates();
            InitializeAndVerifyDependencies();

            int codeModsCount = Mods.Count(mod => mod.IsCodeMod);
            int assetModsCount = Mods.Count(mod => mod.IsAssetMod);

            var modsText = $"{Mods.Count} mod{(Mods.Count != 1 ? "s" : "")}";
            var codeModsText = $"{codeModsCount} code mod{(codeModsCount != 1 ? "s" : "")}";
            var assetModsText = $"{assetModsCount} asset mod{(assetModsCount != 1 ? "s" : "")}";

            Logger.Log("HAT", $"Successfully loaded {modsText} ({codeModsText} and {assetModsText})");
        }

        private void EnsureModDirectory()
        {
            if (!Directory.Exists(Mod.GetModsDirectory()))
            {
                Logger.Log("HAT", LogSeverity.Warning, "Main mods directory not found. Creating and skipping mod loading process...");
                Directory.CreateDirectory(Mod.GetModsDirectory());
                return;
            }
        }

        private void LoadMods()
        {
            EnsureModDirectory();

            // load mods in directories
            foreach (var modDir in Mod.GetModDirectories())
            {
                bool loadingState = Mod.TryLoadFromDirectory(this, modDir, out Mod mod);
                if (loadingState)
                {
                    Mods.Add(mod);
                }
                LogModLoadingState(mod, loadingState);
            }

            // load mods packed into archives
            foreach (var modZip in Mod.GetModArchives())
            {
                bool loadingState = Mod.TryLoadFromZip(this, modZip, out Mod mod);
                if (loadingState)
                {
                    Mods.Add(mod);
                }
                LogModLoadingState(mod, loadingState);
            }
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

        private void RemoveDuplicates()
        {
            var uniqueNames = Mods.Select(mod => mod.Info.Name).Distinct().ToList();
            foreach (var modName in uniqueNames)
            {
                var sameNamedMods = Mods.Where(mod => mod.Info.Name == modName).ToList();
                if (sameNamedMods.Count() > 1)
                {
                    sameNamedMods.Sort((mod1, mod2) => mod2.CompareVersionsWith(mod1));
                    var newestMod = sameNamedMods.First();
                    Logger.Log("HAT", LogSeverity.Warning, $"Multiple instances of mod {modName} detected! Leaving only the newest version ({newestMod.Info.Version})");

                    foreach (var mod in sameNamedMods)
                    {
                        if (mod == newestMod) continue;
                        Mods.Remove(mod);
                    }
                }
            }
        }

        private void InitializeAndVerifyDependencies()
        {
            foreach (var mod in Mods)
            {
                mod.InitializeDependencies();
            }

            var invalidMods = Mods.Where(mod => !mod.AreDependenciesValid()).ToList();
            foreach (var invalidMod in invalidMods)
            {
                var delegateIssues = invalidMod.Dependencies
                    .Where(dep=>dep.Status != DependencyStatus.Valid)
                    .Select(delegate (Dependency dependency)
                    {
                        string issue = "unknown";
                        switch (dependency.Status)
                        {
                            case DependencyStatus.InvalidVersion:
                                issue = $"needs version >={dependency.Info.MinimumVersion}, found {dependency.DetectedVersion}";
                                break;
                            case DependencyStatus.InvalidNotFound:
                                issue = $"not found";
                                break;
                            case DependencyStatus.InvalidRecursive:
                                issue = $"recursive dependency - consider merging mods";
                                break;
                        }

                        return $"{dependency.Info.Name} ({issue})";
                    }).ToList();

                string error = $"Dependency issues in mod {invalidMod.Info.Name} found: {string.Join(", ", delegateIssues)}";

                Logger.Log("HAT", LogSeverity.Warning, error);

                Mods.Remove(invalidMod);
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
