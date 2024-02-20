
namespace HatModLoader.Source
{
    internal class DirectoryModLoader : IModLoader
    {
        private string modDirectory;

        public string ResourcePath => modDirectory;

        public DirectoryModLoader(string directoryPath)
        {
            modDirectory = directoryPath;
        }

        public bool TryLoadMetadata(out ModMetadata metadata)
        {
            foreach (var path in Directory.EnumerateFiles(modDirectory))
            {
                var relativeFileName = Path.GetFileName(path);

                if (relativeFileName.Equals(Mod.ModMetadataFileName, StringComparison.OrdinalIgnoreCase))
                {
                    using var reader = new StreamReader(path);

                    var result = ModMetadata.TryLoadFrom(reader, out metadata);

                    return result;
                }
            }

            metadata = default;
            return false;
        }

        public List<Asset> LoadAssets()
        {
            if(!TryGetAssetDirectoryPath(out var assetPath))
            {
                return new();
            }

            var files = new Dictionary<string, Stream>();

            foreach (var path in Directory.EnumerateFiles(assetPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = path.Substring(assetPath.Length + 1).Replace("/", "\\").ToLower();
                var stream = File.OpenRead(path);
                files.Add(relativePath, stream);
            }

            return AssetLoaderHelper.GetListFromFileDictionary(files);
        }

        public bool TryLoadAssembly(string assemblyName, out byte[] rawAssemblyData)
        {
            var libraryPath = Path.Combine(modDirectory, assemblyName);

            if (File.Exists(libraryPath))
            {
                rawAssemblyData = File.ReadAllBytes(libraryPath);
                return true;
            }
            else
            {
                rawAssemblyData = default!;
                return false;
            }
        }

        public void Dispose() {}

        private bool TryGetAssetDirectoryPath(out string assetPaths)
        {
            foreach (var path in Directory.EnumerateDirectories(modDirectory))
            {
                var relativeDirName = new DirectoryInfo(path).Name;
                if (relativeDirName.Equals(Mod.AssetsDirectoryName, StringComparison.OrdinalIgnoreCase))
                {
                    assetPaths = path;
                    return true;
                }
            }
            assetPaths = "";
            return false;
        }
    }
}
