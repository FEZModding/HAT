using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using AsmResolver;
using AsmResolver.PE;
using AsmResolver.PE.Builder;
using AsmResolver.PE.Win32Resources.Icon;
using Microsoft.Win32;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod;

namespace FEZ.HAT.Installer;

public static class Program
{
    private const string FezExecutable = "FEZ.exe";

    private const string HatExecutable = "HAT.exe";

    private const string MonoRoot = "/usr/lib/mono";

    private static string? UserFezPath = null;
    private static string? UserMonoRoot = null;

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
                ReplaceExecutableIcon(hatPath);
                PostInstallationSetup(hatPath);
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
                    UserFezPath = Path.GetFullPath(queue.Dequeue());
                    break;
                case "-m" or "--mono-path":
                    UserMonoRoot = Path.GetFullPath(queue.Dequeue());
                    break;
            }
        }
    }

    private static bool TryFindFezExecutable(out string executable)
    {
        var path = string.Empty;
        {
            Console.WriteLine("[HAT] Checking CLI \"--path\" or \"-p\" argument");
            if (UserFezPath != null)
                path = UserFezPath;
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
        using var modder = new MonoModder();

        modder.InputPath = path;
        modder.OutputPath = path.Replace(FezExecutable, HatExecutable);
        modder.ReadingMode = ReadingMode.Deferred;
        modder.AssemblyResolver = BuildResolver(basePath);
        modder.MissingDependencyThrow = true;
        modder.WriterParameters = new WriterParameters
        {
            SymbolWriterProvider = new PortablePdbWriterProvider(),
            WriteSymbols = true
        };

        modder.Read();
        modder.ReadMod(Path.Combine(basePath, "FEZ.HAT.mm.dll"));
        modder.ReadMod(Path.Combine(basePath, "FEZ.Hooks.mm.dll"));
        modder.MapDependencies();
        modder.AutoPatch();
        modder.Write();

        return modder.OutputPath;
    }

    private static DefaultAssemblyResolver BuildResolver(string path)
    {
        var resolver = new DefaultAssemblyResolver();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // .NET Framework install - registry tells us where
            var netFxRoot = (string)Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\.NETFramework",
                "InstallRoot", ""
            )!;

            if (string.IsNullOrEmpty(netFxRoot))
            {
                netFxRoot = Directory.EnumerateDirectories(path, "v4.*")
                    .OrderByDescending(d => d)
                    .FirstOrDefault();
            }

            if (!string.IsNullOrEmpty(netFxRoot))
            {
                var installFolder = Directory.EnumerateDirectories(netFxRoot, "v4.*")
                    .OrderByDescending(d => d)
                    .FirstOrDefault();
                resolver.AddSearchDirectory(installFolder);
            }
        }
        else
        {
            string? monoPath = null;

            Console.WriteLine("[HAT] Checking CLI \"--mono-path\" or \"-m\" argument");
            if (UserMonoRoot != null) {
                monoPath = UserMonoRoot;
            }

            if (string.IsNullOrEmpty(monoPath)) {
                // Prefer 4.8-api, fall back to any 4.x directory
                Console.WriteLine("[HAT] Checking for system Mono");
                monoPath = Directory.Exists(MonoRoot)
                    ? Directory.EnumerateDirectories(MonoRoot, "4.*")
                        .Where(d => File.Exists(Path.Combine(d, "Facades", "netstandard.dll")))
                        .OrderByDescending(d => d)
                        .FirstOrDefault()
                    : null;
            }

            if (!string.IsNullOrEmpty(monoPath))
            {
                Console.WriteLine($"[HAT] Using system Mono for patching: {monoPath}");
                resolver.AddSearchDirectory(monoPath);
                var netstandard = Path.Combine(monoPath, "Facades", "netstandard.dll");
                if (File.Exists(netstandard))
                {
                    // Copy netstandard.dll from the resolved 4.x api dir so FEZRepacker can load it at runtime
                    File.Copy(netstandard, Path.Combine(path, "netstandard.dll"), overwrite: true);
                }
            }
            else
            {
                Console.WriteLine("[HAT] System Mono not found, falling back to MonoKickstart libraries");
                resolver.AddSearchDirectory(path);
                var netstandard = Path.Combine(path, "netstandard.dll");
                if (!File.Exists(netstandard))
                {
                    throw new InstallerException("Please supplement netstandard.dll one from mono package.");
                }
            }
        }

        resolver.AddSearchDirectory(Path.Combine(path, "HATDependencies", "MonoMod"));
        resolver.AddSearchDirectory(Path.Combine(path, "HATDependencies", "FEZRepacker.Core"));

        return resolver;
    }

    private static void ReplaceExecutableIcon(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        Console.WriteLine("[HAT] Patching executable icon");

        using var stream = GetResource("HAT.ico");
        var iconData = new byte[stream.Length];
        stream.ReadExactly(iconData);

        // Parse ISO directory entry (first entry at offset 6)
        const int entryOffset = 6;
        var width = iconData[entryOffset];
        var height = iconData[entryOffset + 1];
        var colorCount = iconData[entryOffset + 2];
        var planes = (ushort)BitConverter.ToInt16(iconData, entryOffset + 4);
        var bpp = (ushort)BitConverter.ToInt16(iconData, entryOffset + 6);
        var imageSize = BitConverter.ToInt32(iconData, entryOffset + 8);
        var imageOffset = BitConverter.ToInt32(iconData, entryOffset + 12);
        var pixelData = iconData.Skip(imageOffset).Take(imageSize).ToArray();

        var image = PEImage.FromFile(path);
        var iconResource = IconResource.FromDirectory(image.Resources!, IconType.Icon);

        var group = iconResource!.Groups.First();
        var entry = group.Icons.OrderByDescending(i => i.Width).First();
        entry.PixelData = new DataSegment(pixelData);
        entry.Width = width == 0 ? (byte)255 : width; // 0 in ICO = 256, but byte max is 255
        entry.Height = height == 0 ? (byte)255 : height;
        entry.ColorCount = colorCount;
        entry.Planes = planes;
        entry.BitsPerPixel = bpp;

        iconResource.InsertIntoDirectory(image.Resources!);

        var tempPath = path + ".tmp";
        var builder = new ManagedPEFileBuilder();
        builder.CreateFile(image).Write(tempPath);
        File.Move(tempPath, path, overwrite: true);
    }

    private static void PostInstallationSetup(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("Done! Run HAT.exe to launch the modded game.");
            return;
        }

        // Copy the monokickstart binaries so that /proc/self/exe resolves to
        // HAT.bin.*, causing the embedded Mono runtime to load HAT.exe instead of FEZ.exe
        Console.WriteLine("[HAT] Copying MonoKickstart");
        var basePath2 = Path.GetDirectoryName(path)!;
        var kickstartBinaries = new Dictionary<string, string>
        {
            ["FEZ.bin.x86"] = "HAT.bin.x86",
            ["FEZ.bin.x86_64"] = "HAT.bin.x86_64",
            ["FEZ.bin.osx"] = "HAT.bin.osx"
        };

        foreach (var (src, dst) in kickstartBinaries)
        {
            var srcPath = Path.Combine(basePath2, src);
            if (File.Exists(srcPath))
            {
                File.Copy(srcPath, Path.Combine(basePath2, dst), overwrite: true);
            }
        }

        // Copy launch script mirroring the original FEZ script
        Console.WriteLine("[HAT] Creating launch script");
        var script = path.Replace(HatExecutable, "HAT");
        using (var stream = GetResource("HAT.sh"))
        {
            using (var file = File.Create(script))
            {
                stream.CopyTo(file);
            }
        }

        // chmod +x
        File.SetUnixFileMode(script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        Console.WriteLine("Done! Run ./HAT to launch the modded game.");
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
