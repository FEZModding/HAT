using System.IO.Compression;
using System.Reflection;

namespace HatModLoader.Source.FileProxies
{
    public class ZipFileProxy : IFileProxy
    {
        private ZipArchive archive;
        private string zipPath;
        private readonly Dictionary<IntPtr, string> tempFiles = []; 
        public string RootPath => zipPath;
        public string ContainerName => Path.GetFileName(zipPath);

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

        private ZipArchiveEntry GetEntry(string localPath)
        {
            return archive.Entries.FirstOrDefault(e => e.FullName == localPath);
        }
        
        public IntPtr LoadLibrary(string localPath)
        {
            var tempFile = Path.GetTempFileName();
            var entry = GetEntry(localPath);
            entry.ExtractToFile(tempFile, true);

            var handle = NativeLibraryInterop.Load(tempFile);
            if (handle != IntPtr.Zero)
            {
                tempFiles.Add(handle, tempFile);
            }
            
            return handle;
        }

        public void UnloadLibrary(IntPtr handle)
        {
            if (tempFiles.TryGetValue(handle, out var tempFile))
            {
                NativeLibraryInterop.Free(handle);
                File.Delete(tempFile);
                tempFiles.Remove(handle);
            }
        }

        public bool IsDotNetAssembly(string localPath)
        {
            var tempFile = Path.GetTempFileName();
            var result = true;
            
            try
            {
                var entry = GetEntry(localPath);
                entry.ExtractToFile(tempFile, true);
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
