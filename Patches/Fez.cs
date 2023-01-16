using HatModLoader.Source;
using Microsoft.Xna.Framework;
using MonoMod;
using System;

namespace FezGame
{
    public class patch_Fez : Fez
    {
        public static Hat HatML;

        static patch_Fez()
        {
            LoggerModifier.Initialize();
        }

        protected extern void orig_Initialize();
        protected override void Initialize()
        {
            HatML = new Hat(this);
            HatML.InitalizeAssemblies();
            orig_Initialize();
            HatML.InitializeAssets();
            HatML.InitalizeComponents();
        }

    }
}
