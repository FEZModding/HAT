using HatModLoader.Source.Assets;
using HatModLoader.Source.FileProxies;

namespace HatModLoader.Source.ModDefinition
{
    public class AssetMod
    {
        private const string AssetDirectoryName = "Assets";
        private const string AssetPakName = AssetDirectoryName + ".pak";

        public IEnumerable<Asset> Assets => _assets;

        private readonly List<Asset> _assets = new();

        private readonly HashSet<string> _pakAssetPaths;

        private DateTime _pakLastModified;

        private AssetMod(List<Asset> allAssets, HashSet<string> pakAssetPaths, DateTime pakLastModified)
        {
            _assets.AddRange(allAssets);
            _pakAssetPaths = pakAssetPaths;
            _pakLastModified = pakLastModified;
        }

        public IEnumerable<Asset> Reload(IFileProxy proxy)
        {
            var changed = new List<Asset>();

            #region Loose files
            
            var currentFiles = BuildAssetList(proxy);
            var currentRawPaths = new HashSet<string>(currentFiles.Select(f => f.RawPath));

            // Changed or new
            var toReload = currentFiles
                .Where(f =>
                {
                    var existing = _assets.FirstOrDefault(a => 
                        !_pakAssetPaths.Contains(a.AssetPath) && f.RawPath.StartsWith(a.SourcePath + "."));
                    return existing == null || f.Timestamp > existing.LastModified;
                })
                .ToList();

            if (toReload.Count > 0)
            {
                foreach (var asset in AssetLoaderHelper.GetListFromFileDictionary(toReload))
                {
                    var idx = _assets.FindIndex(a => a.AssetPath == asset.AssetPath);
                    if (idx >= 0)
                        _assets[idx] = asset;
                    else
                        _assets.Add(asset);
                    changed.Add(asset);
                }
            }

            // Removed loose files
            var removedLoose = _assets
                .Where(a => !_pakAssetPaths.Contains(a.AssetPath)
                            && !currentRawPaths.Any(p => p.StartsWith(a.SourcePath + ".")))
                .ToList();
            foreach (var asset in removedLoose)
            {
                _assets.Remove(asset);
                changed.Add(asset.AsRemoved());
            }
            
            #endregion

            #region PAK file
            
            if (proxy.FileExists(AssetPakName))
            {
                var pakModified = proxy.GetLastModified(AssetPakName);
                if (pakModified > _pakLastModified)
                {
                    _pakLastModified = pakModified;
                    var newPakAssets = LoadPakAssets(proxy, pakModified);
                    var newPakPaths = new HashSet<string>(newPakAssets.Select(a => a.AssetPath));

                    // Removed pak entries
                    var removedPak = _assets
                        .Where(a => _pakAssetPaths.Contains(a.AssetPath) && !newPakPaths.Contains(a.AssetPath))
                        .ToList();
                    foreach (var asset in removedPak)
                    {
                        _assets.Remove(asset);
                        _pakAssetPaths.Remove(asset.AssetPath);
                        changed.Add(asset.AsRemoved());
                    }

                    // Added or updated pak entries
                    foreach (var asset in newPakAssets)
                    {
                        var idx = _assets.FindIndex(a => a.AssetPath == asset.AssetPath);
                        if (idx >= 0)
                            _assets[idx] = asset;
                        else
                            _assets.Add(asset);
                        _pakAssetPaths.Add(asset.AssetPath);
                        changed.Add(asset);
                    }
                }
            }
            else if (_pakAssetPaths.Count > 0)
            {
                // Pak deleted entirely
                var removedPak = _assets.Where(a => _pakAssetPaths.Contains(a.AssetPath)).ToList();
                foreach (var asset in removedPak)
                {
                    _assets.Remove(asset);
                    changed.Add(asset.AsRemoved());
                }

                _pakAssetPaths.Clear();
                _pakLastModified = DateTime.MinValue;
            }
            
            #endregion

            return changed;
        }

        private static List<AssetLoaderHelper.File> BuildAssetList(IFileProxy proxy)
        {
            var files = new List<AssetLoaderHelper.File>();
            foreach (var filePath in proxy.EnumerateFiles(AssetDirectoryName))
            {
                var relativePath = filePath.Substring(AssetDirectoryName.Length + 1).Replace("/", "\\").ToLower();
                files.Add(new AssetLoaderHelper.File(
                    relativePath, proxy.OpenFile(filePath), proxy.GetLastModified(filePath)));
            }

            return files;
        }

        private static List<Asset> LoadPakAssets(IFileProxy proxy, DateTime pakModified)
        {
            var assets = new List<Asset>();
            using var pakReader = new FEZRepacker.Core.FileSystem.PakReader(proxy.OpenFile(AssetPakName));
            foreach (var file in pakReader.ReadFiles())
            {
                using var fileData = file.Open();
                assets.Add(new Asset(file.Path, file.FindExtension(), fileData, pakModified));
            }

            return assets;
        }

        public static bool TryLoad(IFileProxy proxy, out AssetMod assetMod)
        {
            var looseAssets = AssetLoaderHelper.GetListFromFileDictionary(BuildAssetList(proxy));

            var pakLastModified = DateTime.MinValue;
            var pakAssets = new List<Asset>();
            if (proxy.FileExists(AssetPakName))
            {
                pakLastModified = proxy.GetLastModified(AssetPakName);
                pakAssets = LoadPakAssets(proxy, pakLastModified);
            }

            var allAssets = looseAssets.Concat(pakAssets).ToList();
            if (allAssets.Count < 1)
            {
                assetMod = null;
                return false;
            }

            var pakAssetPaths = new HashSet<string>(pakAssets.Select(a => a.AssetPath));
            assetMod = new AssetMod(allAssets, pakAssetPaths, pakLastModified);
            return true;
        }
    }
}