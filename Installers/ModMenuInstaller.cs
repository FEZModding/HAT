using FezGame;
using HatModLoader.Source;
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
        private static Type MenuItemType;
        private static Type MenuBaseType;
        private static Type MainMenuType;

        private static int modMenuCurrentIndex;

        private static IDetour MenuInitHook;

        public void Install()
        {
            MenuLevelType = Assembly.GetAssembly(typeof(Fez)).GetType("FezGame.Structure.MenuLevel");
            MenuItemType = Assembly.GetAssembly(typeof(Fez)).GetType("FezGame.Structure.MenuItem");
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
            MenuLevelType.GetField("Oversized").SetValue(ModLevel, true);



            var MenuLevelAddItemGeneric = MenuLevelType.GetMethods().FirstOrDefault(mi => mi.Name == "AddItem" && mi.GetParameters().Length == 5);
            var MenuLevelAddItemInt = MenuLevelAddItemGeneric.MakeGenericMethod(new Type[] { typeof(int) });

            var menuIteratorItem = MenuLevelAddItemInt.Invoke(ModLevel, new object[] { 
                null, (Action)delegate { }, false, 
                (Func<int>) delegate{ return modMenuCurrentIndex; },
                (Action<int, int>) delegate(int value, int change) {
                    modMenuCurrentIndex += change;
                    if (modMenuCurrentIndex < 0) modMenuCurrentIndex = Hat.Instance.Mods.Count-1;
                    if (modMenuCurrentIndex >= Hat.Instance.Mods.Count) modMenuCurrentIndex = 0;
                }
            });
            MenuItemType.GetProperty("SuffixText").SetValue(menuIteratorItem, (Func<string>)delegate
            {
                return $"{modMenuCurrentIndex + 1} / {Hat.Instance.Mods.Count}";
            });

            Action<string, Func<string>> AddInactiveStringItem = delegate (string name, Func<string> suffix)
            {
                var item = MenuLevelType.GetMethod("AddItem", new Type[] { typeof(string) })
                    .Invoke(ModLevel, new object[] {name});
                MenuItemType.GetProperty("Selectable").SetValue(item, false);
                if(suffix != null)
                {
                    MenuItemType.GetProperty("SuffixText").SetValue(item, suffix);
                }
            };

            AddInactiveStringItem(null, null);
            AddInactiveStringItem(null, () => Hat.Instance.Mods[modMenuCurrentIndex].Info.Name);
            AddInactiveStringItem(null, () => Hat.Instance.Mods[modMenuCurrentIndex].Info.Description);
            AddInactiveStringItem(null, () => $"made by {Hat.Instance.Mods[modMenuCurrentIndex].Info.Author}");
            AddInactiveStringItem(null, () => $"version {Hat.Instance.Mods[modMenuCurrentIndex].Info.Version}");

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
