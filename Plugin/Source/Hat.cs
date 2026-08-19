using System.Reflection;
using Common;
using FezEngine.Tools;
using FezGame;
using FezGame.Services;
using HatModLoader.Source.AssemblyResolving;
using HatModLoader.Source.Assets;
using HatModLoader.Source.FileProxies;
using HatModLoader.Source.Languages;
using HatModLoader.Source.ModDefinition;
using HatModLoader.Source.Worlds;

namespace HatModLoader.Source
{
    public class Hat
    {
        private static readonly string ModsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");

        private static readonly IList<string> IgnoredModNames = InitializeIgnoredModsList();

        private static readonly IList<string> PriorityModNames = InitializePriorityList();

        public List<ModContainer> Mods { get; } = new();

        public AssetManager AssetManager { get; private set; }

        internal TextResourceManager TextResources { get; }

        internal LanguagePackManager LanguagePacks { get; }

        public WorldsManifest Worlds { get; private set; }

        public int InvalidModsCount { get; private set; }

        public const string Version = ThisAssembly.Git.BaseVersion.Major + "." + 
                                      ThisAssembly.Git.BaseVersion.Minor + "." + 
                                      ThisAssembly.Git.BaseVersion.Patch;

        public const string CommitHash = ThisAssembly.Git.Branch + "-" + ThisAssembly.Git.Commit;

#if DEBUG
        public const string Suffix = $"-dev ({CommitHash})";
#else
        public const string Suffix = "";
#endif

        public static Hat Instance { get; private set; }

        private readonly Fez _fezGame;

        public Hat(Fez fez)
        {
            Instance = this;
            _fezGame = fez;
            TextResources = new TextResourceManager(this);
            LanguagePacks = new LanguagePackManager();
            AssetManager = new AssetManager(this);
            AssetManager.InitializeHooks();
        }

        public void Initialize()
        {
            Logger.Log("HAT", $"HAT Mod Loader {Version}{Suffix}");

            if (GetModProxies(out var proxies))
            {
                if (GetModList(proxies, out var mods))
                {
                    ResolveDependencies(mods);
                    LoadMods();
                }
            }

            LanguagePacks.Load();
            Worlds = new WorldsManifest(Mods);
            InitializeAssemblies();
        }

        private static bool GetModProxies(out IEnumerable<IFileProxy> proxies)
        {
            proxies = new IEnumerable<IFileProxy>[]
                {
                    DirectoryFileProxy.EnumerateInDirectory(ModsDirectory),
                    ZipFileProxy.EnumerateInDirectory(ModsDirectory)
                }
                .SelectMany(x => x)
                .Where(fp => !IgnoredModNames.Contains(fp.ContainerName));

            if (!proxies.Any())
            {
                Logger.Log("HAT", LogSeverity.Warning, "There are no active mods inside 'Mods' directory.");
                return false;
            }

            return true;
        }

        private static bool GetModList(in IEnumerable<IFileProxy> proxies, out IList<ModContainer> mods)
        {
            mods = new List<ModContainer>();
            foreach (var proxy in proxies)
            {
                if (Metadata.TryLoad(proxy, out var metadata))
                {
                    mods.Add(new ModContainer(proxy, metadata));
                }
            }

            if (mods.Count < 1)
            {
                Logger.Log("HAT", LogSeverity.Warning,
                    "There are no mods to load. Perhaps they are all in 'ignorelist.txt'.");
                return false;
            }

            return true;
        }

        private void ResolveDependencies(IList<ModContainer> mods)
        {
            var resolverResult = ModDependencyResolver.Resolve(mods, PriorityModNames);
            Mods.AddRange(resolverResult.LoadOrder);
            InvalidModsCount = resolverResult.Invalid.Count;

            foreach (var node in resolverResult.Invalid)
            {
                Logger.Log("HAT", $"Mod '{node.Mod.Metadata.Name}' is invalid: {node.Details}");
            }

            Logger.Log("HAT", "The loading order of mods:");
            foreach (var mod in Mods)
            {
                Logger.Log("HAT", $"  {mod.Metadata.Name} by {mod.Metadata.Author} version {mod.Metadata.Version}");
            }
        }

