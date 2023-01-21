using HatModLoader.Helpers;
using HatModLoader.Installers;
using HatModLoader.Source;
using Microsoft.Xna.Framework;
using MonoMod;
using System;
using System.Linq;
using System.Reflection;

namespace FezGame
{
    public class patch_Fez : Fez
    {
        public static Hat HatML;

        protected extern void orig_Initialize();
        protected override void Initialize()
        {
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.IsClass && typeof(IHatInstaller).IsAssignableFrom(t)))
            {
                IHatInstaller installer = (IHatInstaller)Activator.CreateInstance(type);
                installer.Install();
            }

            HatML = new Hat(this);
            HatML.InitalizeAssemblies();
            orig_Initialize();
            DrawingTools.Init();
            HatML.InitializeAssets();
            HatML.InitalizeComponents();
        }

        protected extern void orig_Update(GameTime gameTime);
        protected override void Update(GameTime gameTime)
        {
            InputHelper.Update(gameTime);
            orig_Update(gameTime);
        }
    }
}
