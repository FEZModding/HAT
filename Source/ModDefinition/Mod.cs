using Common;
using FezEngine.Tools;
using HatModLoader.Source.Assets;
using HatModLoader.Source.FileProxies;
using HatModLoader.Source.AssemblyResolving;
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
        
        private IAssemblyResolver _internalAssemblyResolver;

        public byte[] RawAssembly { get; private set; }
        public Assembly Assembly { get; private set; }
        public ModMetadata Info { get; private set; }
        public IFileProxy FileProxy { get; private set; }
        public List<ModDependency> Dependencies { get; private set; }
        public List<Asset> Assets { get; private set; }
        public List<GameComponent> Components { get; private set; }

        public bool IsAssetMod => Assets.Count > 0;
        public bool IsCodeMod => RawAssembly != null;

        public Mod(Hat modLoader, IFileProxy fileProxy)
        {
            ModLoader = modLoader;

            RawAssembly = null;
            Assembly = null;
            Assets = new List<Asset>();
            Components = new List<GameComponent>();
            Dependencies = new List<ModDependency>();
            FileProxy = fileProxy;
        }

        public void InitializeComponents()
        {
            if (RawAssembly == null || Assembly == null) return;

            foreach (Type type in Assembly.GetExportedTypes())
            {
                if (!typeof(GameComponent).IsAssignableFrom(type) || !type.IsPublic || type.IsAbstract) continue;
                //Note: The constructor accepting the type (Game) is defined in GameComponent
                var gameComponent = (GameComponent)Activator.CreateInstance(type, new object[] { ModLoader.Game });
                Components.Add(gameComponent);
            }

            if (Components.Count > 0)
            {
                var countText = $"{Components.Count} component{(Components.Count != 1 ? "s" : "")}";
                Logger.Log("HAT", $"Initialized {countText} in mod \"{Info.Name}\"");
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
            _internalAssemblyResolver = new ModInternalAssemblyResolver(this);
            AssemblyResolverRegistry.Register(_internalAssemblyResolver);
            
            if (RawAssembly != null)
            {
                Assembly = Assembly.Load(RawAssembly);
            }
        }

        public void Dispose()
        {
            // TODO: dispose assets

            foreach (var component in Components)
            {
                ServiceHelper.RemoveComponent(component);
            }
            
            AssemblyResolverRegistry.Unregister(_internalAssemblyResolver);
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

        public static bool TryLoad(Hat modLoader, IFileProxy fileProxy, out Mod mod)
        {
            mod = new Mod(modLoader, fileProxy);

            if (!mod.TryLoadMetadata()) return false;

            mod.TryLoadAssets();
            mod.TryLoadAssembly();

            return mod.IsAssetMod || mod.IsCodeMod;
        }

        public static string GetModsDirectory()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModsDirectoryName);
        }

        private bool TryLoadMetadata()
        {
            if (!FileProxy.FileExists(ModMetadataFileName))
            {
                return false;
            }

            using var metadataStream = FileProxy.OpenFile(ModMetadataFileName);
            if (!ModMetadata.TryLoadFrom(metadataStream, out var metadata))
            {
                return false;
            }

            Info = metadata;

            return true;
        }

        private bool TryLoadAssets()
        {
            var files = new Dictionary<string, Stream>();

            foreach (var filePath in FileProxy.EnumerateFiles(AssetsDirectoryName))
            {
                var relativePath = filePath.Substring(AssetsDirectoryName.Length + 1).Replace("/", "\\").ToLower();
                var fileStream = FileProxy.OpenFile(filePath);
                files.Add(relativePath, fileStream);
            }

            Assets = AssetLoaderHelper.GetListFromFileDictionary(files);

            var pakPackagePath = AssetsDirectoryName + ".pak";
            if (FileProxy.FileExists(pakPackagePath))
            {
                using var pakPackage = FileProxy.OpenFile(pakPackagePath);
                Assets.AddRange(AssetLoaderHelper.LoadPakPackage(pakPackage));
            }

            return Assets.Count > 0;
        }

        private bool TryLoadAssembly()
        {
            if(!IsLibraryNameValid()) return false;

            if (!FileProxy.FileExists(Info.LibraryName)) return false;

            using var assemblyStream = FileProxy.OpenFile(Info.LibraryName);
            RawAssembly = new byte[assemblyStream.Length];
            assemblyStream.Read(RawAssembly, 0, RawAssembly.Length);

            return true;
        }

        private bool IsLibraryNameValid()
        {
            var libraryName = Info.LibraryName;
            return libraryName != null && libraryName.Length > 0 && libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        }
    }
}
