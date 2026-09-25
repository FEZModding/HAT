using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using FezEngine.Structure;
using Microsoft.Xna.Framework;

namespace HatModLoader.Source.AssemblyResolving
{
    internal static class FnaNativeLibraryResolver
    {
        private static readonly Lock Sync = new();

        private static readonly Dictionary<string, IntPtr> LoadedLibraries = new(StringComparer.Ordinal);

        private static Dictionary<string, string> _mappings;

        private static string _cRuntime;

        private static bool _registered;

        public static void Register()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            lock (Sync)
            {
                if (_registered)
                {
                    return;
                }

                var platform = OperatingSystem.IsLinux() ? "linux" :
                    OperatingSystem.IsMacOS() ? "osx" :
                    throw new PlatformNotSupportedException(
                        "FNA native library mappings are unavailable for this platform.");

                var configPath = Path.Combine(AppContext.BaseDirectory, "FNA.dll.config");
                var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var element in XDocument.Load(configPath).Descendants("dllmap"))
                {
                    if ((string)element.Attribute("os") != platform)
                    {
                        continue;
                    }

                    var import = (string)element.Attribute("dll");
                    var target = (string)element.Attribute("target");
                    if (!IsFileName(import) || !IsFileName(target) || !mappings.TryAdd(import, target))
                    {
                        throw new InvalidDataException($"Invalid FNA native library mapping in {configPath}.");
                    }
                }

                if (mappings.Count == 0)
                {
                    throw new InvalidDataException($"No FNA native library mappings for {platform} in {configPath}.");
                }

                _mappings = mappings;
                _cRuntime = platform == "linux" ? "libc.so.6" : "libSystem.B.dylib";
                NativeLibrary.SetDllImportResolver(typeof(Game).Assembly, Resolve);
                // FezEngine's OggStream imports memcpy from the Windows C runtime
                NativeLibrary.SetDllImportResolver(typeof(OggStream).Assembly, Resolve);
                _registered = true;
            }

            return;

            bool IsFileName(string value)
            {
                return !string.IsNullOrWhiteSpace(value) && value != "." && value != ".." &&
                       !value.Contains('/') && !value.Contains('\\') && !value.Contains(':');
            }
        }

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (string.Equals(libraryName, "msvcrt.dll", StringComparison.OrdinalIgnoreCase))
            {
                lock (Sync)
                {
                    if (!LoadedLibraries.TryGetValue(_cRuntime, out var cRuntimeHandle))
                    {
                        cRuntimeHandle = NativeLibrary.Load(_cRuntime);
                        LoadedLibraries.Add(_cRuntime, cRuntimeHandle);
                    }

                    return cRuntimeHandle;
                }
            }

            if (!_mappings.TryGetValue(libraryName, out var target))
            {
                return IntPtr.Zero;
            }

            lock (Sync)
            {
                if (LoadedLibraries.TryGetValue(target, out var handle))
                {
                    return handle;
                }

                var path = Path.Combine(AppContext.BaseDirectory, target);
                if (!File.Exists(path))
                {
                    throw new DllNotFoundException(
                        $"FNA native library '{libraryName}' requires '{path}', which is missing.");
                }

                try
                {
                    handle = NativeLibrary.Load(path);
                }
                catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
                {
                    throw new DllNotFoundException($"FNA native library '{libraryName}' could not load '{path}'.", ex);
                }

                LoadedLibraries.Add(target, handle);
                return handle;
            }
        }
    }
}