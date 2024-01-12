using FEZRepacker.Converter.FileSystem;
using FEZRepacker.Converter.XNB;

namespace HatModLoader.Source.Assets
{
    public class AssetProvider
    {
        private class AssetRecord
        {
            public Asset Asset;
            public string Path;
            public HashSet<string> UsedFilesPaths = new();
        }

        private FileSourceProxy source;

        private Dictionary<string, AssetRecord> assetCache;

        public AssetProvider(FileSourceProxy source)
        {
            this.source = source;
            assetCache = new();
        }

        private Asset FileBundleToAsset(FileBundle bundle)
        {
            var deconverter = new XnbDeconverter();

            using var deconverterStream = deconverter.Deconvert(bundle);

            if (deconverter.Converted)
            {
                return new Asset(bundle.BundlePath, ".xnb", deconverterStream);
            }
            else
            {
                var file = bundle.Files.Last();
                return new Asset(bundle.BundlePath, bundle.MainExtension + file.Extension, file.Data);
            }
        }

        private bool AssetModified(AssetRecord asset)
        {
            if (asset.UsedFilesPaths.Any(path => source.FileChanged(path))) return true;

            var currentAssetFiles = source.GetFilePathsByAssetPath(asset.Path);
            if (!currentAssetFiles.SetEquals(asset.UsedFilesPaths)) return true;

            return false;
        }
        private Asset LoadAssetFromSource(string path)
        {
            var bundle = source.ReadFileBundle(path);
            if (bundle == null) return null;
            var asset = FileBundleToAsset(bundle);

            var usedFilePaths = bundle.Files
                .Select(file => bundle.BundlePath + bundle.MainExtension + file.Extension);

            assetCache[path] = new AssetRecord
            {
                Asset = asset,
                Path = path,
                UsedFilesPaths = new(usedFilePaths)
            };

            return asset;
        }

        public Asset LoadAsset(string path)
        {
            if (assetCache.ContainsKey(path))
            {
                var assetRecord = assetCache[path];
                if (!AssetModified(assetRecord))
                {
                    return assetRecord.Asset;
                }
            }
            
            return LoadAssetFromSource(path);
        }
    }
}
