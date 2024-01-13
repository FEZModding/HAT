using Common;
using FEZRepacker.Converter.FileSystem;
using System;
using System.IO;
using System.Runtime.InteropServices.ComTypes;

namespace HatModLoader.Source.Assets
{
    internal class DirectorySourceProxy : FileSourceProxy
    {
        private Dictionary<string, string> assetPathCache = new();
        private Dictionary<string, FileInfo> cachedFileInfos = new();

        private readonly string rootDirectory;
        public DirectorySourceProxy(string rootDirectory)
        {
            rootDirectory = rootDirectory.Replace("/", "\\");
            if (!rootDirectory.EndsWith("\\")) rootDirectory += "\\";
            this.rootDirectory = rootDirectory;
        }

        private void ReloadInfoCacheForAsset(string assetPath)
        {
            var filePath = assetPathCache[assetPath];

            if (File.Exists(filePath))
            {
                cachedFileInfos[assetPath] = new FileInfo(filePath);
            }
            else 
            {
                cachedFileInfos.Remove(assetPath);
            }
        }

        public void Precache()
        {
            if(!IsValid()) return;

            assetPathCache.Clear();

            foreach (var path in Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = path.Substring(rootDirectory.Length);
                var assetPath = AssetProvider.CleanUpAssetPath(relativePath);
                assetPathCache[assetPath] = path;
            }
        }

        public bool IsValid()
        {
            return Directory.Exists(rootDirectory);
        }

        public HashSet<string> GetFileList()
        {
            if (!IsValid()) return new();

            return new(assetPathCache.Keys);
        }
        public bool FileChanged(string assetPath)
        {
            var filePath = assetPathCache[assetPath];

            if (File.Exists(filePath) != cachedFileInfos.ContainsKey(filePath))
            {
                return true;
            }

            var fileInfo = new FileInfo(filePath);
            var cachedFileInfo = cachedFileInfos[filePath];

            return
                fileInfo.LastWriteTime != cachedFileInfo.LastWriteTime ||
                fileInfo.Length != cachedFileInfo.Length;
        }

        public Dictionary<string, Stream> OpenFilesAndMarkUnchanged(HashSet<string> assetPaths)
        {
            var streams = new Dictionary<string, Stream>();

            foreach(var assetPath in assetPaths)
            {
                ReloadInfoCacheForAsset(assetPath);

                var filePath = assetPathCache[assetPath];
                var stream = File.OpenRead(filePath);

                streams[assetPath] = stream;
            }

            return streams;
        }
    }
}
