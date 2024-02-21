﻿namespace HatModLoader.Source.FileProxies
{
    internal class DirectoryFileProxy : IFileProxy
    {
        private string modDirectory;

        public string RootPath => modDirectory;
        public string ContainerName => new DirectoryInfo(modDirectory).Name;

        public DirectoryFileProxy(string directoryPath)
        {
            modDirectory = directoryPath;
        }

        public IEnumerable<string> EnumerateFiles(string localPath)
        {
            var searchPath = Path.Combine(modDirectory, localPath);

            if(!Directory.Exists(searchPath))
            {
                return Enumerable.Empty<string>();
            }

            var localFilePaths = Directory.EnumerateFiles(searchPath, "*", SearchOption.AllDirectories)
                .Select(path => path.Substring(modDirectory.Length + 1));

            return localFilePaths;
        }

        public bool FileExists(string localPath)
        {
            return File.Exists(Path.Combine(modDirectory, localPath));
        }

        public Stream OpenFile(string localPath)
        {
            return File.OpenRead(Path.Combine(modDirectory, localPath));
        }

        public void Dispose() { }


        public static IEnumerable<DirectoryFileProxy> EnumerateInDirectory(string directory)
        {
            return Directory.EnumerateDirectories(directory)
                .Select(path => new DirectoryFileProxy(path));
        }
    }
}
