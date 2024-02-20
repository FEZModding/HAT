using HatModLoader.Source.Assets;
using HatModLoader.Source.ModDefinition;

namespace HatModLoader.Source.ModLoaders
{
    public interface IModLoader : IDisposable
    {
        public string ResourcePath { get; }

        public bool TryLoadMetadata(out ModMetadata metadata);
        public List<Asset> LoadAssets();
        public bool TryLoadAssembly(string assemblyName, out byte[] rawAssemblyData);
    }
}
