using HatModLoader.Source.Assets;
using HatModLoader.Source.FileProxies;
using System.Xml.Serialization;

namespace HatModLoader.Source.Languages
{
    internal class LanguagePack : IDisposable
    {
        private const string AssetsDirectory = "Assets";
        
        private const string MetadataFilename = "Language.xml";

        private readonly IFileProxy _fileProxy;

        private Dictionary<string, Asset> _assets;

        public LanguageMetadata Metadata { get; }

        public string ContainerName => _fileProxy.ContainerName;

        public int Revision { get; private set; }

        private LanguagePack(IFileProxy fileProxy, LanguageMetadata metadata, Dictionary<string, Asset> assets)
        {
            _fileProxy = fileProxy;
            Metadata = metadata;
            _assets = assets;
        }

        public bool TryGetAsset(string assetPath, out Asset asset)
        {
            return _assets.TryGetValue(NormalizeAssetPath(assetPath), out asset);
        }

        public bool Reload()
        {
            _fileProxy.Refresh();
            var assets = LoadAssets(_fileProxy);
            if (AreAssetsEqual(_assets, assets))
            {
                return false;
            }

            _assets = assets;
            Revision += 1;
            return true;
        }

        public void Dispose()
        {
            _fileProxy.Dispose();
        }

        public static bool TryLoad(IFileProxy fileProxy, out LanguagePack pack)
        {
            pack = null;
            if (!fileProxy.FileExists(MetadataFilename))
            {
                return false;
            }

            try
            {
                using var stream = fileProxy.OpenFile(MetadataFilename);
                using var reader = new StreamReader(stream);
                var metadata = (LanguageMetadata)new XmlSerializer(typeof(LanguageMetadata)).Deserialize(reader);
                if (!IsValidMetadata(metadata))
                {
                    return false;
                }

                var assets = LoadAssets(fileProxy);
                if (!assets.ContainsKey(NormalizeAssetPath(metadata.SmallFont)) ||
                    !assets.ContainsKey(NormalizeAssetPath(metadata.BigFont)))
                {
                    return false;
                }

                pack = new LanguagePack(fileProxy, metadata, assets);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Dictionary<string, Asset> LoadAssets(IFileProxy fileProxy)
        {
            var files = new List<AssetLoaderHelper.File>();
            foreach (var filePath in fileProxy.EnumerateFiles(AssetsDirectory))
            {
                var relativePath = filePath.Substring(AssetsDirectory.Length + 1)
                    .Replace("/", "\\")
                    .ToLowerInvariant();
                files.Add(new AssetLoaderHelper.File(relativePath, fileProxy.OpenFile(filePath),
                    fileProxy.GetLastModified(filePath)));
            }

            return AssetLoaderHelper.GetListFromFileDictionary(files)
                .ToDictionary(asset => NormalizeAssetPath(asset.AssetPath), StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsValidMetadata(LanguageMetadata metadata)
        {
            return metadata is { Code.Length: 2 } &&
                   metadata.Code.All(character => character is >= 'a' and <= 'z') &&
                   !string.IsNullOrWhiteSpace(metadata.DisplayName) &&
                   metadata.DisplayName.IndexOfAny(new[] { '"', '\r', '\n' }) < 0 &&
                   !string.IsNullOrWhiteSpace(metadata.SmallFont) &&
                   !string.IsNullOrWhiteSpace(metadata.BigFont);
        }

        private static bool AreAssetsEqual(
            IReadOnlyDictionary<string, Asset> first,
            IReadOnlyDictionary<string, Asset> second)
        {
            if (first.Count != second.Count)
            {
                return false;
            }

            foreach (var asset in first)
            {
                if (!second.TryGetValue(asset.Key, out var replacement) ||
                    asset.Value.LastModified != replacement.LastModified)
                {
                    return false;
                }
            }

            return true;
        }

        private static string NormalizeAssetPath(string path)
        {
            return path.Replace("/", "\\").TrimStart('\\').ToLowerInvariant();
        }
    }
}
