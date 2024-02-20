using System.Linq;

namespace HatModLoader.Source
{
    [Serializable]
    public struct ModDependency
    {
        public ModDependencyInfo Info;
        public Mod Instance;
        public ModDependencyStatus Status;
        public bool IsModLoaderDependency => Info.Name == "HAT";
        public bool IsFinalized => Status != ModDependencyStatus.None;
        public string DetectedVersion => IsModLoaderDependency ? Hat.Version : (Instance != null ? Instance.Info.Version : null);


        public ModDependency(ModDependencyInfo info, Mod instance)
        {
            Info = info;
            Instance = instance;
            Status = ModDependencyStatus.None;

            Initialize();
        }
        public void Initialize()
        {
            if (IsModLoaderDependency || Instance != null)
            {
                if (ModMetadata.CompareVersions(DetectedVersion, Info.MinimumVersion) < 0)
                {
                    Status = ModDependencyStatus.InvalidVersion;
                }
                else
                {
                    Status = ModDependencyStatus.Valid;
                }
            }

            if (!IsModLoaderDependency)
            {
                if (Instance == null)
                {
                    Status = ModDependencyStatus.InvalidNotFound;
                }
                else if (Instance.AreDependenciesValid())
                {
                    Status = ModDependencyStatus.Valid;
                }

                if (IsRecursive())
                {
                    Status = ModDependencyStatus.InvalidRecursive;
                }
            }
        }

        public bool TryFinalize()
        {
            if (IsModLoaderDependency) return true;

            if (!Instance.AreDependenciesFinalized()) return false;

            Status =
                Instance.AreDependenciesValid()
                ? ModDependencyStatus.Valid
                : ModDependencyStatus.InvalidDependencyTree;

            return true;
        }

        public bool IsRecursive()
        {
            var currentModQueue = new List<Mod>() { Instance };

            var iterationsCount = Instance.ModLoader.Mods.Count();

            while (currentModQueue.Count > 0)
            {
                var newDependencyMods = currentModQueue.SelectMany(mod => mod.Dependencies).Select(dep => dep.Instance).ToList();
                if (newDependencyMods.Contains(Instance))
                {
                    return true;
                }

                currentModQueue = newDependencyMods;

                iterationsCount--;

                if (iterationsCount <= 0)
                {
                    break;
                }
            }

            return false;
        }

        public string GetStatusString()
        {
            return Status switch
            {
                ModDependencyStatus.Valid => $"valid",
                ModDependencyStatus.InvalidVersion => $"needs version >={Info.MinimumVersion}, found {DetectedVersion}",
                ModDependencyStatus.InvalidNotFound => $"not found",
                ModDependencyStatus.InvalidRecursive => $"recursive dependency - consider merging mods or separating it into modules",
                ModDependencyStatus.InvalidDependencyTree => $"couldn't load its own dependencies",
                _ => "unknown"
            };
        }

    }
}
