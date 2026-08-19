using FezEngine.Tools;
using HatModLoader.Helpers;
using HatModLoader.Installers;
using HatModLoader.Source;
using Microsoft.Xna.Framework;
using MonoMod;
using System.Reflection;
using System.Runtime.CompilerServices;
using HatModLoader.Source.Assets;

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
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes()
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
            Activated += (_, _) => AssetHotReloader.OnGameActivated(HatML);
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
    }
}
