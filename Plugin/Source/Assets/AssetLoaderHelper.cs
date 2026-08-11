using Common;
using FEZRepacker.Core.Conversion;
using FEZRepacker.Core.FileSystem;
using FEZRepacker.Core.XNB;

namespace HatModLoader.Source.Assets
{
    internal static class AssetLoaderHelper
    {
        private static readonly string[] AllowedRawExtensions = { ".xnb", ".ogg", ".fxc" };

        public static List<Asset> GetListFromFileDictionary(IReadOnlyList<File> files)
        {
            var assets = new List<Asset>();

            var byPath = files.ToDictionary(af => af.RawPath, af => af);
            var bundles = FileBundle.BundleFiles(byPath.ToDictionary(kv => kv.Key, kv => kv.Value.Stream));

            foreach (var bundle in bundles)
            {
                var lastModified = byPath
                    .Where(kv => kv.Key.StartsWith(bundle.BundlePath))
                    .Select(kv => kv.Value.Timestamp)
                    .DefaultIfEmpty(default)
                    .Max();
                try
                {
                    var deconvertedObject = FormatConversion.Deconvert(bundle)!;
                    using var xnbData = XnbSerializer.Serialize(deconvertedObject);
                    assets.Add(new Asset(bundle.BundlePath, ".xnb", xnbData, lastModified));
                }
                catch(Exception ex)
                {
                    var savedAnyRawFiles = false;
                    foreach (var file in bundle.Files)
                    {
                        var extension = file.Extension;
                        if (extension.Length == 0) extension = bundle.MainExtension;
                        if (!AllowedRawExtensions.Contains(extension))
                        {
                            continue;
                        }

                        file.Data.Seek(0, SeekOrigin.Begin);
                        assets.Add(new Asset(bundle.BundlePath, extension, file.Data, lastModified));
                        savedAnyRawFiles = true;
                    }

                    if (!savedAnyRawFiles)
                    {
                        Logger.Log("HAT", $"Could not convert asset bundle {bundle.BundlePath}: {ex}");
                    }
                }

                bundle.Dispose();
            }

            return assets;
        }

        public struct File
        {
            public readonly string RawPath;
            public readonly Stream Stream;
            public readonly DateTime Timestamp;

            public File(string rawPath, Stream stream, DateTime timestamp)
            {
                RawPath = rawPath;
                Stream = stream;
                Timestamp = timestamp;
            }
        }
    }
}
