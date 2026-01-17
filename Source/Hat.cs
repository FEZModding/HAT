using System.Xml.Serialization;
using Common;
using FezGame;
using HatModLoader.Source.AssemblyResolving;
using HatModLoader.Source.Assets;
using HatModLoader.Source.FileProxies;
using HatModLoader.Source.ModDefinition;

namespace HatModLoader.Source
{
    public class Hat
    {
        public static readonly Version Version = new("1.2.1");

        private static readonly string ModsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");

        private const string ModMetadataFile = "Metadata.xml";

        private const string AssetDirectoryName = "Assets";

        private const string AssetPakName = AssetDirectoryName + ".pak";

        private static readonly IList<string> IgnoredModNames = InitializeIgnoredModsList();

        private static readonly IList<string> PriorityModNames = InitializePriorityList();

        private List<AssetMod> _assetMods;

        private List<CodeMod> _codeMods;
    
        private List<IMod> _mods;

        public IList<IMod> Mods
        {
            get
            {
                _mods ??= _assetMods.Concat<IMod>(_codeMods)
                    .GroupBy(p => p.FileProxy)
                    .Select(g => g.First())
                    .ToList();
                return _mods;
            }
        }

        public int InvalidModsCount { get; private set; }

        public static string VersionString
        {
            get
            {
#if DEBUG
                return $"{Version}-dev";
#else
                return Version.ToString();
#endif
            }
        }

        public static Hat Instance { get; private set; }

        private readonly Fez _fezGame;

        public Hat(Fez fez)
        {
            Instance = this;
            _fezGame = fez;
            Initialize();
        }

        private void Initialize()
        {
            Logger.Log("HAT", $"HAT Mod Loader {VersionString}");

            #region Load file proxies

            if (!Directory.Exists(ModsDirectory))
            {
                Logger.Log("HAT", LogSeverity.Warning, 
                    "Main mods directory not found. Creating and skipping mod loading process...");
                Directory.CreateDirectory(ModsDirectory);
            }

            var proxies = new IEnumerable<IFileProxy>[]
                {
                    DirectoryFileProxy.EnumerateInDirectory(ModsDirectory),
                    ZipFileProxy.EnumerateInDirectory(ModsDirectory),
                }
                .SelectMany(x => x);

            #endregion

            #region Load metadata and check them against blacklist

            var metas = new Dictionary<IFileProxy, Metadata>();
            foreach (var proxy in proxies)
            {
                if (IgnoredModNames.Contains(proxy.ContainerName))
                {
                    continue;
                }

                if (TryLoadMetadata(proxy, out var metadata))
                {
                    metas.Add(proxy, metadata);
                }
            }

            #endregion

            #region Load asset mods first and sort them against priority list

            _assetMods = [];
            foreach (var meta in metas)
            {
                if (TryLoadAssets(meta.Key, meta.Value, out var assetMod))
                {
                    _assetMods.Add(assetMod);
                }
            }

            _assetMods.Sort((mod1, mod2) =>
            {
                var priorityIndex1 = GetPriorityIndex(mod1.FileProxy);
                var priorityIndex2 = GetPriorityIndex(mod2.FileProxy);
                return priorityIndex1.CompareTo(priorityIndex2);
            });

            #endregion

            #region Build dependency graph for code mods and load them

            var codeMods = new List<CodeMod>();
            foreach (var meta in metas)
            {
                if (TryLoadAssembly(meta.Key, meta.Value, out var codeMod))
                {
                    codeMods.Add(codeMod);
                }
            }

            var resolverResult = ModDependencyResolver.Resolve(codeMods);
            _codeMods = resolverResult.LoadOrder;
            InvalidModsCount = resolverResult.Invalid.Count;

            #endregion

            #region Log initialization result

            foreach (var node in resolverResult.Invalid)
            {
                Logger.Log("HAT", $"Mod '{node.Mod.Metadata.Name}' is invalid: {node.GetStatusText()}");
            }
        
            var modsText = $"{Mods.Count} mod{(Mods.Count != 1 ? "s" : "")}";
            var codeModsText = $"{_codeMods.Count} code mod{(_codeMods.Count != 1 ? "s" : "")}";
            var assetModsText = $"{_assetMods.Count} asset mod{(_assetMods.Count != 1 ? "s" : "")}";
            Logger.Log("HAT", $"Successfully loaded {modsText} ({codeModsText} and {assetModsText})");
        
            Logger.Log("HAT", "Mods in their order of appearance:");
            foreach (var mod in Mods)
            {
                Logger.Log("HAT", $"  {mod.Metadata.Name} by {mod.Metadata.Author} version {mod.Metadata.Version}");
            }
        
            #endregion
        }

