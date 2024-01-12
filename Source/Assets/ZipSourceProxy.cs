
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
        private HashSet<string> cachedZipPaths = new();

        public ZipSourceProxy(string zipPath, string rootDirectory)
        {
            this.zipPath = zipPath;
            this.rootDirectory = rootDirectory;

            lastZipState = null;
        }

        private void RecacheZipFile()
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
                if (zipEntry.FullName.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    var relativePath = zipEntry.FullName.Substring(rootDirectory.Length + 1).Replace("/", "\\");
                    cachedZipPaths.Add(relativePath);
                }
            }
        }

        private void CheckZipFileState()
        {
            var zipState = new FileInfo(zipPath);
            if (zipState != lastZipState)
            {
                lastZipState = zipState;
                RecacheZipFile();
            }
        }

        public bool FileChanged(string filePath)
        {
            CheckZipFileState();
            return filesUpToDateCache.Contains(filePath);
        }

        public HashSet<string> GetFilePathsByAssetPath(string assetName)
        {
            CheckZipFileState();
            return new(cachedZipPaths.Where(path => path.StartsWith(assetName)));
        }

        public FileBundle ReadFileBundle(string assetName)
        {
            filesUpToDateCache.Remove(assetName);

            var filePaths = GetFilePathsByAssetPath(assetName);

            if (filePaths.Count == 0)
            {
                return null;
            }

            var files = new Dictionary<string, Stream>();

            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read);

            foreach (var zipEntry in archive.Entries)
            {
                if (zipEntry.FullName.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativePath = zipEntry.FullName.Substring(rootDirectory.Length + 1).Replace("/", "\\");

                if (!filePaths.Contains(relativePath))
                {
                    continue;
                }

                var zipFileStream = zipEntry.Open();
                files.Add(relativePath, zipFileStream);
            }

            return FileBundle.BundleFiles(files).First();
        }

        public bool IsValid()
        {
            CheckZipFileState();
            return filesUpToDateCache.Count > 0;
        }
    }
}
