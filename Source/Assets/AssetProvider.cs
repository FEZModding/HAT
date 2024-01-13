using Common;
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
            return new Asset(bundle.BundlePath, ".xnb", deconverterStream);
        }

        private HashSet<string> GetFilePathsByAssetPath(string assetPath)
        {
            return new(source.GetFileList().Where(path => path.StartsWith(assetPath)));
        }

        private bool AssetModified(AssetRecord asset)
        {
            var currentAssetFiles = GetFilePathsByAssetPath(asset.Path);
            if (!currentAssetFiles.SetEquals(asset.UsedFilesPaths)) return true;

            if (asset.UsedFilesPaths.Any(path => source.FileChanged(path))) return true;

            return false;
        }

        private bool TryLoadFileBundleFromSource(string assetPath, out FileBundle bundle)
        {
            var fileNames = GetFilePathsByAssetPath(assetPath);

            if (fileNames.Count == 0)
            {
                bundle = null;
                return false;
            }

            var filesToBundle = source.OpenFilesAndMarkUnchanged(fileNames);
            bundle = FileBundle.BundleFiles(filesToBundle).Last();

            return true;
        }


        private bool TryLoadAssetFromSource(string path, out Asset asset)
        {
            if (!TryLoadFileBundleFromSource(path, out var bundle))
            {
                asset = null;
                return false;
            }
            asset = FileBundleToAsset(bundle);

            var usedFilePaths = bundle.Files
                .Select(file => bundle.BundlePath + bundle.MainExtension + file.Extension);

            assetCache[path] = new AssetRecord
            {
                Asset = asset,
                Path = path,
                UsedFilesPaths = new(usedFilePaths)
            };

            return true;
        }

        public bool TryLoadAsset(string path, out Asset asset)
        {
            source.Precache();
            if(!source.IsValid())
            {
                asset = null;
                return false;
            }

            path = CleanUpAssetPath(path);


            if (assetCache.ContainsKey(path))
            {
                var assetRecord = assetCache[path];
                if (!AssetModified(assetRecord))
                {
                    asset = assetRecord.Asset;
                    return true;
                }
            }
            
            return TryLoadAssetFromSource(path, out asset);
        }
        public static string CleanUpAssetPath(string path)
        {
            return path.Replace('/', '\\').ToLower();
        }
    }
}
