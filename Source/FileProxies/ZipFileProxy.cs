using System.IO.Compression;

namespace HatModLoader.Source.FileProxies
{
    public class ZipFileProxy : IFileProxy
    {
        private ZipArchive archive;
        private string zipPath;
        public string RootPath => zipPath;
        public string ContainerName => Path.GetFileNameWithoutExtension(zipPath);

        public ZipFileProxy(string zipPath)
        {
            this.zipPath = zipPath;
            archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        }

        public IEnumerable<string> EnumerateFiles(string localPath)
        {
            if (!localPath.EndsWith("/")) localPath += "/";

            return archive.Entries
                .Where(e => e.FullName.StartsWith(localPath))
                .Select(e => e.FullName);
        }

        public bool FileExists(string localPath)
        {
            return archive.Entries.Where(e => e.FullName == localPath).Any();
        }

        public Stream OpenFile(string localPath)
        {
            return archive.Entries.Where(e => e.FullName == localPath).First().Open();
        }

        public void Dispose()
        {
            archive.Dispose();
        }

        public static IEnumerable<ZipFileProxy> EnumerateInDirectory(string directory)
        {
            return Directory.EnumerateFiles(directory)
                .Where(file => Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                .Select(file => new ZipFileProxy(file));
        }
    }
}
