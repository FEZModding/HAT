using System.Reflection;
using Common;
using HatModLoader.Source.FileProxies;
using Microsoft.Xna.Framework;

namespace HatModLoader.Source.ModDefinition
{
    public class CodeMod
    {
        public string LibraryName { get; }

        public Assembly Assembly { get; private set; }

        public List<GameComponent> Components { get; } = new();

        private CodeMod(string libraryName)
        {
            LibraryName = libraryName;
        }

        internal void Initialize(Game game, string entrypoint, ModAssemblyLoadContext context)
        {
            if (Assembly != null)
            {
                throw new InvalidOperationException("Assembly is already loaded.");
            }
            
            Assembly = context.LoadEntryAssembly();
            Components.Clear();

            Type[] types;
            if (!string.IsNullOrEmpty(entrypoint))
            {
                if (!Assembly.GetTypes().Any(t => t.FullName?.Equals(entrypoint) ?? false))
                {
                    throw new ArgumentException($"The entrypoint name is not a fully qualified name: {entrypoint}");
                }
                
                // Entrypoint class may load other components (services) via Game.Components (Game.Services)
                Logger.Log("HAT", LogSeverity.Information, 
                    $"Starting at entrypoint component {entrypoint} in assembly {Assembly.GetName().Name}.");
                types = new [] { Assembly.GetType(entrypoint) };
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
                    Components.Add(gameComponent);
                }
            }
        }

        public static bool TryLoad(IFileProxy proxy, Metadata metadata, out CodeMod codeMod)
        {
            if (string.IsNullOrEmpty(metadata.LibraryName) ||
                !metadata.LibraryName.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase) ||
                !proxy.FileExists(metadata.LibraryName))
            {
                codeMod = null;
                return false;
            }

            codeMod = new CodeMod(metadata.LibraryName);
            return true;
        }
    }
}