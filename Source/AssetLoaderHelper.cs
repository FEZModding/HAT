
using FEZRepacker.Core.Conversion;
using FEZRepacker.Core.FileSystem;
using FEZRepacker.Core.XNB;

namespace HatModLoader.Source
{
    internal static class AssetLoaderHelper
    {
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
                catch
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
    }
}
