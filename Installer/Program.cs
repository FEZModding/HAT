using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod;

namespace FEZ.HAT.Installer;

public static class Program
{
    private const string FezExecutable = "FEZ.exe";

    private const string HatExecutable = "HAT.exe";

    private const string HatLauncherResource = "HAT.Launcher";

    private static readonly string[] FrameworkImplementationAssemblies =
    {
        "SMDiagnostics",
        "System.ServiceModel.Internals"
    };

    private static string? _userFezPath;

    public static void Main(string[] args)
    {
        PrintHeader();
        try
        {
            ParseCommandLineArguments(args);
            if (TryFindFezExecutable(out var fezPath))
            {
                ExtractHatDependencies(fezPath);
                var hatPath = PatchExecutable(fezPath);
                SetupCoreClrLauncher(hatPath);
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
        using var stream = GetResource("HAT.txt");
        using var logo = new StreamReader(stream);
        Console.WriteLine(logo.ReadToEnd());

        const int logoWidth = 50;
        const string version = $"{ThisAssembly.Git.BaseVersion.Major}.{ThisAssembly.Git.BaseVersion.Minor}.{ThisAssembly.Git.BaseVersion.Patch}";
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
                    _userFezPath = Path.GetFullPath(queue.Dequeue());
                    break;
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
            if (File.Exists(Path.Combine(cwd, FezExecutable)))
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
            if (File.Exists(executable))
            {
                Console.WriteLine($"[HAT] Executable found at {executable}");
                return true;
            }
        }

        executable = string.Empty;
        throw new InstallerException("Could not find FEZ. Use --path <dir> or run from the FEZ game directory.");
    }

    private static void ExtractHatDependencies(string path)
    {
        var hatDependenciesDir = Path.Combine(Path.GetDirectoryName(path)!, "HATDependencies");
        if (Directory.Exists(hatDependenciesDir))
        {
            Console.WriteLine("[HAT] Clearing existing HATDependencies");
            Directory.Delete(hatDependenciesDir, recursive: true);
        }

        using var stream = GetResource("HAT.zip");
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in zip.Entries)
        {
            var destination = Path.Combine(Path.GetDirectoryName(path)!, entry.FullName);
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = entry.Open();
            using var file = File.Create(destination);
            source.CopyTo(file);
            Console.WriteLine($"[HAT] Extracting {entry.FullName}");
        }
    }

    private static string PatchExecutable(string path)
    {
        var basePath = Path.GetDirectoryName(path)!;
        var referencePath = ExtractFrameworkReferences();
        try
        {
            using var modder = new MonoModder();

            modder.InputPath = path;
            modder.OutputPath = path.Replace(FezExecutable, HatExecutable);
            modder.ReadingMode = ReadingMode.Deferred;
            modder.AssemblyResolver = BuildResolver(basePath, referencePath);
            modder.MissingDependencyThrow = true;
            modder.MissingDependencyResolver = (currentModder, main, name, fullName) =>
                IsFrameworkImplementationDependency(main, name, fullName, referencePath)
                    ? null
                    : currentModder.DefaultMissingDependencyResolver(currentModder, main, name, fullName);

            modder.WriterParameters = new WriterParameters
            {
                SymbolWriterProvider = new PortablePdbWriterProvider(),
                WriteSymbols = true
            };

            modder.Read();
            modder.ReadMod(Path.Combine(basePath, "FEZ.HAT.mm.dll"));
            modder.ReadMod(Path.Combine(basePath, "FEZ.Hooks.mm.dll"));
            PrioritizeFrameworkDependencyDirectories(modder, referencePath);
            modder.MapDependencies();
            modder.AutoPatch();
            modder.Write();

            return modder.OutputPath;
        }
        finally
        {
            try
            {
                Directory.Delete(referencePath, recursive: true);
            }
            catch
            {
                // Do not mask the patching result or its original error with cleanup failure.
            }
        }
    }

    private static void PrioritizeFrameworkDependencyDirectories(
        MonoModder modder,
        string referencePath)
    {
        var frameworkDirectories = new[]
        {
            referencePath,
            Path.Combine(referencePath, "Facades")
        };

        foreach (var frameworkDirectory in frameworkDirectories)
        {
            var fullFrameworkDirectory = Path.GetFullPath(frameworkDirectory);
            modder.DependencyDirs.RemoveAll(directory =>
                Path.GetFullPath(directory).Equals(
                    fullFrameworkDirectory,
                    StringComparison.OrdinalIgnoreCase));
        }

        modder.DependencyDirs.InsertRange(0, frameworkDirectories);
    }

    private static string ExtractFrameworkReferences()
    {
        const string resourcePrefix = "FrameworkReferences/";
        var referencePath = Path.Combine(
            Path.GetTempPath(),
            "HAT",
            "references-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(referencePath);

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var resourceName in assembly.GetManifestResourceNames()
                         .Where(name => name.StartsWith(resourcePrefix, StringComparison.Ordinal)))
            {
                var relativePath = resourceName[resourcePrefix.Length..]
                    .Replace('/', Path.DirectorySeparatorChar);
                var destination = Path.Combine(referencePath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                using var source = assembly.GetManifestResourceStream(resourceName)
                                   ?? throw new InstallerException(
                                       $"Embedded framework reference is missing: {resourceName}");
                using var destinationStream = File.Create(destination);
                source.CopyTo(destinationStream);
            }

            return referencePath;
        }
        catch
        {
            Directory.Delete(referencePath, recursive: true);
            throw;
        }
    }

