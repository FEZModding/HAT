using System.Reflection;

namespace HatModLoader.Source.AssemblyResolving
{
    internal class HatSubdirectoryAssemblyResolver : IAssemblyResolver
    {
        private static readonly string DependencyDirectory = "HATDependencies";

        private readonly string _subdirectoryName;
        
        public HatSubdirectoryAssemblyResolver(string subdirectoryName)
        {
            this._subdirectoryName = subdirectoryName;
        }
        

        public Assembly ProvideAssembly(object sender, ResolveEventArgs args)
        {
            foreach (var file in EnumerateAssemblyFilesInSubdirectory())
            {
                if (!TryGetAssemblyName(file, out var assemblyName))
                {
                    continue;
                }
                
                if (assemblyName.MatchesRequest(args, true))
                {
                    return Assembly.LoadFrom(file);
                }
            }

            return null;
        }
        
        private IEnumerable<string> EnumerateAssemblyFilesInSubdirectory()
        {
            var path = Path.Combine(DependencyDirectory, _subdirectoryName);
                
            if (!Directory.Exists(path))
            {
                yield break;
            }
            
            foreach (var file in Directory.EnumerateFiles(path, "*.dll", SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }
            
            foreach (var file in Directory.EnumerateFiles(path, "*.exe", SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }
        }

        private bool TryGetAssemblyName(string filePath, out AssemblyName assemblyName)
        {
            assemblyName = null;
            try
            {
                assemblyName = AssemblyName.GetAssemblyName(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
