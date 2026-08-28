using System.Reflection;
using System.Runtime.InteropServices;

namespace HatModLoader.Source
{
    internal static class CoreClrCompatibility
    {
        private static readonly HashSet<Assembly> RegisteredAssemblies = new();

        private static readonly bool IsUnix = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private static readonly Type NativeLibraryType =
            Type.GetType("System.Runtime.InteropServices.NativeLibrary, System.Runtime.InteropServices");

        private static readonly MethodInfo NativeLibraryLoad = NativeLibraryType?.GetMethod(
            "Load",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(string) },
            null);

        public static void Initialize()
        {
            Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;

            AppDomain.CurrentDomain.AssemblyLoad += (_, eventArgs) => RegisterAssembly(eventArgs.LoadedAssembly);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                RegisterAssembly(assembly);
            }
        }

        private static void RegisterAssembly(Assembly assembly)
        {
            if (assembly.GetName().Name == "MonoMod.Utils")
            {
                // MonoMod 22's dynamic method emitter relies on runtime internals which
                // are unavailable on modern CoreCLR. Its Cecil backend remains portable.
                var dmd = assembly.GetType("MonoMod.Utils.DynamicMethodDefinition");
                dmd?.GetField("_PreferCecil", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, true);
            }

            if (!IsUnix || NativeLibraryType == null)
            {
                return;
            }

            var name = assembly.GetName().Name;
            if (name is not ("FNA" or "FezEngine" or "Mono.Posix" or "Steamworks.NET" or "System.Drawing"))
            {
                return;
            }

            lock (RegisteredAssemblies)
            {
                if (!RegisteredAssemblies.Add(assembly))
                {
                    return;
                }
            }

            var method = NativeLibraryType.GetMethod(
                "SetDllImportResolver",
                BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                return;
            }

            var resolverType = method.GetParameters()[1].ParameterType;
            var resolverMethod = typeof(CoreClrCompatibility).GetMethod(
                nameof(ResolveUnixNativeLibrary),
                BindingFlags.NonPublic | BindingFlags.Static);
            var resolver = Delegate.CreateDelegate(resolverType, resolverMethod);
            method.Invoke(null, new object[] { assembly, resolver });
        }

        private static IntPtr ResolveUnixNativeLibrary(
            string libraryName,
            Assembly assembly,
            DllImportSearchPath? searchPath)
        {
            var mappedName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? MapMacLibrary(libraryName)
                : MapLinuxLibrary(libraryName);

            if (mappedName == null)
            {
                return IntPtr.Zero;
            }

            if (libraryName == "libvorbisfile.dll")
            {
                LoadBundled(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libogg.0.dylib" : "libogg.so.0");
                LoadBundled(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libvorbis.0.dylib" : "libvorbis.so.0");
            }
            else if (libraryName == "CSteamworks")
            {
                LoadBundled(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libsteam_api.dylib" : "libsteam_api.so");
            }

            foreach (var directory in NativeDirectories())
            {
                var path = Path.Combine(directory, mappedName);
                if (File.Exists(path))
                {
                    return LoadNativeLibrary(path);
                }
            }

            return IntPtr.Zero;
        }

        private static string MapLinuxLibrary(string name) => name switch
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

        private static string MapMacLibrary(string name) => name switch
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
            var root = AppDomain.CurrentDomain.BaseDirectory;
            yield return root;
            yield return Path.Combine(root, RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "lib64");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
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
                    LoadNativeLibrary(path);
                    return;
                }
            }
        }

        private static IntPtr LoadNativeLibrary(string path)
        {
            return NativeLibraryLoad == null
                ? IntPtr.Zero
                : (IntPtr)NativeLibraryLoad.Invoke(null, new object[] { path });
        }
    }
}
