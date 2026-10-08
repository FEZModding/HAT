using System.Reflection;
using System.Runtime.Loader;
using HatModLoader.Source.FileProxies;
using Microsoft.Xna.Framework;

namespace HatModLoader.Source.ModDefinition;

internal sealed class ModAssemblyLoadContext : AssemblyLoadContext
{
    private readonly Metadata _metadata;

    private readonly string _entryPath;

    private readonly AssemblyDependencyResolver _resolver;

    private readonly Dictionary<string, string> _legacyAssemblies = new(StringComparer.OrdinalIgnoreCase);

    public ModAssemblyLoadContext(IFileProxy proxy, Metadata metadata) : base($"HAT mod: {metadata.Name}", true)
    {
        _metadata = metadata;
        _entryPath = EntryPath(proxy.CodeRootPath, metadata.LibraryName);
        if (!File.Exists(_entryPath))
        {
            throw new FileNotFoundException($"Mod '{metadata.Name}' entry assembly is missing", _entryPath);
        }

        if (File.Exists(Path.ChangeExtension(_entryPath, ".deps.json")))
        {
            _resolver = new AssemblyDependencyResolver(_entryPath);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(proxy.CodeRootPath, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var name = AssemblyName.GetAssemblyName(file).Name;
                    _legacyAssemblies.TryAdd(name, file);
                }
                catch (BadImageFormatException)
                {
                }
            }
        }
    }

    public Assembly LoadEntryAssembly()
    {
        return LoadFromAssemblyPath(_entryPath);
    }

    protected override Assembly Load(AssemblyName assemblyName)
    {
        // FEZ ships different FNA versions across operating systems (16.11 and 16.12, to be exact), so mods
        // may reference a different version. Resolve those references to the game's FNA so mod components
        // share its framework types.
        if (string.Equals(assemblyName.Name, "FNA", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(Game).Assembly;
        }

        // Mod-to-mod references are permitted only by Metadata.xml, and retain the provider's type identity
        foreach (var dependency in _metadata.Dependencies ?? Array.Empty<Metadata.DependencyInfo>())
        {
            var provider = Hat.Instance?.Mods
                .FirstOrDefault(mc =>
                    string.Equals(mc.Metadata.Name, dependency.Name, StringComparison.OrdinalIgnoreCase));

            var providerAssembly = provider?.CodeMod?.Assembly;
            if (providerAssembly != null &&
                string.Equals(providerAssembly.GetName().Name, assemblyName.Name!, StringComparison.OrdinalIgnoreCase))
            {
                return providerAssembly;
            }
        }

        var path = _resolver?.ResolveAssemblyToPath(assemblyName);
        if (path == null && _resolver == null)
        {
            _legacyAssemblies.TryGetValue(assemblyName.Name!, out path);
        }

        if (path == null)
        {
            return null;
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Mod '{_metadata.Name}' declares a missing assembly", path);
        }

        return LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (path == null)
        {
            return IntPtr.Zero;
        }

        if (!File.Exists(path))
        {
            throw new DllNotFoundException($"Mod '{_metadata.Name}' declares a missing native library: {path}");
        }

        return LoadUnmanagedDllFromPath(path);
    }

    private static string EntryPath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
        {
            throw new InvalidDataException($"Invalid mod path: {relative}");
        }

        var path = Path.GetFullPath(Path.Combine(root, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidDataException($"Mod path escapes its directory: {relative}");
        }

        return path;
    }
}
