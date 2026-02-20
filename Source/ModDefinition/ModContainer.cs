using System.Runtime.InteropServices;
using Common;
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
    
    private readonly List<IntPtr> _nativeLibraryHandles = new();

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
            AppDomain.CurrentDomain.ProcessExit += UnloadNativeLibraries;
            LoadNativeLibraries();
            CodeMod?.Initialize(game);
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
    
    private void UnloadNativeLibraries(object sender, EventArgs e)
    {
        lock (_nativeLibraryHandles)
        {
            foreach (var library in _nativeLibraryHandles)
            {
                FileProxy.UnloadLibrary(library);
            }
        }
    }
    
    private void LoadNativeLibraries()
    {
        if (Metadata.NativeDependencies == null || Metadata.NativeDependencies.Length == 0)
        {
            return;
        }
        
        var platformSpecific = Metadata.NativeDependencies
            .Where(library => RuntimeInformation.IsOSPlatform(library.Platform))
            .Where(library => RuntimeInformation.ProcessArchitecture == library.Architecture)
            .ToArray();

        if (platformSpecific.Length == 0)
        {
            var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "OSX"
                : "Unknown";
            var cpu = RuntimeInformation.ProcessArchitecture;
            throw new PlatformNotSupportedException($"There're no native libraries found for " +
                                                    $"Platform=\"{os}\" Architecture=\"{cpu}\"");
        }

        foreach (var library in platformSpecific)
        {
            if (!FileProxy.FileExists(library.Path))
            {
                throw new DllNotFoundException($"There's no native library found at: {library.Path}");
            }

            var libraryHandle = FileProxy.LoadLibrary(library.Path);
            if (libraryHandle == IntPtr.Zero)
            {
                Logger.Log(Metadata.Name, $"Unable to load native library: {library.Path}"); 
                continue;
            }
            
            Logger.Log(Metadata.Name, $"Native library successfully loaded: {library.Path}"); 
            _nativeLibraryHandles.Add(libraryHandle);
        }
    }
}