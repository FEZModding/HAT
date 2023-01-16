using HatModLoader.Source;
using Microsoft.Xna.Framework;
using System;

namespace FezGame
{
    public class patch_Fez : Fez
    {

        public static Hat HatML;

        protected extern void orig_Initialize();
        protected override void Initialize()
        {
            HatML = new Hat(this);
            orig_Initialize();
            HatML.Initalize();
        }

    }
}
