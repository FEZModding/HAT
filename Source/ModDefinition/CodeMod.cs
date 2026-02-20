using System.Reflection;
using Common;
using FezEngine.Tools;
using HatModLoader.Source.AssemblyResolving;
using HatModLoader.Source.FileProxies;
using Microsoft.Xna.Framework;

namespace HatModLoader.Source.ModDefinition
{
    public class CodeMod
    {
        public byte[] RawAssembly { get; }

        public Assembly Assembly { get; private set; }

        public List<GameComponent> Components { get; private set; }

        private CodeMod(byte[] rawAssembly)
        {
            RawAssembly = rawAssembly;
        }

        public void Initialize(Game game, string entrypoint)
        {
            if (RawAssembly == null || RawAssembly.Length < 1)
            {
                throw new ArgumentNullException(nameof(RawAssembly), "There's no raw assembly data.");
            }

            if (Assembly != null)
            {
                throw new InvalidOperationException("Assembly is already loaded.");
            }
            
            Assembly = Assembly.Load(RawAssembly);
            Components = [];

            Type[] types;
            if (!string.IsNullOrEmpty(entrypoint))
            {
                if (!Assembly.GetTypes().Any(t => t.FullName?.Equals(entrypoint) ?? false))
                {
                    throw new ArgumentException($"The entrypoint name is not a fully qualified name: {entrypoint}");
                }
                
                // Entrypoint class may load other components (services) via Game.Components (Game.Services)
                Logger.Log("HAT", LogSeverity.Information, $"Starting at entrypoint {entrypoint}.");
                types = [Assembly.GetType(entrypoint)];
            }
            else
            {
                // Use backward compatible method
                Logger.Log("HAT", LogSeverity.Warning, "No entrypoint was specified. Loading all public components...");
                types = Assembly.GetExportedTypes();
            }
            
            foreach (var type in types)
            {
                if (typeof(GameComponent).IsAssignableFrom(type) && type.IsPublic && !type.IsAbstract)
                {
                    // NOTE: The constructor accepting the type (Game) is defined in GameComponent
                    var gameComponent = (GameComponent)Activator.CreateInstance(type, [game]);
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

            using var assemblyStream = proxy.OpenFile(metadata.LibraryName);
            var rawAssembly = new byte[assemblyStream.Length];
            var count = assemblyStream.Read(rawAssembly, 0, rawAssembly.Length);

            if (rawAssembly.Length != count)
            {
                codeMod = null;
                return false;
            }

            codeMod = new CodeMod(rawAssembly);
            return true;
        }
    }
}