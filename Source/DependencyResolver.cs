using Common;
using System.Reflection;

namespace HatModLoader.Source
{
    internal static class DependencyResolver
    {
        private static readonly string DependencyDirectory = "Dependencies";

        private static readonly Dictionary<string, string> DependencyMap = new();

        public static void Register()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembliesEventHandler;
        }

        private static Assembly ResolveAssembliesEventHandler(object sender, ResolveEventArgs args)
        {
            Logger.Log("HAT", "Resolving assembly: " + args.Name + " for assembly " + args.RequestingAssembly?.FullName ?? "(none)");

            FillInDependencyMap(args);

            if (TryResolveAssemblyFor("FEZRepacker.Core", args, out var repackerAssembly)) return repackerAssembly;
            if (TryResolveAssemblyFor("MonoMod", args, out var monomodAssembly)) return monomodAssembly;

            Logger.Log("HAT", "Did not resolve.");

            return default!;
        }

        private static void FillInDependencyMap(ResolveEventArgs args)
        {
            var assemblyName = args.Name.Split(',')[0];
            var requestingAssemblyName = args.RequestingAssembly?.FullName.Split(',')[0];

            if (requestingAssemblyName == null) return;

            if (DependencyMap.ContainsKey(requestingAssemblyName))
            {
                DependencyMap[assemblyName] = DependencyMap[requestingAssemblyName];
            }
            else
            {
                DependencyMap[assemblyName] = requestingAssemblyName;
            }
        }

        private static bool TryResolveAssemblyFor(string assemblyName, ResolveEventArgs args, out Assembly assembly)
        {
            if (!ShouldResolveFor(assemblyName, args))
            {
                assembly = default!;
                return false;
            }

            var requiredAssemblyName = args.Name.Split(',')[0];
            var dependencyPath = Path.Combine(DependencyDirectory, assemblyName);

            foreach (var file in Directory.EnumerateFiles(dependencyPath))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);

                if (requiredAssemblyName == fileName)
                {
                    var rawAssemblyData = File.ReadAllBytes(file);
                    assembly = Assembly.Load(rawAssemblyData);
                    return true;
                }
            }
            assembly = default!;
            return false;
        }

        private static bool ShouldResolveFor(string assemblyName, ResolveEventArgs args)
        {
            var requiredAssemblyName = args.Name.Split(',')[0];
            var requestingAssemblyName = args.RequestingAssembly?.FullName.Split(',')[0] ?? "";

            if (DependencyMap.ContainsKey(requestingAssemblyName))
            {
                requestingAssemblyName = DependencyMap[requestingAssemblyName];
            }

            Logger.Log("HAT", $"{requiredAssemblyName} mapped to {requestingAssemblyName}");

            bool requiredAssemblyValid = requiredAssemblyName.Contains(assemblyName);
            bool requestingAssemblyValid = requestingAssemblyName.Contains(assemblyName);

            return (requiredAssemblyValid || requestingAssemblyValid);
        }
    }
}
