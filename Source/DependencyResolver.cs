using Common;
using System.Reflection;

namespace HatModLoader.Source
{
    internal static class DependencyResolver
    {
        private static readonly string DependencyDirectory = "HATDependencies";

        private static readonly Dictionary<string, Assembly> DependencyMap = new();
        private static readonly Dictionary<string, Assembly> DependencyCache = new();

        public static void Register()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembliesEventHandler;
        }

        private static Assembly ResolveAssembliesEventHandler(object sender, ResolveEventArgs args)
        {
            FillInDependencyMap(args);

            Assembly assembly;
            if (DependencyCache.TryGetValue(IsolateName(args.Name), out assembly)) return assembly;
            if (TryResolveAssemblyFor("MonoMod", args, out assembly)) return assembly;
            if (TryResolveAssemblyFor("FEZRepacker.Core", args, out assembly)) return assembly;
            if (TryResolveModdedDependency(args, out assembly)) return assembly;

            Logger.Log("HAT", "Could not resolve assembly: \"" + args.Name + "\", required by \"" + args.RequestingAssembly?.FullName ?? "(none)" + "\"");

            return default!;
        }

        private static void FillInDependencyMap(ResolveEventArgs args)
        {
            if (args.RequestingAssembly == null) return;

            var assemblyName = IsolateName(args.Name);
            var requestingAssemblyName = IsolateName(args.RequestingAssembly?.FullName ?? "");

            if (DependencyMap.ContainsKey(requestingAssemblyName))
            {
                DependencyMap[assemblyName] = DependencyMap[requestingAssemblyName];
            }
            else
            {
                DependencyMap[assemblyName] = args.RequestingAssembly!;
            }
        }

        private static bool TryResolveAssemblyFor(string assemblyName, ResolveEventArgs args, out Assembly assembly)
        {
            if (!ShouldResolveNamedFor(assemblyName, args))
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
                    assembly = Assembly.Load(File.ReadAllBytes(file));
                    DependencyCache[requiredAssemblyName] = assembly;
                    return true;
                }
            }
            assembly = default!;
            return false;
        }

        private static bool ShouldResolveNamedFor(string assemblyName, ResolveEventArgs args)
        {
            var requiredAssemblyName = IsolateName(args.Name);
            var requestingAssemblyName = IsolateName(GetMainRequiringAssembly(args)?.FullName ?? "");

            bool requiredAssemblyValid = requiredAssemblyName.Contains(assemblyName);
            bool requestingAssemblyValid = requestingAssemblyName.Contains(assemblyName);

            return (requiredAssemblyValid || requestingAssemblyValid);
        }

        private static bool TryResolveModdedDependency(ResolveEventArgs args, out Assembly assembly)
        {
            assembly = default!;

            var requestingMainAssembly = GetMainRequiringAssembly(args);
            if (requestingMainAssembly == null) return false;
            
            var matchingAssembliesInMods = Hat.Instance.Mods
                .Where(mod => mod.Assembly == requestingMainAssembly);
            if (!matchingAssembliesInMods.Any()) return false;

            var requiredAssemblyName = IsolateName(args.Name);
            var requiredAssemblyPath = requiredAssemblyName + ".dll";
            var fileProxy = matchingAssembliesInMods.First().FileProxy;
            if (!fileProxy.FileExists(requiredAssemblyPath)) return false;
            
            using var assemblyData = fileProxy.OpenFile(requiredAssemblyPath);
            var assemblyBytes = new byte[assemblyData.Length];
            assemblyData.Read(assemblyBytes, 0, assemblyBytes.Length);
            assembly = Assembly.Load(assemblyBytes);
            DependencyCache[requiredAssemblyName] = assembly;
            return true;
        }

        private static Assembly GetMainRequiringAssembly(ResolveEventArgs args)
        {
            var requestingMainAssembly = args.RequestingAssembly;

            if(requestingMainAssembly == null)
            {
                return default!;
            }

            var requestingAssemblyName = IsolateName(requestingMainAssembly.FullName);

            if (DependencyMap.ContainsKey(requestingAssemblyName))
            {
                requestingMainAssembly = DependencyMap[requestingAssemblyName];
            }

            return requestingMainAssembly;
        }

        private static string IsolateName(string fullAssemblyQualifier)
        {
            return fullAssemblyQualifier.Split(',')[0];
        }
    }
}
