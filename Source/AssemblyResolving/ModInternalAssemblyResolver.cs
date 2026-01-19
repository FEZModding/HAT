using System.Reflection;
using HatModLoader.Source.ModDefinition;
using Mono.Cecil;

namespace HatModLoader.Source.AssemblyResolving
{
    internal class ModInternalAssemblyResolver : IAssemblyResolver
    {
        private readonly ModIdentity _mod;

        private readonly Dictionary<AssemblyName, string> _cachedAssemblyPaths = new();
        
        public ModInternalAssemblyResolver(ModIdentity mod)
        {
            _mod = mod;
            CacheAssemblyPaths();
        }
        
        public Assembly ProvideAssembly(object sender, ResolveEventArgs args)
        {
            if (_mod.CodeMod != null && _mod.CodeMod.Assembly.GetName().MatchesRequest(args, false))
            {
                return _mod.CodeMod.Assembly;
            }
            
            foreach(var assemblyName in _cachedAssemblyPaths.Keys)
            {
                if (assemblyName.MatchesRequest(args, false))
                {
                    using var assemblyData = _mod.FileProxy.OpenFile(_cachedAssemblyPaths[assemblyName]);
                    var assemblyBytes = new byte[assemblyData.Length];
                    assemblyData.Read(assemblyBytes, 0, assemblyBytes.Length);
                    return Assembly.Load(assemblyBytes);
                }
            }
            return null;
        }

        private void CacheAssemblyPaths()
        {
            foreach (var filePath in EnumerateAssemblyFilesInMod())
            {
                using var assemblyFile = _mod.FileProxy.OpenFile(filePath);
                using var assemblyDef = AssemblyDefinition.ReadAssembly(assemblyFile, new ReaderParameters { ReadSymbols = false });
                var fullName = new AssemblyName(assemblyDef.Name.ToString());
                
                if (!_cachedAssemblyPaths.ContainsKey(fullName))
                {
                    _cachedAssemblyPaths[fullName] = filePath;
                }
            }
        }
        
        private IEnumerable<string> EnumerateAssemblyFilesInMod()
        {
            foreach (var file in _mod.FileProxy.EnumerateFiles(""))
            {
                if (
                    file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || 
                    file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ) {
                    yield return file;
                }
            }
        }
    }
}

