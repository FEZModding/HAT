using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

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

        public IntPtr LoadLibrary(string localPath)
        {
            var tempFile = Path.GetTempFileName();
            using (var fs = File.Create(tempFile))
            using (var s = GetEntry(localPath).Open())
                s.CopyTo(fs);

            var handle = NativeLibrary.Load(tempFile);
            if (handle != IntPtr.Zero)
            {
                _tempFiles.Add(handle, tempFile);
            }

            return handle;
        }

        public void UnloadLibrary(IntPtr handle)
        {
            if (_tempFiles.TryGetValue(handle, out var tempFile))
            {
                NativeLibrary.Free(handle);
                File.Delete(tempFile);
                _tempFiles.Remove(handle);
            }
        }

        public bool IsDotNetAssembly(string localPath)
        {
            var tempFile = Path.GetTempFileName();
            var result = true;

            try
            {
                using (var fs = File.Create(tempFile))
                using (var s = GetEntry(localPath).Open())
                    s.CopyTo(fs);
                AssemblyName.GetAssemblyName(tempFile);
            }
            catch (BadImageFormatException)
            {
                result = false;     // Native library file
            }

            File.Delete(tempFile);
            return result;
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
