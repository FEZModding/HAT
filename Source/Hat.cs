using Common;
using FezGame;

namespace HatModLoader.Source
{
    public class Hat
    {
        public static Hat Instance;

        public Fez Game;
        public List<Mod> Mods;
        public List<Mod> InvalidMods;

        public static string Version
        {
            get
            {
                const string version = "1.1.1";
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

            Mods = new List<Mod>();
            InvalidMods = new List<Mod>();

            Logger.Log("HAT", $"HAT Mod Loader {Version}");
            PrepareMods();
        }

        public void PrepareMods()
        {
            Mods.Clear();

            LoadMods();

            if(Mods.Count == 0)
            {
                Logger.Log("HAT", $"No mods have been found in the directory.");
                return;
            }

            RemoveOlderDuplicates();
            InitializeDependencies();
            FilterOutInvalidMods();
            SortModsBasedOnDependencies();

            int codeModsCount = Mods.Count(mod => mod.IsCodeMod);
            int assetModsCount = Mods.Count(mod => mod.IsAssetMod);

            var modsText = $"{Mods.Count} mod{(Mods.Count != 1 ? "s" : "")}";
            var codeModsText = $"{codeModsCount} code mod{(codeModsCount != 1 ? "s" : "")}";
            var assetModsText = $"{assetModsCount} asset mod{(assetModsCount != 1 ? "s" : "")}";

            Logger.Log("HAT", $"Successfully loaded {modsText} ({codeModsText} and {assetModsText})");

            Logger.Log("HAT", $"Mods in their order of appearance:");

            foreach(var mod in Mods)
            {
                Logger.Log("HAT", $"  {mod.Info.Name} by {mod.Info.Author} version {mod.Info.Version}");
            }
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

            var modLoaders = PrepareModLoaders();

            foreach (var modLoader in modLoaders)
            {
                bool loadingState = Mod.TryLoad(this, modLoader, out Mod mod);
                if (loadingState)
                {
                    Mods.Add(mod);
                }
                LogModLoadingState(mod, loadingState);
            }
        }

        private static List<IModLoader> PrepareModLoaders()
        {
            var modList = new List<IModLoader>();

            var directoryBasedMods = Directory.GetDirectories(Mod.ModsDirectoryName)
                .Select(path => new DirectoryModLoader(path));
            modList.AddRange(directoryBasedMods);

            var zipBasedMods = Directory.GetFiles(Mod.ModsDirectoryName)
                .Where(file => Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                .Select(file => new ZipModLoader(file));
            modList.AddRange(zipBasedMods);

            return modList;
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
                string containerType = mod.IsZip ? "archive" : "directory";
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

        private void RemoveOlderDuplicates()
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

        private void InitializeDependencies()
        {
            foreach (var mod in Mods)
            {
                mod.InitializeDependencies();
            }

            FinalizeDependencies();
        }

        private void FinalizeDependencies()
        {
            for(int i=0;i<=Mods.Count; i++)
            {
                if(i == Mods.Count)
                {
                    // there's no possible way to have more dependency nesting levels than the mod count. Escape!
                    throw new ApplicationException("Stuck in a mod dependency finalization loop!");
                }

                bool noInvalidMods = true;
                foreach (var mod in Mods)
                {
                    if (mod.TryFinalizeDependencies()) continue;

                    noInvalidMods = false;
                }
                if (noInvalidMods)
                {
                    break;
                }
            }
        }

        private void FilterOutInvalidMods()
        {
            InvalidMods = Mods.Where(mod => !mod.AreDependenciesValid()).ToList();
            foreach (var invalidMod in InvalidMods)
            {
                LogIssuesWithInvalidMod(invalidMod);
                Mods.Remove(invalidMod);
            }
        }

        private void LogIssuesWithInvalidMod(Mod invalidMod)
        {
            var delegateIssues = invalidMod.Dependencies
                    .Where(dep => dep.Status != ModDependencyStatus.Valid)
                    .Select(dependency => $"{dependency.Info.Name} ({dependency.GetStatusString()})")
                    .ToList();

            string error = $"Dependency issues in mod {invalidMod.Info.Name} found: {string.Join(", ", delegateIssues)}";

            Logger.Log("HAT", LogSeverity.Warning, error);
        }

        public void SortModsBasedOnDependencies()
        {
            Mods.Sort((a, b) =>
            {
                if (a.Dependencies.Where(d => d.Instance == b).Any()) return 1;
                if (b.Dependencies.Where(d => d.Instance == a).Any()) return -1;
                return 0;
            });
        }

        public void InitalizeAssemblies()
        {
            foreach (var mod in Mods)
            {
                mod.InitializeAssembly();
            }
            foreach (var mod in Mods)
            {
                mod.InitializeComponents();
            }
            Logger.Log("HAT", "Assembly initialization completed!");
        }

        public List<Asset> GetFullAssetList()
        {
            var list = new List<Asset>();

            foreach (var mod in Mods)
            {
                list.AddRange(mod.Assets);
            }

            return list;
        }

        public void InitalizeComponents()
        {
            foreach(var mod in Mods)
            {
                mod.InjectComponents();
            }
            Logger.Log("HAT", "Component initialization completed!");
        }

    }
}
