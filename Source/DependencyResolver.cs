using Common;
using System.Reflection;

namespace HatModLoader.Source
{
    internal static class DependencyResolver
    {
        private static readonly string DependencyDirectory = "HATDependencies";

        private static readonly Dictionary<string, string> DependencyMap = new();
        private static readonly Dictionary<string, Assembly> DependencyCache = new();

        public static void Register()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembliesEventHandler;
        }

        private static Assembly ResolveAssembliesEventHandler(object sender, ResolveEventArgs args)
        {
            Logger.Log("HAT", "Resolving assembly: \"" + args.Name + "\" for assembly \"" + args.RequestingAssembly?.FullName ?? "(none)" + "\"");

            FillInDependencyMap(args);

            Assembly assembly;
            if (DependencyCache.TryGetValue(IsolateName(args.Name), out assembly)) return assembly;
            if (TryResolveAssemblyFor("MonoMod", args, out assembly)) return assembly;
            if (TryResolveAssemblyFor("FEZRepacker.Core", args, out assembly)) return assembly;

            Logger.Log("HAT", "Did not resolve.");

            return default!;
        }

        private static void FillInDependencyMap(ResolveEventArgs args)
        {
            var assemblyName = IsolateName(args.Name);
            var requestingAssemblyName = IsolateName(args.RequestingAssembly?.FullName ?? "");

            if (requestingAssemblyName.Length == 0) return;

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
                    assembly = Assembly.Load(File.ReadAllBytes(file));
                    DependencyCache[requiredAssemblyName] = assembly;
                    return true;
                }
            }
            assembly = default!;
            return false;
        }

        private static bool ShouldResolveFor(string assemblyName, ResolveEventArgs args)
        {
            var requiredAssemblyName = IsolateName(args.Name);
            var requestingAssemblyName = IsolateName(args.RequestingAssembly?.FullName ?? "");

            if (DependencyMap.ContainsKey(requestingAssemblyName))
            {
                requestingAssemblyName = DependencyMap[requestingAssemblyName];
            }

            bool requiredAssemblyValid = requiredAssemblyName.Contains(assemblyName);
            bool requestingAssemblyValid = requestingAssemblyName.Contains(assemblyName);

            return (requiredAssemblyValid || requestingAssemblyValid);
        }

        private static string IsolateName(string fullAssemblyQualifier)
        {
            return fullAssemblyQualifier.Split(',')[0];
        }
    }
}
