using System.Reflection;
using FezEngine.Tools;
using HatModLoader.Source.AssemblyResolving;
using HatModLoader.Source.FileProxies;
using Microsoft.Xna.Framework;

namespace HatModLoader.Source.ModDefinition
{
    public class CodeMod : IMod
    {
        public IFileProxy FileProxy { get; }

        public Metadata Metadata { get; }

        public byte[] RawAssembly { get; }
    
        public Assembly Assembly { get; private set; }

        private List<GameComponent> Components { get; set; }
    
        private IAssemblyResolver _assemblyResolver;

        public CodeMod(IFileProxy fileProxy, Metadata metadata, byte[] rawAssembly)
        {
            FileProxy = fileProxy;
            Metadata = metadata;
            RawAssembly = rawAssembly;
        }

        public void Initialize(Game game)
        {
            if (RawAssembly == null || RawAssembly.Length < 1)
            {
                throw new ArgumentNullException(nameof(RawAssembly), "There's not raw assembly data.");
            }

            if (Assembly != null)
            {
                throw new InvalidOperationException("Assembly is already loaded.");
            }
        
            _assemblyResolver = new ModInternalAssemblyResolver(this);
            AssemblyResolverRegistry.Register(_assemblyResolver);
            Assembly = Assembly.Load(RawAssembly);

            Components = [];
            foreach (var type in Assembly.GetExportedTypes())
            {
                if (typeof(GameComponent).IsAssignableFrom(type) && type.IsPublic && !type.IsAbstract)
                {
                    // NOTE: The constructor accepting the type (Game) is defined in GameComponent
                    var gameComponent = (GameComponent)Activator.CreateInstance(type, [game]);
                    Components.Add(gameComponent);
                }
            }
        }

        public void InjectComponents()
        {
            foreach (var component in Components)
            {
                ServiceHelper.AddComponent(component);
            }
        }

        public void Dispose()
        {
            foreach (var component in Components)
            {
                ServiceHelper.RemoveComponent(component);
            }

            AssemblyResolverRegistry.Unregister(_assemblyResolver);
        }
    }
}