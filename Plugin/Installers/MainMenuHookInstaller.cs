using FezGame;
using MonoMod.RuntimeDetour;
using System.Reflection;
using HatModLoader.Source.Menu;

namespace HatModLoader.Installers
{
    internal class MainMenuHookInstaller : IHatInstaller
    {
        private static Hook _menuInitializeHook;
        
        public void Install()
        {
            var menuBaseType = Assembly.GetAssembly(typeof(Fez)).GetType("FezGame.Components.MenuBase");

            _menuInitializeHook = new Hook(
                menuBaseType.GetMethod("Initialize"),
                new Action<Action<object>, object>((orig, self) =>
                {
                    orig(self);
                    MenuMediator.OnMenuInitialized(self);
                })
            );
        }

        public void Uninstall()
        {
            _menuInitializeHook?.Dispose();
        }
    }
}