using System.Reflection;
using Common;
using FezEngine.Tools;
using HatModLoader.Source.FileProxies;
using Microsoft.Xna.Framework;

namespace HatModLoader.Source.ModDefinition
{
    public class CodeMod : IDisposable
    {
        public Assembly Assembly { get; private set; }

        private readonly List<GameComponent> _components = new();

        private ModAssemblyLoadContext _loadContext;

        internal void Initialize(Game game, IFileProxy proxy, Metadata metadata)
        {
            if (_loadContext != null)
            {
                throw new InvalidOperationException("Assembly is already loaded.");
            }
            
            _loadContext = new ModAssemblyLoadContext(proxy, metadata);
            try
            {
                Assembly = _loadContext.LoadEntryAssembly();
                _components.Clear();

                Type[] types;
                if (!string.IsNullOrEmpty(metadata.Entrypoint))
                {
                    if (!Assembly.GetTypes().Any(t => t.FullName?.Equals(metadata.Entrypoint) ?? false))
                    {
                        throw new ArgumentException($"The entrypoint name is not a fully qualified name: {metadata.Entrypoint}");
                    }
                
                    // Entrypoint class may load other components (services) via Game.Components (Game.Services)
                    Logger.Log("HAT", LogSeverity.Information, 
                        $"Starting at entrypoint component {metadata.Entrypoint} in assembly {Assembly.GetName().Name}.");
                    types = new [] { Assembly.GetType(metadata.Entrypoint) };
                }
                else
                {
                    // Use backward compatible method
                    Logger.Log("HAT", LogSeverity.Warning, 
                        $"No entrypoint was specified for assembly {Assembly.GetName().Name}. " +
                        "Loading all public components instead.");
                    types = Assembly.GetExportedTypes();
                }
            
                foreach (var type in types)
                {
                    if (typeof(GameComponent).IsAssignableFrom(type) && type.IsPublic && !type.IsAbstract)
                    {
                        // The constructor accepting the type (Game) is defined in GameComponent
                        var gameComponent = (GameComponent)Activator.CreateInstance(type, game);
                        _components.Add(gameComponent);
                    }
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void InjectComponents()
        {
            foreach (var component in _components)
            {
                ServiceHelper.AddComponent(component);
            }
        }

        public void Dispose()
        {
            foreach (var component in _components)
            {
                ServiceHelper.RemoveComponent(component);
            }

            _components.Clear();
            Assembly = null;
            _loadContext?.Unload();
            _loadContext = null;
        }

        public static bool HasLibrary(IFileProxy proxy, Metadata metadata)
        {
            return !string.IsNullOrEmpty(metadata.LibraryName) &&
                   metadata.LibraryName.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase) &&
                   proxy.FileExists(metadata.LibraryName);
        }

        public static bool TryLoad(IFileProxy proxy, Metadata metadata, out CodeMod codeMod)
        {
            if (!HasLibrary(proxy, metadata))
            {
                codeMod = null;
                return false;
            }

            codeMod = new CodeMod();
            return true;
        }
    }
}