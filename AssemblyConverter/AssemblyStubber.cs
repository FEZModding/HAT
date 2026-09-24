using Mono.Cecil;
using Mono.Cecil.Cil;

namespace FEZ.HAT.AssemblyConverter;

public static class AssemblyStubber
{
    public static FileInfo Stub(FileInfo source, DirectoryInfo output, DirectoryInfo resolver)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(resolver);

        if (!source.Exists)
        {
            throw new ConversionException($"Assembly to stub does not exist: {source.FullName}");
        }

        output.Create();
        var destination = new FileInfo(Path.Combine(output.FullName, Path.ChangeExtension(source.Name, ".dll")));
        if (Path.GetFullPath(source.FullName)
            .Equals(Path.GetFullPath(destination.FullName), StringComparison.OrdinalIgnoreCase))
        {
            throw new ConversionException("Stub output must differ from its source.");
        }

        using var assemblyResolver = AssemblyConverter.BuildAssemblyResolver(resolver, [source]);
        using var module = ModuleDefinition.ReadModule(source.FullName, new ReaderParameters
        {
            AssemblyResolver = assemblyResolver,
            ReadingMode = ReadingMode.Immediate,
            ReadSymbols = false,
            InMemory = true
        });

        AssemblyConverter.RetargetFramework(module);
        AssemblyConverter.RemoveCodeAccessSecurity(module);

        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                StubMethod(module, method);
            }
        }

        module.Attributes &= ~ModuleAttributes.Required32Bit;
        module.Attributes |= ModuleAttributes.ILOnly;
        module.RuntimeVersion = "v4.0.30319";
        module.Write(destination.FullName, new WriterParameters { WriteSymbols = false });

        return destination;
    }

    private static void StubMethod(ModuleDefinition module, MethodDefinition method)
    {
        if (method.IsAbstract || IsDelegateMethod(method))
        {
            return;
        }

        if (method.ReturnType.IsByReference)
        {
            throw new ConversionException($"Cannot stub by-reference return in {method.FullName}.");
        }

        method.PInvokeInfo = null;
        method.IsPInvokeImpl = false;
        method.ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed;
        method.Body = new MethodBody(method) { InitLocals = true };
        var il = method.Body.GetILProcessor();

        if (method is { IsConstructor: true, IsStatic: false } && !method.DeclaringType.IsValueType)
        {
            var baseType = method.DeclaringType.BaseType?.Resolve()
                           ?? throw new ConversionException(
                               $"Cannot resolve base type of {method.DeclaringType.FullName}.");

            var baseConstructor = baseType.Methods
                .Where(candidate => candidate.IsConstructor && !candidate.IsStatic &&
                                    (candidate.IsPublic || candidate.IsFamily || candidate.IsFamilyOrAssembly))
                .OrderBy(candidate => candidate.Parameters.Count)
                .FirstOrDefault();

            if (baseConstructor is null)
            {
                throw new ConversionException($"No accessible base constructor for {method.FullName}.");
            }

            il.Append(il.Create(OpCodes.Ldarg_0));
            foreach (var parameter in baseConstructor.Parameters)
            {
                if (parameter.ParameterType is ByReferenceType byReference)
                {
                    var local = AddDefaultLocal(byReference.ElementType);
                    il.Append(il.Create(OpCodes.Ldloca, local));
                }
                else
                {
                    var local = AddDefaultLocal(parameter.ParameterType);
                    il.Append(il.Create(OpCodes.Ldloc, local));
                }
            }

            il.Append(il.Create(OpCodes.Call, module.ImportReference(baseConstructor)));
        }

        foreach (var parameter in method.Parameters)
        {
            if (!parameter.IsOut || parameter.ParameterType is not ByReferenceType byReference)
            {
                continue;
            }

            il.Append(il.Create(OpCodes.Ldarg, parameter));
            il.Append(il.Create(OpCodes.Initobj, module.ImportReference(byReference.ElementType)));
        }

        if (method.ReturnType.MetadataType != MetadataType.Void)
        {
            if (method.ReturnType.IsPointer ||
                method.ReturnType.MetadataType is MetadataType.IntPtr or MetadataType.UIntPtr)
            {
                il.Append(il.Create(OpCodes.Ldc_I4_0));
                il.Append(il.Create(OpCodes.Conv_U));
            }
            else if (!method.ReturnType.IsValueType && method.ReturnType is not GenericParameter)
            {
                il.Append(il.Create(OpCodes.Ldnull));
            }
            else
            {
                var result = AddDefaultLocal(method.ReturnType);
                il.Append(il.Create(OpCodes.Ldloc, result));
            }
        }

        il.Append(il.Create(OpCodes.Ret));
        return;

        VariableDefinition AddDefaultLocal(TypeReference type)
        {
            var imported = module.ImportReference(type);
            var local = new VariableDefinition(imported);
            method.Body.Variables.Add(local);
            il.Append(il.Create(OpCodes.Ldloca, local));
            il.Append(il.Create(OpCodes.Initobj, imported));
            return local;
        }
    }

    private static bool IsDelegateMethod(MethodDefinition method)
    {
        for (var type = method.DeclaringType.BaseType; type is not null; type = type.Resolve()?.BaseType)
        {
            if (type.FullName == "System.MulticastDelegate")
            {
                return true;
            }

            if (type.FullName == "System.Object")
            {
                break;
            }
        }

        return false;
    }
}