    private static DefaultAssemblyResolver BuildResolver(string path, string referencePath)
    {
        var resolver = new FrameworkFirstAssemblyResolver(referencePath);
        foreach (var searchDirectory in resolver.GetSearchDirectories())
        {
            resolver.RemoveSearchDirectory(searchDirectory);
        }

        resolver.AddSearchDirectory(referencePath);
        resolver.AddSearchDirectory(Path.Combine(referencePath, "Facades"));
        resolver.AddSearchDirectory(path);

        resolver.AddSearchDirectory(Path.Combine(path, "HATDependencies", "MonoMod"));
        resolver.AddSearchDirectory(Path.Combine(path, "HATDependencies", "FEZRepacker.Core"));

        return resolver;
    }

    private sealed class FrameworkFirstAssemblyResolver : DefaultAssemblyResolver
    {
        private readonly string _referencePath;

        private readonly Dictionary<string, AssemblyDefinition> _frameworkAssemblies = new(StringComparer.OrdinalIgnoreCase);

        public FrameworkFirstAssemblyResolver(string referencePath)
        {
            _referencePath = referencePath;
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            var referenceFile = FindFrameworkReference(name.Name);
            if (referenceFile is null)
            {
                return base.Resolve(name);
            }

            return ResolveFrameworkReference(name, referenceFile);
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            var referenceFile = FindFrameworkReference(name.Name);
            if (referenceFile is null)
            {
                return base.Resolve(name, parameters);
            }

            return ResolveFrameworkReference(name, referenceFile);
        }

        private AssemblyDefinition ResolveFrameworkReference(AssemblyNameReference name, string referenceFile)
        {
            if (_frameworkAssemblies.TryGetValue(name.FullName, out var assembly))
            {
                return assembly;
            }

            assembly = AssemblyDefinition.ReadAssembly(referenceFile, new ReaderParameters
            {
                AssemblyResolver = this,
                ReadingMode = ReadingMode.Deferred
            });

            _frameworkAssemblies.Add(name.FullName, assembly);
            return assembly;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var assembly in _frameworkAssemblies.Values)
                {
                    assembly.Dispose();
                }

                _frameworkAssemblies.Clear();
            }

            base.Dispose(disposing);
        }

        private string? FindFrameworkReference(string assemblyName)
        {
            var fileName = assemblyName + ".dll";
            var referenceFile = Path.Combine(_referencePath, fileName);
            if (File.Exists(referenceFile))
            {
                return referenceFile;
            }

            var facadeFile = Path.Combine(_referencePath, "Facades", fileName);
            return File.Exists(facadeFile) ? facadeFile : null;
        }
    }

    private static bool IsFrameworkImplementationDependency(
        ModuleDefinition main,
        string name,
        string fullName,
        string referencePath)
    {
        var modulePath = main.FileName;
        if (string.IsNullOrEmpty(modulePath))
        {
            return false;
        }

        var referenceRoot = Path.GetFullPath(referencePath) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(modulePath).StartsWith(referenceRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return FrameworkImplementationAssemblies.Any(assemblyName =>
            name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase) ||
            name.Equals(assemblyName + ".dll", StringComparison.OrdinalIgnoreCase) ||
            fullName.StartsWith(assemblyName + ",", StringComparison.OrdinalIgnoreCase));
    }

    private static void SetupCoreClrLauncher(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var basePath = Path.GetDirectoryName(path)!;
        var launcherName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "HAT.Launcher.exe"
            : "HAT.Launcher";

        var launcherPath = Path.Combine(basePath, launcherName);
        var temporaryLauncher = launcherPath + ".tmp";

        Console.WriteLine($"[HAT] Installing CoreCLR launcher {launcherName}");
        using (var source = GetResource(HatLauncherResource))
        using (var destination = File.Create(temporaryLauncher))
        {
            source.CopyTo(destination);
        }

        File.Move(temporaryLauncher, launcherPath, overwrite: true);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(launcherPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        foreach (var obsoleteName in new[] { "HAT", "HAT.sh", "HAT.bin.x86", "HAT.bin.x86_64", "HAT.bin.osx" })
        {
            var obsoletePath = Path.Combine(basePath, obsoleteName);
            if (!string.Equals(obsoletePath, launcherPath, StringComparison.Ordinal) && File.Exists(obsoletePath))
            {
                File.Delete(obsoletePath);
            }
        }

        Console.WriteLine($"Done! Run {launcherName} to launch the modded game through CoreCLR.");
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
               ?? throw new InstallerException("HAT binaries not found in installer - rebuild the solution.");
    }
}

internal class InstallerException : Exception
{
    public InstallerException(string message) : base(message)
    {
    }
}