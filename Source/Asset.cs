using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace HatModLoader.Source
{
    public class Asset
    {
        public string AssetPath { get; private set; }
        public string Extension { get; private set; }
        public byte[] Data { get; private set; }

        public bool Converted { get; private set; }
        public bool IsMusicFile { get; private set; }

        public Asset(string path, string extension, byte[] data)
        {
            AssetPath = path;
            Extension = extension;
            Data = data;

            TryConvertAsset();
        }

        private void TryConvertAsset()
        {
            // TODO: special conversion handling for different types, like images or animation

            if(Extension == ".ogg" && AssetPath.StartsWith("music\\"))
            {
                IsMusicFile = true;
                AssetPath = AssetPath.Substring("music\\".Length);
            }

            Converted = false;
        }

        public static List<Asset> LoadDirectory(string directoryPath)
        {
            var assets = new List<Asset>();

            foreach (var path in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(path).ToLower();
                // might yield incorrect result if path doesn't start with directory path!!
                var relativePath = path.Substring(directoryPath.Length+1).Replace("/", "\\").ToLower(); 
                relativePath = relativePath.Substring(0, relativePath.Length - extension.Length);

                if (extension.Length > 0)
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    assets.Add(new Asset(relativePath, extension, bytes));
                }
            }

            return assets;
        }

        public static List<Asset> LoadZip(ZipArchive archive, string assetsDirectory)
        {
            var assets = new List<Asset>();

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

                    assets.Add(new Asset(relativePath, extension, bytes));
                }
            }

            return assets;
        }
    }
}
