using Microsoft.Xna.Framework;

namespace HatModLoader.Source.ModDefinition
{
    public static class ModIdentityHelper
    {
        public static Mod GetModByGameComponent<T>(this Hat hat) where T : GameComponent
        {
            return hat.Mods.Where(mod => mod.Components.Any(component => component is T)).FirstOrDefault();
        }

        public static Mod GetOwnMod(this GameComponent gameComponent)
        {
            return Hat.Instance.Mods.Where(mod => mod.Components.Contains(gameComponent)).FirstOrDefault();
        }
    }
}
