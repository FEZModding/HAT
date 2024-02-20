namespace HatModLoader.Source
{
    public interface IModLoader : IDisposable
    {
        public string ResourcePath { get; }

        public bool TryLoadMetadata(out ModMetadata metadata);
        public List<Asset> LoadAssets();
        public bool TryLoadAssembly(string assemblyName, out byte[] rawAssemblyData);
    }
}
