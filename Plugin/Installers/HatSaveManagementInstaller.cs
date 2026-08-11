using FezGame;
using HatModLoader.Source;
using HatModLoader.Source.Menu;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System.Reflection;
using FezEngine.Components;
using FezEngine.Tools;
using FezGame.Services;

namespace HatModLoader.Installers
{
    internal class HatSaveManagementInstaller : IHatInstaller
    {
        public const int SpeedrunSaveSlotIndex = -2256671; // I wonder what it means...
        
        private static IDetour _saveSlotSelectionMenuHook;
        private static IDetour _saveManagementMenuHook;
        private static IDetour _beginSpeedrunHook;
        private static IDetour _resetSpeedrunHook;
        private static IDetour _pauseMenuPostInitializeHook;
        
        public void Install(Hat hat)
        {
            var saveSlotSelectionLevelType = Assembly.GetAssembly(typeof(Fez))
                .GetType("FezGame.Structure.SaveSlotSelectionLevel");
            var saveManagementLevelType = Assembly.GetAssembly(typeof(Fez))
                .GetType("FezGame.Structure.SaveManagementLevel");
            var pauseMenuType = Assembly.GetAssembly(typeof(Fez))
                .GetType("FezGame.Components.PauseMenu");

            _saveSlotSelectionMenuHook = new Hook(
                saveSlotSelectionLevelType.GetMethod("Initialize"),
                new Action<Action<object>, object>((orig, self) =>
                {
                    // we're overriding initial implementation completely 
                    // except for base initialize method (which is just setting a field)
                    self.GetType()
                        .GetField("initialized", BindingFlags.Instance | BindingFlags.NonPublic)?
                        .SetValue(self, true);
                    
                    self.GetType()
                        .GetField("GameState", BindingFlags.Instance | BindingFlags.NonPublic)?
                        .SetValue(self, ServiceHelper.Get<IGameStateManager>());
                    
                    HatSaveManagementMenuBuilder.InitializeWithSaveSelectionMenuLevel(self);
                })
            );

            _saveManagementMenuHook = new Hook(
                saveManagementLevelType.GetMethod("Initialize"),
                new Action<Action<object>, object>((orig, self) =>
                {
                    self.GetType()
                        .GetField("initialized", BindingFlags.Instance | BindingFlags.NonPublic)?
                        .SetValue(self, true);
                    
                    self.GetType()
                        .GetField("FontManager", BindingFlags.Instance | BindingFlags.NonPublic)?
                        .SetValue(self, ServiceHelper.Get<IFontManager>());
                    
                    HatSaveManagementMenuBuilder.InitializeWithSaveManagementMenuLevel(self);
                })
            );

            _beginSpeedrunHook = new ILHook(
                saveSlotSelectionLevelType.GetMethod("BeginSpeedRun", BindingFlags.Instance | BindingFlags.NonPublic),
                InjectNewSpeedrunSaveSlot);

            _resetSpeedrunHook = new ILHook(
                pauseMenuType.GetMethod("ResetSpeedRun", BindingFlags.Instance | BindingFlags.NonPublic),
                InjectNewSpeedrunSaveSlot);

            // This one might be dangerous if anything else in the future uses a literal of 4
            _pauseMenuPostInitializeHook = new ILHook(
                pauseMenuType.GetMethod("PostInitialize", BindingFlags.Instance | BindingFlags.NonPublic),
                InjectNewSpeedrunSaveSlot
            );
        }


        private void InjectNewSpeedrunSaveSlot(ILContext il)
        {
            // replacing the first occurence of literal integer 4 with new integer.
            // easily works for both hook cases, because they have slot assignment is the first line of code in a function.
            const int OldSlot = 4;
            const int NewSlot = SpeedrunSaveSlotIndex; // can be whatever as long as it avoids collision.
            
            var cursor = new ILCursor(il);
            cursor.GotoNext(MoveType.Before, i => i.MatchLdcI4(OldSlot));

            cursor.Remove();
            cursor.Emit(OpCodes.Ldc_I4, NewSlot);
        }

        public void Uninstall()
        {
            _saveSlotSelectionMenuHook?.Dispose();
            _saveManagementMenuHook?.Dispose();
            _beginSpeedrunHook?.Dispose();
            _resetSpeedrunHook?.Dispose();
            _pauseMenuPostInitializeHook?.Dispose();
        }
    }
}