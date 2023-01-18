using MonoMod;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FezGame.Tools
{
    public static class patch_StaticText
    {
        public static extern string orig_GetString(string tag);
        public static string GetString(string tag)
        {
            // returns original text if it's prefixed with @
            // allows easier injection of custom text into in-game UI structures like main menu

            if (tag.StartsWith("@")) return tag.Substring(1);
            return orig_GetString(tag);
        }
    }
}
