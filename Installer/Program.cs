using System.IO.Compression;
using System.Formats.Tar;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod;

namespace FEZ.HAT.Installer;

public static class Program
{
    private const string FezExecutable = "FEZ.exe";

    private const string HatManagedAssembly = "HAT.dll";

    private const string HatAppHostResource = "HAT.AppHost";

    private const string HatRuntimeResource = "HAT.Runtime";

    private const string CoreClrTargetFramework = ".NETCoreApp,Version=v10.0";

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
                SetupCoreClrDeployment(hatPath);
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
            modder.OutputPath = Path.Combine(basePath, HatManagedAssembly);
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
            PrepareForCoreClr(modder.Module);
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

    private static void PrepareForCoreClr(ModuleDefinition module)
    {
        module.Attributes &= ~(ModuleAttributes.Required32Bit | ModuleAttributes.Preferred32Bit);

        var targetFramework = module.Assembly.CustomAttributes.FirstOrDefault(attribute =>
            attribute.AttributeType.FullName == "System.Runtime.Versioning.TargetFrameworkAttribute");
        if (targetFramework == null || targetFramework.ConstructorArguments.Count == 0)
        {
            return;
        }

        targetFramework.ConstructorArguments[0] = new CustomAttributeArgument(
            module.TypeSystem.String,
            CoreClrTargetFramework);

        for (var index = 0; index < targetFramework.Properties.Count; index++)
        {
            if (targetFramework.Properties[index].Name == "FrameworkDisplayName")
            {
                targetFramework.Properties[index] = new Mono.Cecil.CustomAttributeNamedArgument(
                    "FrameworkDisplayName",
                    new CustomAttributeArgument(module.TypeSystem.String, ".NET 10.0"));
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

    private static void SetupCoreClrDeployment(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var basePath = Path.GetDirectoryName(path)!;
        var appHostName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "HAT.exe" : "HAT";
        var appHostPath = Path.Combine(basePath, appHostName);
        var temporaryAppHost = appHostPath + ".tmp";
        var runtimePath = Path.Combine(basePath, "bin", "dotnet");

        Console.WriteLine($"[HAT] Installing .NET 10 apphost {appHostName}");
        using (var source = GetResource(HatAppHostResource))
        using (var destination = File.Create(temporaryAppHost))
        {
            source.CopyTo(destination);
        }

        File.Move(temporaryAppHost, appHostPath, overwrite: true);

        Console.WriteLine($"[HAT] Installing private .NET 10 runtime into {runtimePath}");
        if (Directory.Exists(runtimePath))
        {
            Directory.Delete(runtimePath, recursive: true);
        }

        Directory.CreateDirectory(runtimePath);
        using (var runtime = GetResource(HatRuntimeResource))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ZipFile.ExtractToDirectory(runtime, runtimePath, overwriteFiles: true);
            }
            else
            {
                using var gzip = new GZipStream(runtime, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(gzip, runtimePath, overwriteFiles: true);
            }
        }

        PrepareManagedDependencies(path);
        WriteRuntimeConfiguration(path);
        WriteDependencyManifest(path);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetExecutable(appHostPath);
            SetExecutable(Path.Combine(runtimePath, "dotnet"));
        }

        foreach (var obsoleteName in new[]
                 {
                     "HAT.Launcher", "HAT.Launcher.exe", "HAT.sh",
                     "HAT.bin.x86", "HAT.bin.x86_64", "HAT.bin.osx"
                 })
        {
            var obsoletePath = Path.Combine(basePath, obsoleteName);
            if (File.Exists(obsoletePath))
            {
                File.Delete(obsoletePath);
            }
        }

        Console.WriteLine($"Done! Run {appHostName} to launch the modded game through .NET 10.");
    }

    private static void SetExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static void WriteRuntimeConfiguration(string managedAssemblyPath)
    {
        var configurationPath = Path.ChangeExtension(managedAssemblyPath, ".runtimeconfig.json");
        using var stream = File.Create(configurationPath);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteStartObject("runtimeOptions");
        writer.WriteString("tfm", "net10.0");
        writer.WriteStartObject("framework");
        writer.WriteString("name", "Microsoft.NETCore.App");
        writer.WriteString("version", "10.0.0");
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void PrepareManagedDependencies(string managedAssemblyPath)
    {
        var gameDirectory = Path.GetDirectoryName(managedAssemblyPath)!;
        var managedDirectory = Path.Combine(gameDirectory, "bin", "managed");

        if (Directory.Exists(managedDirectory))
        {
            Directory.Delete(managedDirectory, recursive: true);
        }

        var sourceAssemblies = DiscoverManagedAssemblies(managedAssemblyPath);
        Directory.CreateDirectory(managedDirectory);

        foreach (var source in sourceAssemblies.Values)
        {
            if (Path.GetFullPath(source.Path).Equals(
                    Path.GetFullPath(managedAssemblyPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = Path.Combine(managedDirectory, Path.GetFileName(source.Path));
            using var assembly = AssemblyDefinition.ReadAssembly(source.Path, new ReaderParameters
            {
                ReadingMode = ReadingMode.Immediate
            });
            assembly.MainModule.Attributes &=
                ~(ModuleAttributes.Required32Bit | ModuleAttributes.Preferred32Bit);
            assembly.Write(destination);
        }
    }

    private static void WriteDependencyManifest(string managedAssemblyPath)
    {
        var assemblies = DiscoverManagedAssemblies(managedAssemblyPath);
        var assembliesByName = assemblies.Values.ToDictionary(
            assembly => assembly.Name,
            StringComparer.OrdinalIgnoreCase);
        var manifestPath = Path.ChangeExtension(managedAssemblyPath, ".deps.json");

        using var stream = File.Create(manifestPath);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteStartObject("runtimeTarget");
        writer.WriteString("name", CoreClrTargetFramework);
        writer.WriteString("signature", string.Empty);
        writer.WriteEndObject();
        writer.WriteStartObject("compilationOptions");
        writer.WriteEndObject();

        writer.WriteStartObject("targets");
        writer.WriteStartObject(CoreClrTargetFramework);
        foreach (var assembly in assemblies.Values)
        {
            writer.WriteStartObject($"{assembly.Name}/{assembly.Version}");
            writer.WriteStartObject("runtime");
            var relativeAssemblyPath = Path.GetRelativePath(
                    Path.GetDirectoryName(managedAssemblyPath)!,
                    assembly.Path)
                .Replace(Path.DirectorySeparatorChar, '/');
            writer.WriteStartObject(relativeAssemblyPath);
            writer.WriteEndObject();
            writer.WriteEndObject();

            var localDependencies = assembly.References
                .Where(reference => assembliesByName.ContainsKey(reference.Name))
                .ToArray();
            if (localDependencies.Length > 0)
            {
                writer.WriteStartObject("dependencies");
                foreach (var reference in localDependencies)
                {
                    writer.WriteString(reference.Name, reference.Version.ToString());
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteStartObject("libraries");
        foreach (var assembly in assemblies.Values)
        {
            writer.WriteStartObject($"{assembly.Name}/{assembly.Version}");
            writer.WriteString("type",
                Path.GetFullPath(assembly.Path).Equals(
                    Path.GetFullPath(managedAssemblyPath),
                    StringComparison.OrdinalIgnoreCase)
                    ? "project"
                    : "reference");
            writer.WriteBoolean("serviceable", false);
            writer.WriteString("sha512", string.Empty);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static Dictionary<string, ManagedAssemblyInfo> DiscoverManagedAssemblies(string mainAssemblyPath)
    {
        var gameDirectory = Path.GetDirectoryName(mainAssemblyPath)!;
        var assemblies = new Dictionary<string, ManagedAssemblyInfo>(StringComparer.OrdinalIgnoreCase);

        void Discover(string assemblyPath)
        {
            assemblyPath = Path.GetFullPath(assemblyPath);
            if (assemblies.ContainsKey(assemblyPath))
            {
                return;
            }

            using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters
            {
                ReadingMode = ReadingMode.Deferred
            });

            var info = new ManagedAssemblyInfo(
                assemblyPath,
                assembly.Name.Name,
                assembly.Name.Version,
                assembly.MainModule.AssemblyReferences.ToArray());
            assemblies.Add(assemblyPath, info);

            foreach (var reference in info.References)
            {
                if (IsFrameworkAssembly(reference.Name))
                {
                    continue;
                }

                var dependencyPath = new[]
                    {
                        Path.Combine(gameDirectory, "bin", "managed", reference.Name + ".dll"),
                        Path.Combine(gameDirectory, "bin", "managed", reference.Name + ".exe"),
                        Path.Combine(gameDirectory, reference.Name + ".dll"),
                        Path.Combine(gameDirectory, reference.Name + ".exe")
                    }
                    .FirstOrDefault(File.Exists);
                if (dependencyPath != null)
                {
                    Discover(dependencyPath);
                }
            }
        }

        Discover(mainAssemblyPath);
        return assemblies;
    }

    private static bool IsFrameworkAssembly(string name)
    {
        return name is "mscorlib" or "netstandard" or "System" or "Microsoft.CSharp"
               || name.StartsWith("System.", StringComparison.Ordinal);
    }

    private sealed record ManagedAssemblyInfo(
        string Path,
        string Name,
        Version Version,
        AssemblyNameReference[] References);

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