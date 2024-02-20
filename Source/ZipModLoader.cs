using System.IO.Compression;

namespace HatModLoader.Source
{
    public class ZipModLoader : IModLoader
    {
        private ZipArchive archive;
        private string zipPath;
        public string ResourcePath => zipPath;

        public ZipModLoader(string zipPath)
        {
            this.zipPath = zipPath;
            archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        }

        public bool TryLoadMetadata(out ModMetadata metadata)
        {
            foreach (var zipEntry in archive.Entries.Where(e => !e.FullName.Contains("/")))
            {
                if (zipEntry.Name.Equals(Mod.ModMetadataFileName, StringComparison.OrdinalIgnoreCase))
                {
                    using var reader = new StreamReader(zipEntry.Open());

                    var result = ModMetadata.TryLoadFrom(reader, out metadata);

                    return result;
                }
            }

            metadata = default;
            return false;
        }

        public List<Asset> LoadAssets()
        {
            var files = new Dictionary<string, Stream>();

            foreach (var zipEntry in archive.Entries.Where(e => e.FullName.StartsWith(Mod.AssetsDirectoryName, StringComparison.OrdinalIgnoreCase)))
            {
                var relativePath = zipEntry.FullName.Substring(Mod.AssetsDirectoryName.Length + 1).Replace("/", "\\").ToLower();
                var zipFileStream = zipEntry.Open();
                files.Add(relativePath, zipFileStream);
            }

            return AssetLoaderHelper.GetListFromFileDictionary(files);
        }

        public bool TryLoadAssembly(string assemblyName, out byte[] rawAssemblyData)
        {
            var dllMatches = archive.Entries.Where(entry => entry.Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase));

            if (dllMatches.Count() > 0)
            {
                var zipFile = dllMatches.First().Open();
                rawAssemblyData = new byte[zipFile.Length];
                zipFile.Read(rawAssemblyData, 0, rawAssemblyData.Length);
                return true;
            }
            else
            {
                rawAssemblyData = default!;
                return false;
            }
        }

        public void Dispose()
        {
            archive.Dispose();
        }
    }
}
