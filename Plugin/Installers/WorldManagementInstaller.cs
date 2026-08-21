using System.Reflection;
using FezEngine.Tools;
using FezGame;
using FezGame.Components;
using FezGame.Services;
using HatModLoader.Source;
using HatModLoader.Source.Worlds;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace HatModLoader.Installers;

public class WorldManagementInstaller : IHatInstaller
{
    private IDetour _gameStateClearSaveFileHook;
    private IDetour _gameStateLoadLevelHook;
    private IDetour _worldMapNameHook;
    private IDetour _createHolesHook;
    
    public void Install(Hat hat)
    {
        _gameStateClearSaveFileHook = new Hook(typeof(GameStateManager).GetMethod("ClearSaveFile"), GameStateClearSaveFileHook);
        _gameStateLoadLevelHook = new Hook(typeof(GameStateManager).GetMethod("LoadLevel"), GameStateLoadLevelHook);
        _worldMapNameHook = new ILHook(typeof(WorldMap).GetMethod("Initialize"), WorldMapCustomNameInject);
        _createHolesHook = new ILHook(
            typeof(BlackHolesHost).GetMethod("CreateHoles", BindingFlags.NonPublic | BindingFlags.Instance),
            BlackHolesAlwaysEnabledListInject
        );
    }

    private void GameStateClearSaveFileHook(Action<IGameStateManager> orig, IGameStateManager self)
    {
        // we want to preserve world custom data from being cleared so starting new game can work properly
        var worlds = Hat.Instance.Worlds;
        bool worldRetrieved = worlds.TryGetFromSave(self.SaveData, out var world);
        
        orig(self);

        if (worldRetrieved)
        {
            worlds.SetInSave(self.SaveData, world);
            world.AssignStartingSaveFieldsTo(self.SaveData);
        }
    }
    
    private void GameStateLoadLevelHook(Action<IGameStateManager> orig, IGameStateManager self)
    {
        // The level is about to be loaded. ForcedLevelName will be used as a fallback on new game/game reset.
        // Make sure its value is matching our save world before going any further.
        if (Hat.Instance.Worlds.TryGetFromSave(self.SaveData, out var world))
        {
            Fez.ForcedLevelName = world.StartingLevel;
        }
        orig(self);
    }

    private void WorldMapCustomNameInject(ILContext il)
    {
        var cursor = new ILCursor(il);
        cursor.GotoNext(i => i.MatchLdstr("MapTree"));
        cursor.Remove();
        cursor.EmitDelegate(static () => Hat.Instance.Worlds.GetCurrent().MapTree);
    }

    private void BlackHolesAlwaysEnabledListInject(ILContext il)
    {
        var cursor = new ILCursor(il);
        cursor.GotoNext(i => i.MatchLdsfld(out var field) && field.Name == "AlwaysEnabledLevels");
        cursor.GotoNext(i => i.MatchCall(out var method) && method.Name == "Contains");
        cursor.Remove();
        cursor.EmitDelegate(static (string[] _, string name) => 
            Hat.Instance.Worlds.GetCurrent().AlwaysBlackHoleLevels.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    public void Uninstall()
    {
        _gameStateClearSaveFileHook?.Dispose();
        _gameStateLoadLevelHook?.Dispose();
        _worldMapNameHook?.Dispose();
        _createHolesHook?.Dispose();
    }
}
