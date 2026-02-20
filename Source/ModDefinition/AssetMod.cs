using HatModLoader.Source.Assets;
using HatModLoader.Source.FileProxies;

namespace HatModLoader.Source.ModDefinition
{
    public class AssetMod
    {
        private const string AssetDirectoryName = "Assets";

        private const string AssetPakName = AssetDirectoryName + ".pak";

        public List<Asset> Assets { get; }
    
        private AssetMod(List<Asset> assets)
        {
            Assets = assets;
        }

        public static bool TryLoad(IFileProxy proxy, Metadata metadata, out AssetMod assetMod)
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

            assetMod = new AssetMod(assets);
            return true;
        }
    }
}