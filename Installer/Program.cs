using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using Microsoft.Win32;
using MonoMod;
using MonoMod.RuntimeDetour.HookGen;

namespace FEZ.HAT.Installer;

public static class Program
{
    private const string FezExecutable = "FEZ.exe";

    private static string? _userFezPath;

    public static void Main(string[] args)
    {
        ParseCommandLineArguments(args);
        PrintHeader();
        try
        {
            if (TryFindFezExecutable(out var fezPath))
            {
                var originalFezPath = CreateOriginalCopy(fezPath);
                ExtractHatDependencies(fezPath);
                var convertedFezPath = ConvertAssemblies(originalFezPath, fezPath);
                GenerateHooks(convertedFezPath);
                var hatPath = PatchAssemblies(convertedFezPath);
                WriteDeploymentMetadata(hatPath);
                CopyFnaFiles(originalFezPath, hatPath);
                CopyContentsFolder(originalFezPath, hatPath);
                PostInstallationCleanup(hatPath);
                PrintOutput(originalFezPath);
            }
        }
        catch (InstallerException ex)
        {
            Console.WriteLine($"[ERROR] {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Unexpected error: {ex}");
            Console.WriteLine("Please file a bug at https://github.com/FEZModding/HAT/issues");
        }

        WaitForUserInput();
    }

    private static void PrintHeader()
    {
        using var stream = GetResource("Installer.txt");
        using var logo = new StreamReader(stream);
        Console.WriteLine(logo.ReadToEnd());

        const int logoWidth = 50;
        const string version = $"{ThisAssembly.Git.BaseVersion.Major}." +
                               $"{ThisAssembly.Git.BaseVersion.Minor}." +
                               $"{ThisAssembly.Git.BaseVersion.Patch}";

        const string commit = ThisAssembly.Git.Branch +
                              "-" + ThisAssembly.Git.Commit;

        Console.WriteLine($"HAT Installer v{version} ({commit})".PadLeft(logoWidth));
        Console.WriteLine("Created by zerocker and FEZModding community".PadLeft(logoWidth));
        Console.WriteLine("HAT ASCII logo by Krzyhau".PadLeft(logoWidth));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine($"Platform: Windows ({RuntimeInformation.ProcessArchitecture})".PadLeft(logoWidth));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Console.WriteLine($"Platform: Linux ({RuntimeInformation.ProcessArchitecture})".PadLeft(logoWidth));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Console.WriteLine($"Platform: macOS ({RuntimeInformation.ProcessArchitecture})".PadLeft(logoWidth));
        }

