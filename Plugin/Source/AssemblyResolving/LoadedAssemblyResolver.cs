using System.Reflection;

namespace HatModLoader.Source.AssemblyResolving
{
    // The desktop CLR tolerated version drift between unsigned game and mod
    // dependencies. CoreCLR requires an explicit unification policy instead.
    internal sealed class LoadedAssemblyResolver : IAssemblyResolver
    {
        public Assembly ProvideAssembly(object sender, ResolveEventArgs args)
        {
            var requestedName = new AssemblyName(args.Name);
            if (requestedName.GetPublicKeyToken()?.Length > 0)
            {
                return null;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var loadedName = assembly.GetName();
                if (string.Equals(loadedName.Name, requestedName.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        loadedName.CultureName ?? string.Empty,
                        requestedName.CultureName ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase) &&
                    loadedName.GetPublicKeyToken()?.Length is null or 0)
                {
                    return assembly;
                }
            }

            return null;
        }
    }
}