namespace HatModLoader.Source.AssemblyResolving
{
    internal static class AssemblyResolverRegistry
    {
        private static readonly HashSet<IAssemblyResolver> RegisteredResolvers = new();
    
        public static void Register(IAssemblyResolver resolver)
        {
            if (RegisteredResolvers.Contains(resolver))
            {
                return;
            }
        
            RegisteredResolvers.Add(resolver);
            AppDomain.CurrentDomain.AssemblyResolve += resolver.ProvideAssembly;
        }

        public static void Unregister(IAssemblyResolver resolver)
        {
            if (!RegisteredResolvers.Contains(resolver))
            {
                return;
            }
        
            RegisteredResolvers.Remove(resolver);
            AppDomain.CurrentDomain.AssemblyResolve -= resolver.ProvideAssembly;
        }
    }
}