        public void InitializeAssemblies()
        {
            foreach (var mod in _codeMods)
            {
                mod.Initialize(_fezGame);
            }
        }

        public void InitializeComponents()
        {
            foreach (var mod in _codeMods)
            {
                mod.InjectComponents();
            }
        }
    
        public List<Asset> GetFullAssetList()
        {
            return _assetMods.SelectMany(x => x.Assets).ToList();
        }

        public static void RegisterRequiredDependencyResolvers()
        {
            AssemblyResolverRegistry.Register(new HatSubdirectoryAssemblyResolver("MonoMod"));
            AssemblyResolverRegistry.Register(new HatSubdirectoryAssemblyResolver("FEZRepacker.Core"));
        }

        private static bool TryLoadMetadata(IFileProxy proxy, out Metadata metadata)
        {
            if (!proxy.FileExists(ModMetadataFile))
            {
                metadata = default;
                return false;
            }

            try
            {
                using var stream = proxy.OpenFile(ModMetadataFile);
                using var reader = new StreamReader(stream);

                var serializer = new XmlSerializer(typeof(Metadata));
                metadata = (Metadata)serializer.Deserialize(reader);
                if (string.IsNullOrEmpty(metadata.Name) || metadata.Version == null)
                {
                    metadata = default;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("HAT", LogSeverity.Warning, $"Failed to load mod metadata: {ex.Message}");
                metadata = default;
                return false;
            }
        }

        private static bool TryLoadAssets(IFileProxy proxy, Metadata metadata, out AssetMod assetMod)
        {
            var files = new Dictionary<string, Stream>();
            foreach (var filePath in proxy.EnumerateFiles(AssetDirectoryName))
            {
                var relativePath = filePath.Substring(AssetDirectoryName.Length + 1).Replace("/", "\\").ToLower();
                var fileStream = proxy.OpenFile(filePath);
                files.Add(relativePath, fileStream);
            }

            var assets = AssetLoaderHelper.GetListFromFileDictionary(files);
            if (proxy.FileExists(AssetPakName))
            {
                using var pakPackage = proxy.OpenFile(AssetPakName);
                assets.AddRange(AssetLoaderHelper.LoadPakPackage(pakPackage));
            }

            if (assets.Count < 1)
            {
                assetMod = null;
                return false;
            }

            assetMod = new AssetMod(proxy, metadata, assets);
            return true;
        }

        private static bool TryLoadAssembly(IFileProxy proxy, Metadata metadata, out CodeMod codeMod)
        {
            if (string.IsNullOrEmpty(metadata.LibraryName) ||
                !metadata.LibraryName.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase) ||
                !proxy.FileExists(metadata.LibraryName))
            {
                codeMod = null;
                return false;
            }

            using var assemblyStream = proxy.OpenFile(metadata.LibraryName);
            var rawAssembly = new byte[assemblyStream.Length];
            var count = assemblyStream.Read(rawAssembly, 0, rawAssembly.Length);

            if (rawAssembly.Length != count)
            {
                codeMod = null;
                return false;
            }

            codeMod = new CodeMod(proxy, metadata, rawAssembly);
            return true;
        }

        private static IList<string> InitializeIgnoredModsList()
        {
            var ignoredModsNamesFilePath = Path.Combine(ModsDirectory, "ignorelist.txt");
            const string defaultContent =
                "# List of directories and zip archives to ignore when loading mods, one per line.\n" +
                "# Lines starting with # will be ignored.\n\n" +
                "ExampleDirectoryModName\n" +
                "ExampleZipPackageName.zip\n";
            return ModsTextListLoader.LoadOrCreateDefault(ignoredModsNamesFilePath, defaultContent);
        }

        private static IList<string> InitializePriorityList()
        {
            var priorityListFilePath = Path.Combine(ModsDirectory, "prioritylist.txt");
            const string defaultContent = "# List of directories and zip archives to prioritize during mod loading.\n" +
                                          "# If present on this list, the mod will be loaded before other mods not listed here or listed below it,\n" +
                                          "# including newer versions of the same mod. However, it does not override dependency ordering.\n" +
                                          "# Lines starting with # will be ignored.\n\n" +
                                          "ExampleDirectoryModName\n" +
                                          "ExampleZipPackageName.zip\n";
            return ModsTextListLoader.LoadOrCreateDefault(priorityListFilePath, defaultContent);
        }

        private static int GetPriorityIndex(IFileProxy proxy)
        {
            var index = PriorityModNames.IndexOf(proxy.ContainerName);
            if (index == -1) index = int.MaxValue;
            return index;
        }
    }
}