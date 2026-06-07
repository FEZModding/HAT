using Common;
using FezGame;
using FezGame.Services;
using HatModLoader.Source;
using MonoMod.RuntimeDetour;

namespace HatModLoader.Installers;

public class WorldManagementInstaller : IHatInstaller
{
    private IDetour _gameStateClearSaveFileHook;
    private IDetour _gameStateLoadLevelHook;
    
    public void Install()
    {
        _gameStateClearSaveFileHook = new Hook(typeof(GameStateManager).GetMethod("ClearSaveFile"), GameStateClearSaveFileHook);
        _gameStateLoadLevelHook = new Hook(typeof(GameStateManager).GetMethod("LoadLevel"), GameStateLoadLevelHook);
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

    public void Uninstall()
    {
        _gameStateClearSaveFileHook?.Dispose();
        _gameStateLoadLevelHook?.Dispose();
    }
}
