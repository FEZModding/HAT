using Common;
using System.Reflection;

namespace HatModLoader.Source
{
    internal static class DependencyResolver
    {
        private static readonly string DependencyDirectory = "Dependencies";

        public static void Register()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembliesEventHandler;
        }

        private static Assembly ResolveAssembliesEventHandler(object sender, ResolveEventArgs args)
        {
            Logger.Log("HAT", "Resolving assembly: " + args.Name + " for assembly " + args.RequestingAssembly?.FullName);

            if (TryResolveAssemblyFor("FEZRepacker.Core", args, out var repackerAssembly)) return repackerAssembly;
            if (TryResolveAssemblyFor("MonoMod", args, out var monomodAssembly)) return monomodAssembly;

            return default!;
        }

        private static bool TryResolveAssemblyFor(string assemblyName, ResolveEventArgs args, out Assembly assembly)
        {
            bool requiredAssemblyValid = args.Name.Contains(assemblyName);
            bool requestingAssemblyValid = args.RequestingAssembly?.FullName.Contains(assemblyName) ?? false;

            if (!requiredAssemblyValid && !requestingAssemblyValid)
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
    }
}
