using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Runtime.InteropServices;

namespace FEZ.HAT.AssemblyConverter;

public sealed class ConversionException(string message) : Exception(message);

public static class AssemblyConverter
{
    private static readonly string[] MonoKickstartAssemblies =
    [
        "Microsoft.CSharp",
        "System",
        "System.Configuration",
        "System.Core",
        "System.Data",
        "System.Drawing",
        "System.IO.Compression",
        "System.IO.Compression.FileSystem",
        "System.Numerics",
        "System.Runtime",
        "System.Security",
        "System.Xml",
        "System.Xml.Linq",
        "mscorlib",
        "netstandard"
    ];

    public static IReadOnlyList<FileInfo> Convert(
        DirectoryInfo output,
        DirectoryInfo resolver,
        IReadOnlyList<FileInfo> resolverInputs,
        params FileInfo[] sources)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(resolverInputs);
        ValidateInputs(sources);

        foreach (var input in resolverInputs)
        {
            if (!input.Exists)
            {
                throw new ConversionException($"Resolver input does not exist: {input.FullName}");
            }
        }

        output.Create();
        var rootNames = sources
            .Select(file => Path.GetFileNameWithoutExtension(file.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var input in resolverInputs)
        {
            if (!rootNames.Add(Path.GetFileNameWithoutExtension(input.Name)))
            {
                throw new ConversionException($"Resolver input duplicates a conversion root: {input.Name}");
            }
        }

        var assemblyResolver = BuildAssemblyResolver(resolver, [.. sources, .. resolverInputs]);
        var converted = new List<FileInfo>(sources.Length);

        foreach (var source in sources)
        {
            var outputName = Path.ChangeExtension(source.Name, ".dll");
            var outputFile = new FileInfo(Path.Combine(output.FullName, outputName));

            using var module = ModuleDefinition.ReadModule(source.FullName, new ReaderParameters
            {
                AssemblyResolver = assemblyResolver,
                ReadingMode = ReadingMode.Immediate,
                ReadSymbols = false,
                InMemory = true
            });

            ValidateDependencies(module, rootNames, source);
            RetargetFramework(module);
            RemoveCodeAccessSecurity(module);
            RemoveErrorDialog(module);

            module.Attributes &= ~ModuleAttributes.Required32Bit;
            module.Attributes |= ModuleAttributes.ILOnly;
            module.RuntimeVersion = "v4.0.30319";
            module.Write(outputFile.FullName, new WriterParameters { WriteSymbols = false });
            converted.Add(outputFile);
        }

        ValidateOutputs(converted);
        return converted;
    }

