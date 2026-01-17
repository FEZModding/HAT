using HatModLoader.Source.Assets;
using HatModLoader.Source.FileProxies;

namespace HatModLoader.Source.ModDefinition
{
    public class AssetMod : IMod
    {
        public IFileProxy FileProxy { get; }

        public Metadata Metadata { get; }

        public List<Asset> Assets { get; }
    
        public AssetMod(IFileProxy fileProxy, Metadata metadata, List<Asset> assets)
        {
            FileProxy = fileProxy;
            Metadata = metadata;
            Assets = assets;
        }

        public void Dispose()
        {
            // Nothing
        }
    }
}