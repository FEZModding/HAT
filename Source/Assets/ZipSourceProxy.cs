
using FEZRepacker.Converter.FileSystem;
using System.IO.Compression;

namespace HatModLoader.Source.Assets
{
    internal class ZipSourceProxy : FileSourceProxy
    {
        private readonly string zipPath;
        private readonly string rootDirectory;

        private FileInfo lastZipState;
        private HashSet<string> filesUpToDateCache = new();
        private Dictionary<string, string> cachedZipPaths = new();

        public ZipSourceProxy(string zipPath, string rootDirectory)
        {
            this.zipPath = zipPath;

            rootDirectory = rootDirectory.Replace("/", "\\");
            if (!rootDirectory.EndsWith("\\")) rootDirectory += "\\";
            this.rootDirectory = rootDirectory;

            lastZipState = null;
        }

        private void RecacheZipFileContent()
        {
            filesUpToDateCache.Clear();
            cachedZipPaths.Clear();

            if (!File.Exists(zipPath))
            {
                return;
            }

            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read);
            foreach (var zipEntry in archive.Entries)
            {
                var fileName = zipEntry.FullName;
                if (fileName.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    var relativePath = fileName.Substring(rootDirectory.Length);
                    var assetPath = AssetProvider.CleanUpAssetPath(relativePath);
                    cachedZipPaths[fileName] = assetPath;
                }
            }
        }

        public void Precache()
        {
            var zipState = new FileInfo(zipPath);
            if (zipState != lastZipState)
            {
                lastZipState = zipState;
                RecacheZipFileContent();
            }
        }

        public bool FileChanged(string filePath)
        {
            return filesUpToDateCache.Contains(filePath);
        }

        public bool IsValid()
        {
            return filesUpToDateCache.Count > 0;
        }

        public HashSet<string> GetFileList()
        {
            return new(cachedZipPaths.Values);
        }

        public Dictionary<string, Stream> OpenFilesAndMarkUnchanged(HashSet<string> filePaths)
        {
            var files = new Dictionary<string, Stream>();

            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read);

            foreach(var zipEntry in archive.Entries)
            {
                var assetPath = cachedZipPaths[zipEntry.FullName];

                if (!filePaths.Contains(assetPath))
                {
                    continue;
                }

                var zipFileStream = zipEntry.Open();
                files.Add(assetPath, zipFileStream);
            }

            return files;
        }
    }
}