    private static void ValidateInputs(FileInfo[] assemblies)
    {
        if (assemblies.Length == 0)
        {
            throw new ConversionException("At least one conversion root is required.");
        }

        var outputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fi in assemblies)
        {
            if (!fi.Exists)
            {
                throw new ConversionException($"Conversion root does not exist: {fi.FullName}");
            }

            var outputName = Path.ChangeExtension(fi.Name, ".dll");
            if (!outputNames.Add(outputName))
            {
                throw new ConversionException($"Multiple conversion roots would produce {outputName}.");
            }
        }
    }

    internal static DefaultAssemblyResolver BuildAssemblyResolver(DirectoryInfo resolver, FileInfo[] assemblies)
    {
        var assemblyResolver = new DefaultAssemblyResolver();
        var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in assemblies)
        {
            AddDirectory(assembly.DirectoryName);
        }

        AddDirectory(resolver.FullName);
        AddDirectory(RuntimeEnvironment.GetRuntimeDirectory());
        return assemblyResolver;

        void AddDirectory(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && seenDirectories.Add(path))
            {
                assemblyResolver.AddSearchDirectory(path);
            }
        }
    }

    private static void ValidateDependencies(ModuleDefinition module, HashSet<string> rootNames, FileInfo source)
    {
        foreach (var reference in module.AssemblyReferences)
        {
            if (rootNames.Contains(reference.Name) ||
                MonoKickstartAssemblies.Contains(reference.Name) ||
                reference.Name.StartsWith("System.", StringComparison.Ordinal))
            {
                continue;
            }

            throw new ConversionException(
                $"{source.Name} has unexpected managed dependency '{reference.FullName}'. " +
                "Include it in the conversion list if it is intentional.");
        }
    }

    internal static void RetargetFramework(ModuleDefinition module)
    {
        // .NET resolves the other legacy framework references through compatibility assemblies and type forwarders.
        // This clears their original version and identity metadata so they do not retain the source assembly's requirements.
        foreach (var reference in module.AssemblyReferences)
        {
            if (reference.Name != "mscorlib" && MonoKickstartAssemblies.Contains(reference.Name))
            {
                reference.Version = new Version(0, 0, 0, 0);
                reference.PublicKeyToken = [];
                reference.Culture = null;
                reference.Hash = [];
            }
        }
    }

    internal static void RemoveCodeAccessSecurity(ModuleDefinition module)
    {
        module.Assembly.SecurityDeclarations.Clear();

        foreach (var type in module.GetTypes())
        {
            type.SecurityDeclarations.Clear();
            RemoveSecurityAttributes(type.CustomAttributes);

            foreach (var method in type.Methods)
            {
                method.SecurityDeclarations.Clear();
                RemoveSecurityAttributes(method.CustomAttributes);
            }
        }

        RemoveSecurityAttributes(module.Assembly.CustomAttributes);
        RemoveSecurityAttributes(module.CustomAttributes);

        return;

        void RemoveSecurityAttributes(Mono.Collections.Generic.Collection<CustomAttribute> attributes)
        {
            for (var index = attributes.Count - 1; index >= 0; index--)
            {
                var ns = attributes[index].AttributeType.Namespace;
                if (ns == "System.Security" || ns.StartsWith("System.Security.Permissions", StringComparison.Ordinal))
                {
                    attributes.RemoveAt(index);
                }
            }
        }
    }

    private static void RemoveErrorDialog(ModuleDefinition module)
    {
        var errorDialog = module.GetType("FezGame.Tools.ErrorDialog");
        if (errorDialog != null)
        {
            foreach (var method in module.GetTypes().SelectMany(type => type.Methods).Where(method => method.HasBody))
            {
                var il = method.Body.GetILProcessor();
                foreach (var variable in method.Body.Variables)
                {
                    if (variable.VariableType.FullName == "FezGame.Tools.ErrorDialog")
                    {
                        variable.VariableType = module.TypeSystem.Object;
                    }
                }

                foreach (var instruction in method.Body.Instructions.ToArray())
                {
                    if (instruction.Operand is not MethodReference called ||
                        called.DeclaringType.FullName != "FezGame.Tools.ErrorDialog")
                    {
                        continue;
                    }

                    var isConstructor = instruction.OpCode.Code == Code.Newobj;
                    var pops = called.Parameters.Count + (!isConstructor && called.HasThis ? 1 : 0);
                    for (var index = 0; index < pops; index++)
                    {
                        il.InsertBefore(instruction, il.Create(OpCodes.Pop));
                    }

                    if (isConstructor)
                    {
                        il.Replace(instruction, il.Create(OpCodes.Ldnull));
                        continue;
                    }

                    if (called.ReturnType.MetadataType != MetadataType.Void)
                    {
                        throw new ConversionException($"Cannot remove non-void ErrorDialog call in {method.FullName}.");
                    }

                    il.Replace(instruction, il.Create(OpCodes.Nop));
                }
            }

            module.Types.Remove(errorDialog);
            foreach (var resource in module.Resources.ToArray())
            {
                if (resource.Name.Contains("ErrorDialog", StringComparison.Ordinal))
                {
                    module.Resources.Remove(resource);
                }
            }
        }

        #region Remove System.Windows.Forms

        foreach (var reference in module.AssemblyReferences.ToArray())
        {
            if (reference.Name == "System.Windows.Forms")
            {
                module.AssemblyReferences.Remove(reference);
            }
        }

        #endregion

        #region Remove System.Drawing

        {
            var reference = module.AssemblyReferences.FirstOrDefault(anr => anr.Name == "System.Drawing");
            if (reference != null)
            {
                var used = module.GetTypeReferences()
                    .Any(type => type.Scope is AssemblyNameReference { Name: "System.Drawing" });

                if (!used)
                {
                    module.AssemblyReferences.Remove(reference);
                }
            }
        }

        #endregion
    }

    private static void ValidateOutputs(IList<FileInfo> files)
    {
        foreach (var file in files)
        {
            using var module = ModuleDefinition.ReadModule(file.FullName);
            if ((module.Attributes & ModuleAttributes.Required32Bit) != 0)
            {
                throw new ConversionException($"Required32Bit flag remained in {file}.");
            }

            if (module.AssemblyReferences.Any(anr => anr.Name == "System.Windows.Forms"))
            {
                throw new ConversionException($"System.Windows.Forms remained in {file}.");
            }

            if (module.GetType("FezGame.Tools.ErrorDialog") != null)
            {
                throw new ConversionException($"ErrorDialog remained in {file}.");
            }
        }
    }
}