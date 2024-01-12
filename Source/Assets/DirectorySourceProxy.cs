using FEZRepacker.Converter.FileSystem;
using System.IO;

namespace HatModLoader.Source.Assets
{
    internal class DirectorySourceProxy : FileSourceProxy
    {

        private Dictionary<string, FileInfo> cachedFileInfos = new();

        private readonly string rootDirectory;
        public DirectorySourceProxy(string rootDirectory)
        {
            this.rootDirectory = rootDirectory;
        }

        private void ClearCacheForAsset(string assetPath)
        {
            foreach(var path in cachedFileInfos.Keys)
            {
                if (path.StartsWith(assetPath))
                {
                    cachedFileInfos.Remove(path);
                }
            }
        }

        public bool FileChanged(string filePath)
        {
            if(File.Exists(filePath) != cachedFileInfos.ContainsKey(filePath))
            {
                return true;
            }

            var fileInfo = new FileInfo(filePath);
            var cachedFileInfo = cachedFileInfos[filePath];

            return
                fileInfo.LastWriteTime != cachedFileInfo.LastWriteTime ||
                fileInfo.Length != cachedFileInfo.Length;
        }

        public HashSet<string> GetFilePathsByAssetPath(string assetName)
        {
            var paths = new HashSet<string>();
            if (!IsValid()) return paths;

            foreach(var path in Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = path.Replace("/", "\\").Substring(rootDirectory.Length + 1);
                if(relativePath.StartsWith(assetName, StringComparison.OrdinalIgnoreCase))
                {
                    paths.Add(relativePath);
                }
            }

            return paths;
        }

        public FileBundle ReadFileBundle(string assetName)
        {
            ClearCacheForAsset(assetName);

            var filePaths = GetFilePathsByAssetPath(assetName);

            if(filePaths.Count == 0)
            {
                return null;
            }

            var files = new Dictionary<string, Stream>();

            foreach (var path in filePaths)
            {
                var filePath = Path.Combine(rootDirectory, path);
                var stream = File.OpenRead(filePath);
                files.Add(path, stream);

                cachedFileInfos.Add(path, new FileInfo(filePath));
            }

            return FileBundle.BundleFiles(files).First();
        }

        public bool IsValid()
        {
            return Directory.Exists(rootDirectory);
        }
    }
}
