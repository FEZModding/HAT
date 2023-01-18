using FezGame;
using MonoMod.RuntimeDetour;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace HatModLoader.Installers
{
    internal class ModMenuInstaller : IHatInstaller
    {

        private static Type MenuLevelType;
        private static Type MenuBaseType;
        private static Type MainMenuType;

        private static IDetour MenuInitHook;

        public void Install()
        {
            MenuLevelType = Assembly.GetAssembly(typeof(Fez)).GetType("FezGame.Structure.MenuLevel");
            MenuBaseType = Assembly.GetAssembly(typeof(Fez)).GetType("FezGame.Components.MenuBase");
            MainMenuType = Assembly.GetAssembly(typeof(Fez)).GetType("FezGame.Components.MainMenu");

            MenuInitHook = new Hook(
                MenuBaseType.GetMethod("Initialize"),
                new Action<Action<object>, object>((orig, self) => {
                    orig(self);
                    CreateAndAddModLevel(self);
                })
            );
        }

        private static void CreateAndAddModLevel(object MenuBase)
        {
            const BindingFlags privBind = BindingFlags.NonPublic | BindingFlags.Instance;

            // prepare main menu object
            object MenuRoot;
            if (MenuBase.GetType() == MainMenuType)
            {
                MenuRoot = MainMenuType.GetField("RealMenuRoot", privBind).GetValue(MenuBase);
            }
            else
            {
                MenuRoot = MenuBaseType.GetField("MenuRoot", privBind).GetValue(MenuBase);
            }
            MenuLevelType.GetField("IsDynamic").SetValue(MenuRoot, true);

            // create new level
            object ModLevel = Activator.CreateInstance(MenuLevelType);
            MenuLevelType.GetField("IsDynamic").SetValue(ModLevel, true);
            MenuLevelType.GetProperty("Title").SetValue(ModLevel, "@MODS");
            MenuLevelType.GetField("Parent").SetValue(ModLevel, MenuRoot);

            // add created menu level to the main menu
            int modsIndex = ((IList)MenuLevelType.GetField("Items").GetValue(MenuRoot)).Count - 2;
            MenuLevelType.GetMethod("AddItem", new Type[] { typeof(string), typeof(Action), typeof(int) })
                .Invoke(MenuRoot, new object[] { "@MODS", (Action) delegate{
                    MenuBaseType.GetMethod("ChangeMenuLevel").Invoke(MenuBase, new object[] { ModLevel, false });
            }, modsIndex});

            // needed to refresh the menu before the transition to it happens (pause menu)
            MenuBaseType.GetMethod("RenderToTexture", privBind).Invoke(MenuBase, new object[] { });
        }

        public void Uninstall()
        {
            MenuInitHook.Dispose();
        }
    }
}
