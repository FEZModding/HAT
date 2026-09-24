using System.IO.Compression;

namespace HatModLoader.Source.FileProxies
{
    public class ZipFileProxy : IFileProxy
    {
        private ZipArchive _archive;
        private DateTime _fileLastModified;
        private readonly Dictionary<IntPtr, string> _tempFiles = new();

        public string RootPath { get; }

        public string ContainerName => Path.GetFileName(RootPath);

        private ZipFileProxy(string zipPath)
        {
            RootPath = zipPath;
            Reopen();
        }

        private void Reopen()
        {
            _archive?.Dispose();
            _archive = ZipFile.OpenRead(RootPath);
            _fileLastModified = File.GetLastWriteTimeUtc(RootPath);
        }

        public void Refresh()
        {
            var modified = File.GetLastWriteTimeUtc(RootPath);
            if (modified > _fileLastModified)
            {
                Reopen();
            }
        }

        public IEnumerable<string> EnumerateFiles(string localPath)
        {
            if (localPath.Length > 0 && !localPath.EndsWith('/')) localPath += "/";

            return _archive.Entries
                .Where(e => e.Name.Length > 0 && e.FullName.StartsWith(localPath))
                .Select(e => e.FullName);
        }

        public bool FileExists(string localPath)
        {
            return _archive.Entries.Any(e => e.Name.Length > 0 && e.FullName == localPath);
        }

        public Stream OpenFile(string localPath)
        {
            var entry = GetEntry(localPath);
            var ms = new MemoryStream();
            using var s = entry.Open();
            s.CopyTo(ms);
            ms.Position = 0;
            return ms;
        }

        public DateTime GetLastModified(string localPath)
        {
            return GetEntry(localPath).LastWriteTime.UtcDateTime;
        }

        private ZipArchiveEntry GetEntry(string localPath)
        {
            return _archive.Entries.FirstOrDefault(e => e.Name.Length > 0 && e.FullName == localPath);
        }

        public void Dispose()
        {
            _archive.Dispose();
        }

        public static IEnumerable<ZipFileProxy> EnumerateInDirectory(string directory)
        {
            return Directory.EnumerateFiles(directory)
                .Where(file => Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                .Select(file => new ZipFileProxy(file));
        }
    }
}
