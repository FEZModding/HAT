using System.Reflection;

namespace HatModLoader.Source.AssemblyResolving
{
    internal interface IAssemblyResolver
    {
        public Assembly ProvideAssembly(object sender, ResolveEventArgs args);
    }
}

