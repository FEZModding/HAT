
using Common;
using FEZRepacker.Core.Conversion;
using FEZRepacker.Core.FileSystem;
using FEZRepacker.Core.XNB;

namespace HatModLoader.Source.Assets
{
    internal static class AssetLoaderHelper
    {
        private static readonly string[] AllowedRawExtensions = { ".xnb", ".ogg", ".fxc" };

        public static List<Asset> GetListFromFileDictionary(Dictionary<string, Stream> files)
        {
            var assets = new List<Asset>();

            var bundles = FileBundle.BundleFiles(files);

            foreach (var bundle in bundles)
            {
                try
                {
                    var deconvertedObject = FormatConversion.Deconvert(bundle)!;
                    using var xnbData = XnbSerializer.Serialize(deconvertedObject);

                    assets.Add(new Asset(bundle.BundlePath, ".xnb", xnbData));
                }
                catch(Exception ex)
                {
                    bool savedAnyRawFiles = false;
                    foreach (var file in bundle.Files)
                    {
                        var extension = file.Extension;
                        if (extension.Length == 0) extension = bundle.MainExtension;
                        if (!AllowedRawExtensions.Contains(extension)) continue;

                        file.Data.Seek(0, SeekOrigin.Begin);
                        assets.Add(new Asset(bundle.BundlePath, extension, file.Data));
                        savedAnyRawFiles = true;
                    }

                    if (!savedAnyRawFiles)
                    {
                        Logger.Log("HAT", $"Could not convert asset bundle {bundle.BundlePath}: {ex.Message}.");
                    }
                }

                bundle.Dispose();
            }

            return assets;
        }

        public static List<Asset> LoadPakPackage(Stream stream)
        {
            var assets = new List<Asset>();

            using var pakReader = new PakReader(stream);
            foreach(var file in pakReader.ReadFiles())
            {
                using var fileData = file.Open();
                assets.Add(new Asset(file.Path, file.FindExtension(), fileData));
            }

            return assets;
        }
    }
}