        private void LoadMods()
        {
            var assetModCount = 0;
            var codeModsCount = 0;

            foreach (var mod in Mods)
            {
                if (AssetMod.TryLoad(mod.FileProxy, out var assetMod))
                {
                    mod.AssetMod = assetMod;
                    assetModCount += 1;
                }

                if (CodeMod.TryLoad(mod.FileProxy, mod.Metadata, out var codeMod))
                {
                    mod.CodeMod = codeMod;
                    codeModsCount += 1;
                }
            }

            var modsText = $"{Mods.Count} mod{(Mods.Count != 1 ? "s" : "")}";
            var codeModsText = $"{codeModsCount} code mod{(codeModsCount != 1 ? "s" : "")}";
            var assetModsText = $"{assetModCount} asset mod{(assetModCount != 1 ? "s" : "")}";
            Logger.Log("HAT", $"Successfully loaded {modsText} ({codeModsText} and {assetModsText})");
        }

        private void InitializeAssemblies()
        {
            foreach (var mod in Mods)
            {
                mod.Initialize(_fezGame);
            }
        }

        public void InitializeComponents()
        {
            foreach (var mod in Mods)
            {
                mod.InjectComponents();
            }
        }

        public void OnGameActivated()
        {
            LanguagePacks.ReloadActivePack();
            var changedAssets = new List<Asset>();
            foreach (var mod in Mods)
            {
                foreach (var asset in mod.ReloadAssets())
                {
                    if (!asset.Extension.Equals(".fxc", StringComparison.OrdinalIgnoreCase))
                    {
                        changedAssets.Add(asset);
                    }
                }
            }

            if (changedAssets.Count < 1)
            {
                return;
            }

            foreach (var asset in changedAssets)
            {
                if (TextResourceManager.IsTextResource(asset))
                {
                    continue;
                }

                if (asset.IsRemoved)
                {
                    AssetManager.RemoveAsset(asset);
                    if (!asset.IsMusicFile)
                    {
                        AssetManager.EvictFromCommon(asset.AssetPath);
                    }
                }
                else
                {
                    AssetManager.InjectAsset(asset);
                    if (!asset.IsMusicFile)
                    {
                        AssetManager.PatchInCommon(asset.AssetPath);
                    }
                }
            }

            Logger.Log("HAT", $"Reloaded {changedAssets.Count} asset(s)");

            if (changedAssets.Any(TextResourceManager.IsTextResource))
            {
                TextResources.Reload();
            }

            var levelManager = ServiceHelper.Get<IGameLevelManager>();
            var currentLevel = levelManager.Name;
            if (!string.IsNullOrEmpty(currentLevel))
            {
                var currentLevelAssetPath = "levels\\" + currentLevel.ToLower();
                if (changedAssets.Any(a => a.AssetPath == currentLevelAssetPath))
                {
                    levelManager.Name = null;
                    levelManager.ChangeLevel(currentLevel);
                    Logger.Log("HAT", $"Reloading {currentLevel}...");
                }
            }
        }

        public static void RegisterRequiredDependencyResolvers()
        {
            AssemblyResolverRegistry.Register(new HatSubdirectoryAssemblyResolver("MonoMod"));
            AssemblyResolverRegistry.Register(new HatSubdirectoryAssemblyResolver("FEZRepacker.Core"));
        }

        private static IList<string> InitializeIgnoredModsList() =>
            LoadModsTextList("ignorelist.txt", "ignorelist.default.txt");
        
        private static IList<string> InitializePriorityList() =>
            LoadModsTextList("prioritylist.txt", "prioritylist.default.txt");
        
        private static IList<string> LoadModsTextList(string path, string defaultContentResourceName)
        {
            if (!Directory.Exists(ModsDirectory))
            {
                Logger.Log("HAT", LogSeverity.Warning, "'Mods' directory not found. Creating it...");
                Directory.CreateDirectory(ModsDirectory);
            }

            var fullPath = Path.Combine(ModsDirectory, path);
            if (!File.Exists(fullPath))
            {
                File.WriteAllText(fullPath, LoadDefaultTextList(defaultContentResourceName));
                return new List<string>();
            }

            var modsList = new List<string>();
            var contents = File.ReadAllText(fullPath);

            foreach (var line in contents.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var clearedLine = line.Trim();
                if (clearedLine.StartsWith("#"))
                {
                    continue;
                }

                if (clearedLine.Length > 0)
                {
                    modsList.Add(clearedLine);
                }
            }

            return modsList;
        }

        private static string LoadDefaultTextList(string defaultContentResourceName)
        {
            var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(defaultContentResourceName);
            if (resourceStream == null)
            {
                return string.Empty;
            }
            
            using var resourceReader = new StreamReader(resourceStream);
            return resourceReader.ReadToEnd();
        }
    }
}