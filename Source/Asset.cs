using FEZRepacker.Converter.FileSystem;
using FEZRepacker.Converter.XNB;
using System.IO.Compression;

namespace HatModLoader.Source
{
    public class Asset
    {
        public string AssetPath { get; private set; }
        public string Extension { get; private set; }
        public byte[] Data { get; private set; }
        public bool IsMusicFile { get; private set; }

        public Asset(string path, string extension)
        {
            AssetPath = path;
            Extension = extension;

            CheckMusicAsset();
        }

        public Asset(string path, string extension, byte[] data)
            : this(path, extension)
        {
            Data = data;
        }

        public Asset(string path, string extension, Stream data)
            : this(path, extension)
        {
            Data = new byte[data.Length];
            data.Read(Data, 0, Data.Length);
        }

        private void CheckMusicAsset()
        {
            if(Extension == ".ogg" && AssetPath.StartsWith("music\\"))
            {
                IsMusicFile = true;
                AssetPath = AssetPath.Substring("music\\".Length);
            }
        }

        private static Dictionary<string, Stream> LoadDictionary(string directoryPath)
        {
            var files = new Dictionary<string, Stream>();

            foreach (var path in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = path.Substring(directoryPath.Length + 1).Replace("/", "\\").ToLower();
                var stream = File.OpenRead(path);
                files.Add(relativePath, stream);
            }

            return files;
        }

        private static Dictionary<string, Stream> LoadZip(ZipArchive archive, string assetsDirectory)
        {
            var files = new Dictionary<string, Stream>();

            foreach (var zipEntry in archive.Entries.Where(e => e.FullName.StartsWith(assetsDirectory, StringComparison.OrdinalIgnoreCase)))
            {
                var relativePath = zipEntry.FullName.Substring(assetsDirectory.Length + 1).Replace("/", "\\").ToLower();
                var zipFileStream = zipEntry.Open();
                files.Add(relativePath, zipFileStream);
            }

            return files;
        }

        private static List<Asset> GetAssetsFromFileDictionary(Dictionary<string, Stream> files)
        {
            var assets = new List<Asset>();

            var bundles = FileBundle.BundleFiles(files);

            foreach(var bundle in bundles)
            {
                var deconverter = new XnbDeconverter();

                using var deconverterStream = deconverter.Deconvert(bundle);

                if (deconverter.Converted)
                {
                    assets.Add(new Asset(bundle.BundlePath, ".xnb", deconverterStream));
                }
                else
                {
                    foreach (var file in bundle.Files)
                    {
                        file.Data.Seek(0, SeekOrigin.Begin);
                        assets.Add(new Asset(bundle.BundlePath, bundle.MainExtension + file.Extension, file.Data));
                    }
                }

                bundle.Dispose();
            }

            return assets;
        }
            

        public static List<Asset> ConvertDirectoryToAssetList(string directoryPath)
        {
            var files = LoadDictionary(directoryPath);
            return GetAssetsFromFileDictionary(files);
        }

        public static List<Asset> ConvertZipToAssetList(ZipArchive archive, string assetsDirectory)
        {
            var files = LoadZip(archive, assetsDirectory);
            return GetAssetsFromFileDictionary(files);
        }
    }
}
