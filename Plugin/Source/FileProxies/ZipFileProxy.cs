using System.IO.Compression;
using System.Security.Cryptography;
using Common;

namespace HatModLoader.Source.FileProxies
{
    public class ZipFileProxy : IFileProxy
    {
        private const string TempModPrefix = "hat-";

        private ZipArchive _archive;
        private DateTime _fileLastModified;
        private byte[] _zipHash;
        private string _codeRootPath;
        private readonly Lock _codeRootSync = new();

        public string RootPath { get; }

        public string CodeRootPath
        {
            get
            {
                lock (_codeRootSync)
                {
                    return _codeRootPath ??= ExtractCode();
                }
            }
        }

        public string ContainerName => Path.GetFileName(RootPath);

        private ZipFileProxy(string zipPath)
        {
            RootPath = zipPath;
            Reopen();
        }

        private void Reopen()
        {
            using (var zipStream = File.OpenRead(RootPath)) 
                _zipHash = SHA256.HashData(zipStream);
            _archive?.Dispose();
            _archive = ZipFile.OpenRead(RootPath);
            _fileLastModified = File.GetLastWriteTimeUtc(RootPath);
            _codeRootPath = null;
        }

        public void Refresh()
        {
            lock (_codeRootSync)
            {
                var modified = File.GetLastWriteTimeUtc(RootPath);
                if (modified > _fileLastModified)
                {
                    Reopen();
                }
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

        static ZipFileProxy()
        {
            CleanupStaleState();
        }

        public static IEnumerable<ZipFileProxy> EnumerateInDirectory(string directory)
        {
            return Directory.EnumerateFiles(directory)
                .Where(file => Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                .Select(file => new ZipFileProxy(file));
        }

        private string ExtractCode()
        {
            var root = Path.Combine(Hat.TempDirectory, TempModPrefix + Convert.ToHexString(_zipHash).ToLowerInvariant());
            if (Directory.Exists(root))
            {
                return root;
            }

            Directory.CreateDirectory(root);

            try
            {
                using var archive = ZipFile.OpenRead(RootPath);
                foreach (var entry in archive.Entries)
                {
                    var relative = entry.FullName.Replace('\\', '/');
                    if (relative.Length == 0)
                    {
                        continue;
                    }

                    var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                    var comparison = OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal;

                    if (Path.IsPathRooted(relative) ||
                        !path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                    {
                        throw new InvalidDataException($"Unsafe ZIP entry in '{RootPath}': {entry.FullName}");
                    }

                    if (!relative.EndsWith('/') && IsCodeFile(relative))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        entry.ExtractToFile(path);
                    }
                }
            }
            catch
            {
                Directory.Delete(root, true);
                throw;
            }

            return root;
        }

        private static bool IsCodeFile(string path)
        {
            var name = Path.GetFileName(path);
            return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains(".so.", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase);
        }

        private static void CleanupStaleState()
        {
            if (!Directory.Exists(Hat.TempDirectory))
            {
                return;
            }

            try
            {
                foreach (var path in Directory.EnumerateDirectories(Hat.TempDirectory, TempModPrefix + "*"))
                {
                    var name = Path.GetFileName(path);
                    var hash = name[4..];
                    if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                    {
                        continue;
                    }

                    try
                    {
                        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        Directory.Delete(path, true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Logger.Log("HAT", LogSeverity.Warning,
                            $"Could not clear staged mod folder '{path}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Log("HAT", LogSeverity.Warning, $"Could not inspect staged mod folders: {ex.Message}");
            }
        }
    }
}
