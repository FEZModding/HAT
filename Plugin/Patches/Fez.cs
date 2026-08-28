using FezEngine.Tools;
using HatModLoader.Helpers;
using HatModLoader.Installers;
using HatModLoader.Source;
using Microsoft.Xna.Framework;
using MonoMod;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace FezGame
{
    class patch_Fez : Fez
    {
        public static Hat HatML;

        [MethodImpl(MethodImplOptions.ForwardRef)]
        public extern void orig_ctor();
        [MonoModConstructor]
        public void ctor()
        {
            HatML = new Hat(this);
            foreach (Type type in GetLoadableTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.IsClass && typeof(IHatInstaller).IsAssignableFrom(t)))
            {
                IHatInstaller installer = (IHatInstaller)Activator.CreateInstance(type);
                installer.Install(HatML);
            }

            orig_ctor();
        }

        [MethodImpl(MethodImplOptions.ForwardRef)]
        protected extern void orig_Initialize();
        protected override void Initialize()
        {
            HatML.Initialize();
            orig_Initialize();
            DrawingTools.Init();
            Activated += (_, _) => HatML.OnGameActivated();
        }

        [MethodImpl(MethodImplOptions.ForwardRef)]
        internal static extern void orig_LoadComponents(Fez game);
        internal static void LoadComponents(Fez game)
        {
            bool doLoad = !ServiceHelper.FirstLoadDone;
            orig_LoadComponents(game);
            if (doLoad) {
                HatML.InitializeComponents();
            }
        }

        [MethodImpl(MethodImplOptions.ForwardRef)]
        protected extern void orig_Update(GameTime gameTime);
        protected override void Update(GameTime gameTime)
        {
            InputHelper.Update(gameTime);
            orig_Update(gameTime);
        }
        
        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                // CoreCLR does not provide every .NET Framework desktop assembly.
                // Optional game types may therefore be unavailable, but that should
                // not prevent unrelated HAT installers from being discovered.
                return exception.Types.Where(type => type != null);
            }
        }
    }
}
