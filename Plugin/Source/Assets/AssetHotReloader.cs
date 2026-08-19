using System.Reflection;
using Common;
using FezEngine.Services;
using FezEngine.Structure;
using FezEngine.Tools;
using FezGame.Services;

namespace HatModLoader.Source.Assets
{
    internal static class AssetHotReloader
    {
        private static readonly FieldInfo LevelDataField = typeof(LevelManager)
            .GetField("levelData", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo ArtObjectNameField = typeof(ArtObjectInstance)
            .GetField("artObjectName", BindingFlags.NonPublic | BindingFlags.Instance);

        public static void OnGameActivated(Hat hat)
        {
            var changedAssets = GetChangedAssets(hat);
            if (changedAssets.Count < 1)
            {
                return;
            }

            var levelManager = ServiceHelper.Get<IGameLevelManager>();
            var currentLevel = levelManager.Name;
            var levelDependencyPaths = new HashSet<string>(StringComparer.Ordinal);
            var levelMusicDependencyPaths = new HashSet<string>(StringComparer.Ordinal);

            CollectCurrentLevelDependencies(
                levelManager, currentLevel, levelDependencyPaths, levelMusicDependencyPaths);

            var reloadCurrentLevel = changedAssets.Any(asset =>
                IsCurrentLevelDependency(asset, levelDependencyPaths, levelMusicDependencyPaths));

            foreach (var asset in changedAssets)
            {
                var isLevelDependency =
                    IsCurrentLevelDependency(asset, levelDependencyPaths, levelMusicDependencyPaths);

                if (asset.IsRemoved)
                {
                    hat.AssetManager.RemoveAsset(asset);
                    if (!asset.IsMusicFile && !isLevelDependency)
                    {
                        hat.AssetManager.EvictFromCommon(asset.AssetPath);
                    }
                }
                else
                {
                    hat.AssetManager.InjectAsset(asset);
                    if (!asset.IsMusicFile && !isLevelDependency)
                    {
                        hat.AssetManager.PatchInCommon(asset.AssetPath);
                    }
                }
            }

            Logger.Log("HAT", $"Reloaded {changedAssets.Count} asset(s)");

            if (reloadCurrentLevel)
            {
                levelManager.Reset();
                levelManager.ChangeLevel(currentLevel);
                Logger.Log("HAT", $"Reloading {currentLevel}...");
            }
        }

        private static List<Asset> GetChangedAssets(Hat hat)
        {
            var changedAssets = new List<Asset>();
            foreach (var mod in hat.Mods)
            {
                foreach (var asset in mod.ReloadAssets())
                {
                    if (!asset.Extension.Equals(".fxc", StringComparison.OrdinalIgnoreCase))
                    {
                        changedAssets.Add(asset);
                    }
                }
            }

            return changedAssets;
        }

        private static void CollectCurrentLevelDependencies(
            IGameLevelManager levelManager,
            string currentLevel,
            ISet<string> levelDependencyPaths,
            ISet<string> levelMusicDependencyPaths)
        {
            if (string.IsNullOrEmpty(currentLevel))
            {
                return;
            }

            levelDependencyPaths.Add(NormalizeAssetPath(Path.Combine("levels", currentLevel)));

            // Loaded assets may have an internal Name which differs from the serialized asset reference.
            // FEZ keeps the original references in Level and ArtObjectInstance; use those for cache paths.
            var levelData = LevelDataField?.GetValue(levelManager) as Level;
            var trileSetName = levelData?.TrileSetName ?? levelManager.TrileSet?.Name;
            var skyName = levelData?.SkyName ?? levelManager.Sky?.Name;

            AddDependency(levelDependencyPaths, "trile sets", trileSetName);
            AddDependency(levelDependencyPaths, "skies", skyName);

            if (!string.IsNullOrEmpty(levelManager.SongName))
            {
                // Music assets use a path relative to the Music directory in SoundManager's cache.
                levelMusicDependencyPaths.Add(NormalizeAssetPath(levelManager.SongName));
            }

            foreach (var instance in levelManager.ArtObjects.Values)
            {
                var assetName = ArtObjectNameField?.GetValue(instance) as string ?? instance.ArtObjectName;
                AddDependency(levelDependencyPaths, "art objects", assetName);
            }

            foreach (var plane in levelManager.BackgroundPlanes.Values)
            {
                if (!string.IsNullOrEmpty(plane.TextureName))
                {
                    AddDependency(levelDependencyPaths, "background planes", plane.TextureName);
                }
            }
        }

        private static void AddDependency(ISet<string> dependencies, string directory, string assetName)
        {
            if (!string.IsNullOrEmpty(assetName))
            {
                dependencies.Add(NormalizeAssetPath(Path.Combine(directory, assetName)));
            }
        }

        private static bool IsCurrentLevelDependency(
            Asset asset,
            ISet<string> levelDependencyPaths,
            ISet<string> levelMusicDependencyPaths)
        {
            return asset.IsMusicFile
                ? levelMusicDependencyPaths.Contains(NormalizeAssetPath(asset.AssetPath))
                : levelDependencyPaths.Contains(NormalizeAssetPath(asset.AssetPath));
        }

        private static string NormalizeAssetPath(string path)
        {
            return path.Replace('/', '\\').ToLowerInvariant();
        }
    }
}
