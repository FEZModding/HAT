using FezEngine.Tools;
using HatModLoader.Source.Assets;
using HatModLoader.Source.FileProxies;
using Microsoft.Xna.Framework;

namespace HatModLoader.Source.ModDefinition;

public class ModContainer : IDisposable
{
    public IFileProxy FileProxy { get; }

    public Metadata Metadata { get; }

    public AssetMod AssetMod { get; internal set; }

    public ModText ModText { get; internal set; }

    public CodeMod CodeMod { get; internal set; }

    private ModAssemblyLoadContext _loadContext;

    public ModContainer(IFileProxy fileProxy, Metadata metadata)
    {
        FileProxy = fileProxy;
        Metadata = metadata;
    }

    public void Initialize(Game game)
    {
        if (CodeMod != null)
        {
            _loadContext = new ModAssemblyLoadContext(this);
            CodeMod.Initialize(game, Metadata.Entrypoint, _loadContext);
        }
    }

    public void InjectComponents()
    {
        foreach (var component in CodeMod?.Components ?? new List<GameComponent>())
        {
            ServiceHelper.AddComponent(component);
        }
    }

    public IEnumerable<Asset> GetAssets()
    {
        return AssetMod?.Assets ?? new List<Asset>();
    }

    public IEnumerable<Asset> ReloadAssets()
    {
        FileProxy.Refresh();
        List<Asset> changed;
        if (AssetMod == null)
        {
            if (!AssetMod.TryLoad(FileProxy, out var assetMod))
            {
                return new List<Asset>();
            }

            AssetMod = assetMod;
            changed = AssetMod.Assets.ToList();
        }
        else
        {
            changed = AssetMod.Reload(FileProxy).ToList();
        }

        if (changed.Any(asset => asset.AssetType == AssetType.ModTextResource))
        {
            if (ModText == null)
            {
                ModText.TryLoad(AssetMod, out var modText);
                ModText = modText;
            }
            else if (!ModText.Reload(AssetMod))
            {
                ModText = null;
            }
        }

        return changed;
    }

    public void Dispose()
    {
        foreach (var component in CodeMod?.Components ?? new List<GameComponent>())
        {
            ServiceHelper.RemoveComponent(component);
        }

        _loadContext?.Unload();
    }
}