using FezEngine.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace HatModLoader.Source
{
    public static class AssetsHelper
    {
        public static void InjectAsset(string path, byte[] data)
        {
            var cachedAssetsField = typeof(MemoryContentManager).GetField("cachedAssets", BindingFlags.NonPublic | BindingFlags.Static);
            var cachedAssets = cachedAssetsField.GetValue(null) as Dictionary<string, byte[]>;
            cachedAssets[path] = data;
        }

        public static Dictionary<string, byte[]> LoadDirectory(string directoryPath)
        {
            var assets = new Dictionary<string, byte[]>();

            foreach (var path in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(path).ToLower();
                var relativePath = new Uri(directoryPath).MakeRelativeUri(new Uri(path)).OriginalString
                    .Replace("/", "\\").ToLower();
                relativePath = relativePath.Substring(0, relativePath.Length - extension.Length);

                if (extension.Length > 0)
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    assets.Add(relativePath, ConvertFile(bytes, extension));
                }
            }

            return assets;
        }

        public static Dictionary<string, byte[]> LoadZip(ZipArchive archive, string assetsDirectory)
        {
            var assets = new Dictionary<string, byte[]>();

            foreach (var zipEntry in archive.Entries.Where(e => e.FullName.StartsWith(assetsDirectory, StringComparison.OrdinalIgnoreCase)))
            {
                var extension = Path.GetExtension(zipEntry.Name).ToLower();
                var relativePath = zipEntry.FullName.Substring(assetsDirectory.Length + 1)
                    .Replace("/", "\\").ToLower();
                relativePath = relativePath.Substring(0, relativePath.Length - extension.Length);

                if (extension.Length > 0)
                {
                    var zipFile = zipEntry.Open();
                    byte[] bytes = new byte[zipFile.Length];
                    zipFile.Read(bytes, 0, bytes.Length);

                    assets.Add(relativePath, ConvertFile(bytes, extension));
                }
            }

            return assets;
        }

        public static byte[] ConvertFile(byte[] original, string extension)
        {
            // TODO: special conversion handling for different types, like images or animation

            if (extension == ".xnb")
            {
                return original;
            }

            return original;
        }
    }
}
