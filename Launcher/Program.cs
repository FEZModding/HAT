using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace FEZ.HAT.Launcher;

internal static class Program
{
    private const string ManagedExecutable = "HAT.exe";

    private static readonly ConcurrentDictionary<Assembly, byte> RegisteredAssemblies = new();

    private static readonly bool UseUnixCompatibility = !OperatingSystem.IsWindows();

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var gameDirectory = AppContext.BaseDirectory;
            Environment.CurrentDirectory = gameDirectory;

            AppDomain.CurrentDomain.AssemblyLoad += (_, eventArgs) => RegisterNativeResolver(eventArgs.LoadedAssembly);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                RegisterNativeResolver(assembly);
            }

            var gamePath = Path.Combine(gameDirectory, ManagedExecutable);
            if (!File.Exists(gamePath))
            {
                throw new FileNotFoundException($"Patched {ManagedExecutable} is missing.", gamePath);
            }

            var game = UseUnixCompatibility
                ? new UnixGameLoader(gameDirectory).LoadGame(gamePath)
                : Assembly.LoadFrom(gamePath);

            var entryPoint = game.EntryPoint ??
                             throw new MissingMethodException($"{ManagedExecutable} has no entry point.");

            var parameters = entryPoint.GetParameters().Length switch
            {
                0 => null,
                1 => new object?[] { args },
                _ => throw new MissingMethodException($"{ManagedExecutable} has an unsupported entry-point signature.")
            };

            try
            {
                var result = entryPoint.Invoke(null, parameters);
                return result switch
                {
                    int exitCode => exitCode,
                    Task<int> task => task.GetAwaiter().GetResult(),
                    Task task => Wait(task),
                    _ => 0
                };
            }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
        catch (Exception error)
        {
            ReportFailure(error);
            return 1;
        }
    }

    private static int Wait(Task task)
    {
        task.GetAwaiter().GetResult();
        return 0;
    }

    private static void ReportFailure(Exception error)
    {
        Console.Error.WriteLine(error);
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "HAT.Launcher.log"),
                $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Preserve the original failure when the game directory is read-only.
        }
    }

    private static void RegisterNativeResolver(Assembly assembly)
    {
        if (assembly.GetName().Name == "MonoMod.Utils")
        {
            // MonoMod 22's DynamicMethod emitter relies on runtime internals removed in .NET 8.
            // Its Cecil backend is portable across the legacy runtimes and CoreCLR.
            var dmd = assembly.GetType("MonoMod.Utils.DynamicMethodDefinition");
            dmd?.GetField("_PreferCecil", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, true);
        }

        if (!UseUnixCompatibility)
        {
            return;
        }

        var name = assembly.GetName().Name;
        if (name is not ("FNA" or "FezEngine" or "Mono.Posix" or "Steamworks.NET" or "System.Drawing") ||
            !RegisteredAssemblies.TryAdd(assembly, 0))
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(assembly, ResolveUnixNativeLibrary);
    }

    private static IntPtr ResolveUnixNativeLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        var mappedName = OperatingSystem.IsMacOS()
            ? MapMacLibrary(libraryName)
            : MapLinuxLibrary(libraryName);

        if (mappedName is null)
        {
            return IntPtr.Zero;
        }

        if (libraryName == "libvorbisfile.dll")
        {
            LoadBundled(OperatingSystem.IsMacOS() ? "libogg.0.dylib" : "libogg.so.0");
            LoadBundled(OperatingSystem.IsMacOS() ? "libvorbis.0.dylib" : "libvorbis.so.0");
        }
        else if (libraryName == "CSteamworks")
        {
            LoadBundled(OperatingSystem.IsMacOS() ? "libsteam_api.dylib" : "libsteam_api.so");
        }

        foreach (var directory in NativeDirectories())
        {
            var path = Path.Combine(directory, mappedName);
            if (File.Exists(path))
            {
                return NativeLibrary.Load(path);
            }
        }

        return NativeLibrary.TryLoad(mappedName, assembly, searchPath, out var handle) ? handle : IntPtr.Zero;
    }

    private static string? MapLinuxLibrary(string name) => name switch
    {
        "SDL2.dll" => "libSDL2-2.0.so.0",
        "SDL2_image.dll" => "libSDL2_image-2.0.so.0",
        "soft_oal.dll" => "libopenal.so.1",
        "MojoShader.dll" => "libmojoshader.so",
        "libvorbisfile.dll" => "libvorbisfile.so.3",
        "libtheoraplay.dll" => "libtheoraplay.so",
        "MonoPosixHelper" => "libMonoPosixHelper.so",
        "CSteamworks" => "libCSteamworks.so",
        "msvcrt" or "msvcrt.dll" or "intl" or "libc" => "libc.so.6",
        "libX11" => "libX11.so.6",
        "libcups" => "libcups.so.2",
        _ => null
    };

    private static string? MapMacLibrary(string name) => name switch
    {
        "SDL2.dll" => "libSDL2-2.0.0.dylib",
        "SDL2_image.dll" => "libSDL2_image-2.0.0.dylib",
        "soft_oal.dll" => "libopenal.1.dylib",
        "MojoShader.dll" => "libmojoshader.dylib",
        "libvorbisfile.dll" => "libvorbisfile.3.dylib",
        "libtheoraplay.dll" => "libtheoraplay.dylib",
        "MonoPosixHelper" => "libMonoPosixHelper.dylib",
        "CSteamworks" => "libCSteamworks.dylib",
        "msvcrt" or "msvcrt.dll" or "intl" or "libc" => "/usr/lib/libSystem.B.dylib",
        "libcups" => "/usr/lib/libcups.2.dylib",
        _ => null
    };

    private static IEnumerable<string> NativeDirectories()
    {
        var root = AppContext.BaseDirectory;
        yield return root;
        yield return Path.Combine(root, OperatingSystem.IsMacOS() ? "osx" : "lib64");

        if (OperatingSystem.IsLinux())
        {
            yield return Path.Combine(root, "lib");
        }
    }

    private static void LoadBundled(string name)
    {
        foreach (var directory in NativeDirectories())
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                NativeLibrary.Load(path);
                return;
            }
        }
    }

    private sealed class UnixGameLoader
    {
        private const uint Required32Bit = 0x00000002;

        private const uint Preferred32Bit = 0x00020000;

        private readonly string _gameDirectory;

        private readonly object _loadLock = new();

        public UnixGameLoader(string gameDirectory)
        {
            _gameDirectory = gameDirectory;
            AssemblyLoadContext.Default.Resolving += ResolveGameAssembly;
        }

        public Assembly LoadGame(string path)
        {
            return LoadPatchedAssembly(path);
        }

        private Assembly? ResolveGameAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName.Name))
            {
                return null;
            }

            // The native Linux and macOS releases keep Mono's framework assemblies beside
            // FEZ.exe for MonoKickstart. Loading those copies into CoreCLR produces invalid
            // type hierarchies (most notably System.Object from the legacy mscorlib.dll).
            // Returning null lets CoreCLR satisfy framework references from its own bundle.
            if (IsFrameworkAssembly(assemblyName.Name))
            {
                return null;
            }

            var candidate = Path.Combine(_gameDirectory, assemblyName.Name + ".dll");
            if (!File.Exists(candidate))
            {
                return null;
            }

            lock (_loadLock)
            {
                var loaded = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(
                    assembly => AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName));
                return loaded ?? LoadPatchedAssembly(candidate);
            }
        }

        private static bool IsFrameworkAssembly(string name)
        {
            return name is "mscorlib" or "netstandard" or "System" or "Microsoft.CSharp"
                   || name.StartsWith("System.", StringComparison.Ordinal);
        }

        private static Assembly LoadPatchedAssembly(string path)
        {
            var image = File.ReadAllBytes(path);
            using MemoryStream inspectionStream = new(image, writable: false);
            using PEReader reader = new(inspectionStream, PEStreamOptions.LeaveOpen);

            var corHeaderOffset = reader.PEHeaders.CorHeaderStartOffset;
            if (corHeaderOffset < 0)
            {
                throw new BadImageFormatException($"Managed PE file has no CLR header: {path}");
            }

            var flagsBytes = image.AsSpan(corHeaderOffset + 16, sizeof(uint));
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(flagsBytes);
            BinaryPrimitives.WriteUInt32LittleEndian(flagsBytes, flags & ~(Required32Bit | Preferred32Bit));

            using MemoryStream loadStream = new(image, writable: false);
            var assembly = AssemblyLoadContext.Default.LoadFromStream(loadStream);
            RegisterNativeResolver(assembly);
            return assembly;
        }
    }
}
