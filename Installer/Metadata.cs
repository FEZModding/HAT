using System.Text.Json.Serialization;
using Mono.Cecil;

namespace FEZ.HAT.Installer;

public sealed class RuntimeConfig
{
    [JsonPropertyName("runtimeOptions")] public RuntimeSettings RuntimeOptions { get; set; } = new();

    public sealed class RuntimeSettings
    {
        [JsonPropertyName("tfm")] public string TargetFramework { get; set; } = "net10.0";

        [JsonPropertyName("framework")] public FrameworkReference Framework { get; set; } = new();
    }

    public sealed class FrameworkReference
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "Microsoft.NETCore.App";

        [JsonPropertyName("version")] public string Version { get; set; } = "10.0.0";
    }
}

public sealed class Deps
{
    private const string Target = ".NETCoreApp,Version=v10.0";

    [JsonPropertyName("runtimeTarget")] public RuntimeTargetInfo RuntimeTarget { get; set; } = new();

    [JsonPropertyName("targets")]
    public Dictionary<string, Dictionary<string, TargetLibrary>> Targets { get; set; } = new()
    {
        [Target] = new Dictionary<string, TargetLibrary>()
    };

    [JsonPropertyName("libraries")] public Dictionary<string, LibraryInfo> Libraries { get; set; } = new();

    public sealed class RuntimeTargetInfo
    {
        [JsonPropertyName("name")] public string Name { get; set; } = Target;
    }

    public sealed class TargetLibrary
    {
        [JsonPropertyName("dependencies")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Dependencies { get; set; }

        [JsonPropertyName("runtime")] public Dictionary<string, RuntimeAsset> Runtime { get; set; } = new();
    }

    public sealed class RuntimeAsset
    {
        [JsonPropertyName("localPath")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LocalPath { get; set; }
    }

    public sealed class LibraryInfo
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "project";

        [JsonPropertyName("serviceable")] public bool Serviceable { get; set; }

        [JsonPropertyName("sha512")] public string Sha512 { get; set; } = "";
    }

    public static Deps Create(string entryAssemblyPath, IEnumerable<string> dependencyDirectories)
    {
        var gameDir = Path.GetDirectoryName(entryAssemblyPath)!;
        var assemblies = new Dictionary<string, ManagedAssembly>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<ManagedAssembly>();

        AddAssembly(entryAssemblyPath);
        foreach (var directory in dependencyDirectories)
        {
            if (!Directory.Exists(directory))
            {
                throw new InstallerException($"Managed dependency directory is missing: {directory}");
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                AddAssembly(path);
            }
        }

        while (pending.Count > 0)
        {
            var assembly = pending.Pop();
            foreach (var reference in assembly.References)
            {
                if (assemblies.ContainsKey(reference) || IsFrameworkAssembly(reference))
                {
                    continue;
                }

                var localPath = Path.Combine(gameDir, reference + ".dll");
                if (File.Exists(localPath))
                {
                    AddAssembly(localPath);
                }
                else
                {
                    throw new InstallerException($"Cannot resolve managed dependency '{reference}' of {assembly.Name}.");
                }
            }
        }

        var deps = new Deps();
        var target = deps.Targets[deps.RuntimeTarget.Name];
        foreach (var assembly in assemblies.Values)
        {
            var library = new TargetLibrary();
            var relativePath = Path.GetRelativePath(gameDir, assembly.Path).Replace('\\', '/');
            library.Runtime.Add(relativePath, new RuntimeAsset { LocalPath = relativePath });

            foreach (var reference in assembly.References)
            {
                if (!assemblies.TryGetValue(reference, out var dependency))
                {
                    continue;
                }

                library.Dependencies ??= new Dictionary<string, string>();
                library.Dependencies.Add(reference, dependency.Version.ToString());
            }

            var key = $"{assembly.Name}/{assembly.Version}";
            target.Add(key, library);
            deps.Libraries.Add(key, new LibraryInfo());
        }

        return deps;

        void AddAssembly(string path)
        {
            using var definition = AssemblyDefinition.ReadAssembly(path);
            var name = definition.Name.Name;
            if (assemblies.TryGetValue(name, out var existing))
            {
                throw new InstallerException(
                    $"Duplicate managed assembly identity '{name}': {existing.Path} and {path}.");
            }

            var references = definition.MainModule.AssemblyReferences
                .Select(reference => reference.Name)
                .ToArray();

            var assembly = new ManagedAssembly(path, name, definition.Name.Version, references);
            assemblies.Add(name, assembly);
            pending.Push(assembly);
        }

        bool IsFrameworkAssembly(string name)
        {
            return name is "mscorlib" or "netstandard" or "System" or "Microsoft.CSharp" ||
                   name.StartsWith("System.", StringComparison.Ordinal) ||
                   name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal);
        }
    }

    private sealed record ManagedAssembly(string Path, string Name, Version Version, string[] References);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(RuntimeConfig))]
[JsonSerializable(typeof(Deps))]
internal partial class DeploymentJsonContext : JsonSerializerContext;
