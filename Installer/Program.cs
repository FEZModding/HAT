using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using MonoMod;
using MonoMod.RuntimeDetour.HookGen;

namespace FEZ.HAT.Installer;

public static class Program
{
    private enum WindowsError
    {
        Cancelled = 1223
    }

    private const string FezExecutable = "FEZ.exe";

    private const string OriginalFolder = "Original";

    private static readonly string FezLauncher =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "FEZ.exe" : "FEZ";

    private static readonly string AppHostLauncher =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "HAT.exe" : "HAT";

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
                LinkContentsFolder(originalFezPath, hatPath);
                RenameExecutable(hatPath);
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
        const string version = ThisAssembly.Constants.HatVersion;
        const string commit = ThisAssembly.Git.Branch + "-" + ThisAssembly.Git.Commit;

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
            {
                path = _userFezPath;
            }
        }

        if (string.IsNullOrEmpty(path))
        {
            Console.WriteLine("[HAT] Checking current working directory");
            var cwd = Environment.CurrentDirectory;
            if (HasFezExecutable(cwd))
            {
                path = cwd;
            }
        }

        if (string.IsNullOrEmpty(path))
        {
            Console.WriteLine("[HAT] Checking installer directory");
            var installerDir = AppContext.BaseDirectory;
            if (HasFezExecutable(installerDir))
            {
                path = installerDir;
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
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                        {
                            candidate = Path.Combine(candidate, "FEZ.app", "Contents", "MacOS");
                        }

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
            if (HasFezExecutable(path))
            {
                Console.WriteLine($"[HAT] Executable found at {executable}");
                return true;
            }
        }

        executable = string.Empty;
        throw new InstallerException(
            "Could not find FEZ. Use --path <dir> or place the installer in the FEZ game directory.");
    }

    private static bool HasFezExecutable(string directory)
    {
        return File.Exists(Path.Combine(directory, FezExecutable)) ||
               File.Exists(Path.Combine(directory, OriginalFolder, FezExecutable));
    }

    private static string CreateOriginalCopy(string fezPath)
    {
        var fezDir = Path.GetDirectoryName(fezPath)!;
        var originalDir = Path.Combine(fezDir, OriginalFolder);
        if (Directory.Exists(originalDir))
        {
            RestoreOriginalExecutablePermissions(originalDir);
            RestoreGogFiles(originalDir, fezDir);
            return Path.Combine(originalDir, Path.GetFileName(fezPath));
        }

        // Back up distro entries only so upgrades keep mods and user data beside the game
        var platform = OriginalFilesExtensions.GetPlatform();
        using var allowlistResource = GetResource("OriginalFiles.xml");
        var allowlist = OriginalFiles.Load(allowlistResource, platform);
        var comparer = platform == OriginalFiles.Platform.Windows
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var files = allowlist.Files.ToHashSet(comparer);
        var directories = allowlist.Directories.ToHashSet(comparer);

        // GOG metadata must accompany the vanilla copy and remain available to its client
        foreach (var source in Directory.EnumerateFiles(fezDir, "goggame-*", SearchOption.TopDirectoryOnly))
        {
            files.Add(Path.GetFileName(source));
        }

        var tempDir = Path.Combine(Path.GetDirectoryName(fezDir)!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            foreach (var source in Directory.GetFiles(fezDir))
            {
                if (files.Contains(Path.GetFileName(source)))
                {
                    File.Move(source, Path.Combine(tempDir, Path.GetFileName(source)));
                }
            }

            foreach (var source in Directory.GetDirectories(fezDir))
            {
                if (directories.Contains(Path.GetFileName(source)))
                {
                    Directory.Move(source, Path.Combine(tempDir, Path.GetFileName(source)));
                }
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

        RestoreOriginalExecutablePermissions(originalDir);
        RestoreGogFiles(originalDir, fezDir);
        return Path.Combine(originalDir, Path.GetFileName(fezPath));
    }

    private static void RestoreOriginalExecutablePermissions(string originalDir)
    {
        if (OperatingSystem.IsLinux() ||  OperatingSystem.IsMacOS())
        {
            foreach (var executable in new[] { "FEZ", "FEZ.bin.x86", "FEZ.bin.x86_64", "FEZ.bin.osx" })
            {
                var path = Path.Combine(originalDir, executable);
                if (File.Exists(path))
                {
                    var mode = File.GetUnixFileMode(path);
                    File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                }
            }
        }
    }

    private static void RestoreGogFiles(string originalDir, string gameDir)
    {
        foreach (var source in Directory.EnumerateFiles(originalDir, "goggame-*", SearchOption.TopDirectoryOnly))
        {
            var destination = Path.Combine(gameDir, Path.GetFileName(source));
            if (!File.Exists(destination))
            {
                File.Copy(source, destination);
            }
        }
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
            using (var appHost = GetResource("HAT.AppHost"))
            {
                Console.WriteLine("[HAT] Extracting new game app host");
                using (var file = File.Create(Path.Combine(gameDir, AppHostLauncher)))
                {
                    appHost.CopyTo(file);
                }
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(Path.Combine(gameDir, AppHostLauncher),
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
            InstallSteamworks(fezDir.FullName);
            resolverInputs.Add(new FileInfo(Path.Combine(fezDir.FullName, "Steamworks.NET.dll")));
        }

        var converted =
            AssemblyConverter.AssemblyConverter.Convert(fezDir, resolverDir, resolverInputs, gameAssemblies);
        return converted[0].FullName;
    }

    private static void InstallSteamworks(string gameDir)
    {
        string platform;
        string nativeLibrary;
        string nativeEntry;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            platform = "Windows-x86";
            nativeLibrary = "steam_api.dll";
            nativeEntry = $"{platform}/{nativeLibrary}";
        }
        else
        {
            platform = "OSX-Linux-x64";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                nativeLibrary = "libsteam_api.dylib";
                nativeEntry = $"{platform}/steam_api.bundle/Contents/MacOS/{nativeLibrary}";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                nativeLibrary = "libsteam_api.so";
                nativeEntry = $"{platform}/{nativeLibrary}";
            }
            else
            {
                throw new PlatformNotSupportedException();
            }
        }

        Console.WriteLine("[HAT] Replacing old Steamworks.NET with new one");
        using var archiveResource = GetResource("Steamworks.NET.zip");
        using var archive = new ZipArchive(archiveResource, ZipArchiveMode.Read);
        Extract($"{platform}/Steamworks.NET.dll", "Steamworks.NET.dll");
        Extract(nativeEntry, nativeLibrary);

        return;

        void Extract(string entryName, string fileName)
        {
            var entry = archive.GetEntry(entryName)!;
            entry.ExtractToFile(Path.Combine(gameDir, fileName), overwrite: true);
        }
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
            var gameDir = Path.GetDirectoryName(hatPath)!;
            var dependencyDirectories = new[]
            {
                Path.Combine(gameDir, "HATDependencies", "MonoMod"),
                Path.Combine(gameDir, "HATDependencies", "FEZRepacker.Core")
            };

            var deps = Deps.Create(hatPath, dependencyDirectories);
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

    private static void LinkContentsFolder(string originalFezPath, string hatPath)
    {
        // Share the root assets with the vanilla backup without moving Content out of the game
        var sourceDir = Path.Combine(Path.GetDirectoryName(hatPath)!, "Content");
        var destinationDir = Path.Combine(Path.GetDirectoryName(originalFezPath)!, "Content");

        var destinationInfo = new DirectoryInfo(destinationDir);
        if (destinationInfo is { Exists: true, LinkTarget: not null })
        {
            Console.WriteLine("[HAT] Content folder is already linked");
            return;
        }

        if (!destinationInfo.Exists)
        {
            // Windows always delegates link creation to an elevated helper
            if (OperatingSystem.IsWindows())
            {
                if (TryLinkContentsFolderAsAdministrator(sourceDir, destinationDir))
                {
                    Console.WriteLine("[HAT] Linked Original/Content folder to root Content");
                    return;
                }

                Console.WriteLine("[HAT] Could not link Content folder; copying it instead");
            }
            else
            {
                try
                {
                    var target = Path.GetRelativePath(Path.GetDirectoryName(destinationDir)!, sourceDir);
                    Directory.CreateSymbolicLink(destinationDir, target);
                    Console.WriteLine("[HAT] Linked Original/Content folder to root Content");
                    return;
                }
                catch (Exception e) when (e is UnauthorizedAccessException or IOException
                                              or PlatformNotSupportedException)
                {
                    Console.WriteLine($"[HAT] Could not link Content folder ({e.Message}); copying it instead");
                }
            }
        }

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

    private static bool TryLinkContentsFolderAsAdministrator(string sourceDir, string destinationDir)
    {
        Console.WriteLine("[HAT] Requesting administrator privileges to link Content");
        Thread.Sleep(TimeSpan.FromSeconds(2)); // User should be warned about this!

        // Elevate only link creation. Encode paths separately so PowerShell cannot interpret their contents.
        var encodedSource = Convert.ToBase64String(Encoding.Unicode.GetBytes(Path.GetFullPath(sourceDir)));
        var encodedDestination = Convert.ToBase64String(Encoding.Unicode.GetBytes(Path.GetFullPath(destinationDir)));
        var script = $"$source = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{encodedSource}')); " +
                     $"$destination = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{encodedDestination}')); " +
                     "try { New-Item -ItemType SymbolicLink -Path $destination " +
                     "-Target $source -ErrorAction Stop | Out-Null; exit 0 } catch { exit 1 }";
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encodedScript}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return false;
            }

            // Verify the helper actually created the expected link before reporting success.
            var target = new DirectoryInfo(destinationDir).ResolveLinkTarget(returnFinalTarget: true);
            return target != null &&
                   string.Equals(target.FullName, Path.GetFullPath(sourceDir), StringComparison.OrdinalIgnoreCase);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == (int)WindowsError.Cancelled)
        {
            Console.WriteLine("[HAT] Administrator prompt cancelled; copying Content instead");
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[HAT] Could not create Content link as administrator ({ex.Message})");
            return false;
        }
    }

    private static void RenameExecutable(string hatPath)
    {
        var gameDir = Path.GetDirectoryName(hatPath)!;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Console.WriteLine($"[HAT] Renaming game app host as {FezLauncher}");
            File.Move(Path.Combine(gameDir, AppHostLauncher), Path.Combine(gameDir, FezLauncher), overwrite: true);
            return;
        }

        // Some FEZ dylibs reference dependencies under /usr/local/lib, but we copy those
        // dependencies beside the game. The launcher sets DYLD_LIBRARY_PATH before dyld loads
        // them and also restores Steam's overlay variable.

        Console.WriteLine("[HAT] Installing macOS game launcher");
        File.Move(Path.Combine(gameDir, AppHostLauncher), Path.Combine(gameDir, "HAT.bin.osx"), overwrite: true);

        using var launcherResource = GetResource("HAT.MacLauncher");
        using var launcher = new StreamReader(launcherResource);
        var launcherPath = Path.Combine(gameDir, FezLauncher);
        File.WriteAllText(launcherPath, launcher.ReadToEnd().ReplaceLineEndings("\n"));
        File.SetUnixFileMode(launcherPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
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
        Console.WriteLine($"Done! Run {FezLauncher} to launch the modded game");
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