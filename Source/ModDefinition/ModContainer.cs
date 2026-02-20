using FezEngine.Tools;
using HatModLoader.Source.AssemblyResolving;
using HatModLoader.Source.Assets;
using HatModLoader.Source.FileProxies;
using Microsoft.Xna.Framework;

namespace HatModLoader.Source.ModDefinition;

public class ModContainer : IDisposable
{
    public IFileProxy FileProxy { get; }

    public Metadata Metadata { get; }
    
    public AssetMod AssetMod { get; internal set; }
    
    public CodeMod CodeMod { get; internal set; }
    
    private IAssemblyResolver _assemblyResolver;

    public ModContainer(IFileProxy fileProxy, Metadata metadata)
    {
        FileProxy = fileProxy;
        Metadata = metadata;
    }

    public void Initialize(Game game)
    {
        if (CodeMod != null)
        {
            _assemblyResolver = new ModInternalAssemblyResolver(this);
            AssemblyResolverRegistry.Register(_assemblyResolver);
            CodeMod?.Initialize(game, Metadata.Entrypoint);
        }
    }

    public void InjectComponents()
    {
        foreach (var component in CodeMod?.Components ?? [])
        {
            ServiceHelper.AddComponent(component);
        }
    }

    public List<Asset> GetAssets()
    {
        return AssetMod?.Assets ?? [];
    }

    public void Dispose()
    {
        foreach (var component in CodeMod?.Components ?? [])
        {
            ServiceHelper.RemoveComponent(component);
        }

        if (_assemblyResolver != null)
        {
            AssemblyResolverRegistry.Unregister(_assemblyResolver);
        }
    }
}