        Console.WriteLine(); // separator
#if !DEBUG
        Console.WriteLine("Press Enter to proceed with installation");
        Console.Write("or press Ctrl+C to abort it...");
        Console.ReadLine();
#endif
    }

    private static void ParseCommandLineArguments(string[] args)
    {
        var queue = new Queue<string>(args);
        while (queue.Count > 0)
        {
            switch (queue.Dequeue().ToLowerInvariant())
            {
                case "-p" or "--path":
                {
                    _userFezPath = Path.GetFullPath(queue.Dequeue());
                    break;
                }
            }
        }
    }

    private static bool TryFindFezExecutable(out string executable)
    {
        var path = string.Empty;
        {
            Console.WriteLine("[HAT] Checking CLI \"--path\" or \"-p\" argument");
            if (_userFezPath != null)
                path = _userFezPath;
        }

        if (string.IsNullOrEmpty(path))
        {
            Console.WriteLine("[HAT] Checking current working directory");
            var cwd = Environment.CurrentDirectory;
            if (File.Exists(Path.Combine(cwd, FezExecutable)) ||
                File.Exists(Path.Combine(cwd, "Original", FezExecutable)))
            {
                path = cwd;
            }
        }

        if (string.IsNullOrEmpty(path))
        {
            Console.WriteLine("[HAT] Checking Steam library");
            string steamPath;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                steamPath = (string)Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                    "InstallPath",
                    string.Empty
                )!;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var candidates = new[]
                {
                    Path.Combine(home, ".steam", "steam"),
                    Path.Combine(home, ".local", "share", "Steam")
                };

                steamPath = Array.Find(candidates, Directory.Exists) ?? "";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var path1 = Path.Combine(home, "Library", "Application Support", "Steam");
                steamPath = Directory.Exists(path1) ? path1 : string.Empty;
            }
            else
            {
                steamPath = string.Empty;
            }

            if (!string.IsNullOrEmpty(steamPath))
            {
                var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdfPath))
                {
                    var text = File.ReadAllText(vdfPath);
                    var matches = Regex.Matches(text, @"""path""\s+""([^""]+)""");

                    foreach (Match m in matches)
                    {
                        var folder = m.Groups[1].Value.Replace(@"\\", @"\"); // unescape VDF backslashes
                        var candidate = Path.Combine(folder, "steamapps", "common", "FEZ");
                        if (Directory.Exists(candidate))
                        {
                            path = candidate;
                            break;
                        }
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(path))
        {
            Console.WriteLine("[HAT] Checking GOG library");
            string gogPath;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                gogPath = @"C:\Program Files (x86)\GOG Galaxy\Games\FEZ";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                gogPath = Path.Combine(home, "GOG Games", "FEZ");
            }
            else
            {
                // GOG has no macOS default for this title
                gogPath = string.Empty;
            }

            if (!string.IsNullOrEmpty(gogPath) && Directory.Exists(gogPath))
            {
                path = gogPath;
            }
        }

        if (!string.IsNullOrEmpty(path))
        {
            executable = Path.Combine(path, FezExecutable);
            if (File.Exists(executable) || File.Exists(Path.Combine(path, "Original", FezExecutable)))
            {
                Console.WriteLine($"[HAT] Executable found at {executable}");
                return true;
            }
        }

        executable = string.Empty;
        throw new InstallerException("Could not find FEZ. Use --path <dir> or run from the FEZ game directory.");
    }

    private static string CreateOriginalCopy(string fezPath)
    {
        var fezDir = Path.GetDirectoryName(fezPath)!;
        var originalDir = Path.Combine(fezDir, "Original");
        if (Directory.Exists(originalDir))
        {
            return Path.Combine(originalDir, Path.GetFileName(fezPath));
        }

        var tempDir = Path.Combine(Path.GetDirectoryName(fezDir)!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            foreach (var source in Directory.GetFiles(fezDir))
            {
                File.Move(source, Path.Combine(tempDir, Path.GetFileName(source)));
            }

            foreach (var source in Directory.GetDirectories(fezDir))
            {
                Directory.Move(source, Path.Combine(tempDir, Path.GetFileName(source)));
            }

            Directory.Move(tempDir, originalDir);
        }
        catch
        {
            if (Directory.Exists(tempDir))
            {
                foreach (var source in Directory.GetFiles(tempDir))
                {
                    File.Move(source, Path.Combine(fezDir, Path.GetFileName(source)));
                }

                foreach (var source in Directory.GetDirectories(tempDir))
                {
                    Directory.Move(source, Path.Combine(fezDir, Path.GetFileName(source)));
                }

                Directory.Delete(tempDir);
            }

            throw;
        }

        return Path.Combine(originalDir, Path.GetFileName(fezPath));
    }

    private static void ExtractHatDependencies(string path)
    {
        var gameDir = Path.GetDirectoryName(path)!;
        var hatDependenciesDir = Path.Combine(gameDir, "HATDependencies");
        if (Directory.Exists(hatDependenciesDir))
        {
            Console.WriteLine("[HAT] Clearing existing HATDependencies");
            Directory.Delete(hatDependenciesDir, recursive: true);
        }

        #region HAT modloader

        {
            using var stream = GetResource("HAT.zip");
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            Console.WriteLine("[HAT] Extracting mod loader dependencies");

            foreach (var entry in zip.Entries)
            {
                var destination = Path.Combine(gameDir, entry.FullName);
                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var source = entry.Open();
                using var file = File.Create(destination);
                source.CopyTo(file);
            }
        }

        #endregion

        #region Game app host

        {
            var appHostPath = Path.Combine(gameDir,
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "HAT.exe" : "HAT");

            using (var appHost = GetResource("HAT.AppHost"))
            {
                Console.WriteLine("[HAT] Extracting new game app host");
                using (var file = File.Create(appHostPath))
                {
                    appHost.CopyTo(file);
                }
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(appHostPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }

        #endregion

        #region .NET self-contained runtime

        var runtimeDir = Path.Combine(hatDependenciesDir, "Runtime");
        Directory.CreateDirectory(runtimeDir);
        using var runtime = GetResource("HAT.Runtime");
        Console.WriteLine("[HAT] Extracting .NET self-contained runtime");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ZipFile.ExtractToDirectory(runtime, runtimeDir);
        }
        else
        {
            using var gzip = new GZipStream(runtime, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, runtimeDir, overwriteFiles: false);
        }

        #endregion
    }

    private static string ConvertAssemblies(string originalFezPath, string fezPath)
    {
        var gameDir = Path.GetDirectoryName(originalFezPath)!;
        var gameExecutable = new FileInfo(originalFezPath);
        var gameAssemblies = new[]
        {
            gameExecutable,
            new FileInfo(Path.Combine(gameDir, "Common.dll")),
            new FileInfo(Path.Combine(gameDir, "ContentSerialization.dll")),
            new FileInfo(Path.Combine(gameDir, "EasyStorage.dll")),
            new FileInfo(Path.Combine(gameDir, "FNA.dll")),
            new FileInfo(Path.Combine(gameDir, "FezEngine.dll")),
            new FileInfo(Path.Combine(gameDir, "SimpleDefinitionLanguage.dll")),
            new FileInfo(Path.Combine(gameDir, "XnaWordWrapCore.dll"))
        };

        var fezDir = new DirectoryInfo(Path.GetDirectoryName(fezPath)!);
        Console.WriteLine("[HAT] Converting game assemblies to CoreCLR");

        var resolverDir = new DirectoryInfo(GetExtractedRuntimeDirectory(fezDir.FullName));
        var resolverInputs = new List<FileInfo>();

        var steamworksPath = Path.Combine(gameDir, "Steamworks.NET.dll");
        if (File.Exists(steamworksPath))
        {
            Console.WriteLine("[HAT] Creating inert Steamworks.NET assembly");
            var steamworksStub =
                AssemblyConverter.AssemblyStubber.Stub(new FileInfo(steamworksPath), fezDir, resolverDir);
            resolverInputs.Add(steamworksStub);
        }

        var converted =
            AssemblyConverter.AssemblyConverter.Convert(fezDir, resolverDir, resolverInputs, gameAssemblies);
        return converted[0].FullName;
    }

    private static string GetExtractedRuntimeDirectory(string gameDir)
    {
        var sharedRuntimeDir = Path.Combine(gameDir, "HATDependencies", "Runtime", "shared", "Microsoft.NETCore.App");
        var runtimeDirectories = Directory.Exists(sharedRuntimeDir)
            ? Directory.GetDirectories(sharedRuntimeDir)
            : [];

        if (runtimeDirectories.Length != 1 || !File.Exists(Path.Combine(runtimeDirectories[0], "mscorlib.dll")))
        {
            throw new InvalidOperationException(
                $"Expected one extracted .NET runtime with mscorlib.dll in {sharedRuntimeDir}.");
        }

        return runtimeDirectories[0];
    }

    private static void GenerateHooks(string hatPath)
    {
        var gameDir = Path.GetDirectoryName(hatPath)!;
        var monoModDir = Path.Combine(gameDir, "HATDependencies", "MonoMod");
        var runtimeDir = GetExtractedRuntimeDirectory(gameDir);
        Console.WriteLine("[HAT] Generating MonoMod hooks");

        var gameAssembliesToPatch = new[]
        {
            hatPath,
            Path.Combine(gameDir, "FezEngine.dll"),
            Path.Combine(gameDir, "FNA.dll")
        };

        foreach (var assemblyPath in gameAssembliesToPatch)
        {
            var outputPath = Path.Combine(gameDir, "MMHOOK_" + Path.GetFileName(assemblyPath));
            using var modder = new MonoModder
            {
                InputPath = assemblyPath,
                OutputPath = outputPath,
                MissingDependencyThrow = false
            };
            modder.DependencyDirs.Add(monoModDir);
            modder.DependencyDirs.Add(runtimeDir);
            modder.Read();
            modder.MapDependencies();

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var generator = new HookGenerator(modder, Path.GetFileName(outputPath));
            using var output = generator.OutputModule;
            generator.Generate();
            output.Write(outputPath);
        }
    }

    private static string PatchAssemblies(string convertedFezPath)
    {
        var gameDir = Path.GetDirectoryName(convertedFezPath)!;
        var hatPath = Path.Combine(gameDir, "HAT.dll");
        Console.WriteLine("[HAT] Applying HAT patch");

        using var modder = new MonoModder
        {
            InputPath = convertedFezPath,
            OutputPath = hatPath,
            MissingDependencyThrow = false
        };

        modder.DependencyDirs.Add(Path.Combine(gameDir, "HATDependencies", "MonoMod"));
        modder.DependencyDirs.Add(Path.Combine(gameDir, "HATDependencies", "FEZRepacker.Core"));
        modder.DependencyDirs.Add(GetExtractedRuntimeDirectory(gameDir));
        modder.Read();
        modder.ReadMod(Path.Combine(gameDir, "FEZ.HAT.mm.dll"));
        modder.MapDependencies();
        modder.AutoPatch();
        modder.WriterParameters.WriteSymbols = false;
        modder.WriterParameters.SymbolWriterProvider = null;
        modder.Write();

        return hatPath;
    }

    private static void WriteDeploymentMetadata(string hatPath)
    {
        Console.WriteLine("[HAT] Writing .NET deployment metadata");

        #region Runtime Configuration

        {
            var runtimeConfig = new RuntimeConfig();
            var path = Path.ChangeExtension(hatPath, "runtimeconfig.json");
            using var stream = File.Create(path);
            JsonSerializer.Serialize(stream, runtimeConfig, DeploymentJsonContext.Default.RuntimeConfig);
        }

        #endregion

        #region Dependency Manifest

        {
            var deps = Deps.Create(hatPath);
            var path = Path.ChangeExtension(hatPath, "deps.json");
            using var stream = File.Create(path);
            JsonSerializer.Serialize(stream, deps, DeploymentJsonContext.Default.Deps);
        }

        #endregion
    }

    private static void CopyFnaFiles(string originalFezPath, string hatPath)
    {
        var originalDir = Path.GetDirectoryName(originalFezPath)!;
        var hatDir = Path.GetDirectoryName(hatPath)!;

        using var configResource = GetResource("FNA.dll.config");
        var config = FnaDllConfig.Load(configResource, leaveOpen: true);

        Console.WriteLine("[HAT] Copying native libraries");
        File.Copy(Path.Combine(originalDir, "gamecontrollerdb.txt"),
            Path.Combine(hatDir, "gamecontrollerdb.txt"), overwrite: true);

        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceDir = FnaDllConfigExtensions.GetOperatingSystem()
            .GetLibrariesDirectory(originalDir);

        foreach (var mapping in config.Dependencies)
        {
            var target = mapping.Target;
            if (string.IsNullOrWhiteSpace(target) || Path.GetFileName(target) != target || !copied.Add(target))
            {
                throw new InstallerException($"Invalid or duplicate FNA native target: '{target}'.");
            }

            var source = Path.Combine(sourceDir, target);
            if (!File.Exists(source))
            {
                if (mapping.Dll is "SDL2_image.dll" or "libtheoraplay.dll")
                {
                    continue;
                }

                throw new InstallerException($"Required FNA native library is missing: {source}");
            }

            File.Copy(source, Path.Combine(hatDir, target), overwrite: true);
        }

        configResource.Seek(0, SeekOrigin.Begin);
        using var configDestination = File.Create(Path.Combine(hatDir, "FNA.dll.config"));
        configResource.CopyTo(configDestination);
    }

    private static void CopyContentsFolder(string originalFezPath, string hatPath)
    {
        var sourceDir = Path.Combine(Path.GetDirectoryName(originalFezPath)!, "Content");
        var destinationDir = Path.Combine(Path.GetDirectoryName(hatPath)!, "Content");

        Console.WriteLine("[HAT] Copying Content folder");
        Directory.CreateDirectory(destinationDir);
        foreach (var directory in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationDir, Path.GetRelativePath(sourceDir, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(destinationDir, Path.GetRelativePath(sourceDir, file));
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void PostInstallationCleanup(string hatPath)
    {
        var gameDir = Path.GetDirectoryName(hatPath)!;
        Console.WriteLine("[HAT] Removing installation intermediates");

        var intermediates = new[]
        {
            "FEZ.dll",
            "FEZ.HAT.mm.dll",
            "FEZ.HAT.mm.pdb",
            "MMHOOK_FEZ.dll",
            "MMHOOK_FezEngine.dll",
            "MMHOOK_FNA.dll"
        };

        foreach (var file in intermediates)
        {
            File.Delete(Path.Combine(gameDir, file));
        }
    }

    private static void PrintOutput(string originalFezPath)
    {
        var executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "HAT.exe" : "./HAT";
        Console.WriteLine($"Done! Run {executable} to launch the modded game :>");
        Console.WriteLine($"The vanilla game is available at: {originalFezPath}");
    }

    private static void WaitForUserInput()
    {
#if !DEBUG
        if (!Console.IsInputRedirected)
        {
            Console.Write("Press Enter to exit...");
            Console.ReadLine();
        }
#endif
    }

    private static Stream GetResource(string resource)
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
               ?? throw new InstallerException($"Resource '{resource}' not found in installer - rebuild the solution.");
    }
}

internal class InstallerException : Exception
{
    public InstallerException(string message) : base(message)
    {
    }
}
