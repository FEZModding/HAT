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

    public sealed class RuntimeAsset;

    public sealed class LibraryInfo
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "project";

        [JsonPropertyName("serviceable")] public bool Serviceable { get; set; }

        [JsonPropertyName("sha512")] public string Sha512 { get; set; } = "";
    }

    public static Deps Create(string entryAssemblyPath)
    {
        var gameDir = Path.GetDirectoryName(entryAssemblyPath)!;
        var assemblies = new Dictionary<string, ManagedAssembly>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(entryAssemblyPath);

        while (pending.Count > 0)
        {
            var path = pending.Pop();
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            var name = assembly.Name.Name;
            if (assemblies.ContainsKey(name))
            {
                continue;
            }

            var references = assembly.MainModule.AssemblyReferences
                .Select(reference => reference.Name)
                .ToArray();

            assemblies.Add(name, new ManagedAssembly(path, name, assembly.Name.Version, references));
            foreach (var reference in references)
            {
                if (assemblies.ContainsKey(reference) || IsFrameworkAssembly(reference))
                {
                    continue;
                }

                var localPath = Path.Combine(gameDir, reference + ".dll");
                if (File.Exists(localPath))
                {
                    pending.Push(localPath);
                }
                else if (!IsHatDependency(reference))
                {
                    throw new InstallerException($"Cannot resolve managed dependency '{reference}' of {name}.");
                }
            }
        }

        var deps = new Deps();
        var target = deps.Targets[deps.RuntimeTarget.Name];
        foreach (var assembly in assemblies.Values)
        {
            var library = new TargetLibrary();
            var relativePath = Path.GetRelativePath(gameDir, assembly.Path).Replace('\\', '/');
            library.Runtime.Add(relativePath, new RuntimeAsset());

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

        bool IsHatDependency(string name)
        {
            return File.Exists(Path.Combine(gameDir, "HATDependencies", "MonoMod", name + ".dll")) ||
                   File.Exists(Path.Combine(gameDir, "HATDependencies", "FEZRepacker.Core", name + ".dll"));
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
