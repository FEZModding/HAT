using FezEngine.Tools;
using HatModLoader.Source.Assets;
using HatModLoader.Source.ModLoaders;
using Microsoft.Xna.Framework;
using System.Reflection;

namespace HatModLoader.Source.ModDefinition
{
    public class Mod : IDisposable
    {
        public static readonly string ModsDirectoryName = "Mods";

        public static readonly string AssetsDirectoryName = "Assets";
        public static readonly string ModMetadataFileName = "Metadata.xml";

        public Hat ModLoader;

        public byte[] RawAssembly { get; private set; }
        public Assembly Assembly { get; private set; }
        public ModMetadata Info { get; private set; }
        public string DirectoryName { get; private set; }
        public List<ModDependency> Dependencies { get; private set; }
        public bool IsZip { get; private set; }
        public List<Asset> Assets { get; private set; }
        public List<IGameComponent> Components { get; private set; }

        public bool IsAssetMod => Assets.Count > 0;
        public bool IsCodeMod => RawAssembly != null;

        public Mod(Hat modLoader)
        {
            ModLoader = modLoader;

            RawAssembly = null;
            Assembly = null;
            Assets = new List<Asset>();
            Components = new List<IGameComponent>();
            Dependencies = new List<ModDependency>();
        }

        public void InitializeComponents()
        {
            if (RawAssembly == null || Assembly == null) return;

            foreach (Type type in Assembly.GetExportedTypes())
            {
                if (!typeof(IGameComponent).IsAssignableFrom(type) || !type.IsPublic) continue;
                var gameComponent = (IGameComponent)Activator.CreateInstance(type, new object[] { ModLoader.Game });
                Components.Add(gameComponent);
            }
        }

        public void InjectComponents()
        {
            foreach (var component in Components)
            {
                ServiceHelper.AddComponent(component);
            }
        }

        public void InitializeAssembly()
        {
            if (RawAssembly == null) return;
            Assembly = Assembly.Load(RawAssembly);
        }

        public void Dispose()
        {
            // TODO: dispose assets

            foreach (var component in Components)
            {
                ServiceHelper.RemoveComponent(component);
            }
        }

        public int CompareVersionsWith(Mod mod)
        {
            return ModMetadata.CompareVersions(Info.Version, mod.Info.Version);
        }

        public void InitializeDependencies()
        {
            if (Info.Dependencies == null || Info.Dependencies.Count() == 0) return;
            if (Dependencies.Count() == Info.Dependencies.Length) return;

            Dependencies.Clear();
            foreach (var dependencyInfo in Info.Dependencies)
            {
                var matchingMod = ModLoader.Mods.FirstOrDefault(mod => mod.Info.Name == dependencyInfo.Name);
                var dependency = new ModDependency(dependencyInfo, matchingMod);
                Dependencies.Add(dependency);
            }
        }

        public bool TryFinalizeDependencies()
        {
            foreach (var dependency in Dependencies)
            {
                if (dependency.TryFinalize()) continue;
                else return false;
            }
            return true;
        }

        public bool AreDependenciesFinalized()
        {
            return Dependencies.All(dependency => dependency.IsFinalized);
        }

        public bool AreDependenciesValid()
        {
            if (Info.Dependencies == null) return true; // if mod has no dependencies, they are "valid"
            if (Info.Dependencies.Count() != Dependencies.Count()) return false;

            return Dependencies.All(dependency => dependency.Status == ModDependencyStatus.Valid);
        }

        public static bool TryLoad(Hat modLoader, IModLoader loader, out Mod mod)
        {
            mod = new Mod(modLoader)
            {
                DirectoryName = loader.ResourcePath,
                IsZip = loader is ZipModLoader
            };

            if (!loader.TryLoadMetadata(out var metadata))
            {
                return false;
            }

            mod.Info = metadata;
            mod.Assets = loader.LoadAssets();

            if (mod.IsLibraryNameValid() && loader.TryLoadAssembly(mod.Info.LibraryName, out var assemblyData))
            {
                mod.RawAssembly = assemblyData;
            }

            return mod.IsAssetMod || mod.IsCodeMod;
        }

        public static string GetModsDirectory()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModsDirectoryName);
        }

        private bool IsLibraryNameValid()
        {
            var libraryName = Info.LibraryName;
            return libraryName != null && libraryName.Length > 0 && libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        }
    }
}
