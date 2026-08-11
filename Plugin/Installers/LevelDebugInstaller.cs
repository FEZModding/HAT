using System.Reflection;
using Common;
using FezEngine.Tools;
using FezGame;
using FezGame.Components;
using FezGame.Services;
using HatModLoader.Source;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;

namespace HatModLoader.Installers;

public class LevelDebugInstaller : IHatInstaller
{
    public const int TemporarySaveSlotIndex = -32767;
    
    private IDetour _gameInitializeHook;
    
    public void Install(Hat hat)
    {
        _gameInitializeHook = new Hook(
            typeof(Game).GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic),
            new Action<Action<Game>, Game>((orig, game) => { 
                if (!CheckDebugLevel(out var levelName))
                {
                    orig(game);
                    return;
                }
                LoadFezComponents(game);
                orig(game);
                KillIntroSequence();
                SkipToDebugLevel(levelName);
            })
        );
    }

    private bool CheckDebugLevel(out string levelName)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--level", StringComparison.OrdinalIgnoreCase))
            {
                levelName = args[i+1];
                return true;
            }
        }
        levelName = null;
        return false;
    }

    private void KillIntroSequence()
    {
        // it's cleaner to get rid of Intro sequence now than to prevent it from being initialized
        ServiceHelper.RemoveComponent(Intro.Instance);
        var gameState = ServiceHelper.Get<IGameStateManager>();
        gameState.ForceTimePaused = false;
        gameState.InCutscene = false;
    }

    private void LoadFezComponents(Game game)
    {
        typeof(Fez)
            .GetMethod("LoadComponents", BindingFlags.NonPublic | BindingFlags.Static)?
            .Invoke(null, new object[] { game });
    }

    private void SkipToDebugLevel(string levelName)
    {
        var gameState = ServiceHelper.Get<IGameStateManager>();
        gameState.SaveSlot = TemporarySaveSlotIndex;
        gameState.SignInAndChooseStorage(Util.NullAction);
        gameState.LoadSaveFile(() =>
        {
            gameState.ClearSaveFile();
            gameState.SaveData.Level = levelName;
            gameState.SaveData.CanOpenMap = true;
            gameState.SaveData.IsNew = false;
            gameState.LoadLevelAsync(Util.NullAction);
        });
    }

    public void Uninstall()
    {
        _gameInitializeHook.Dispose();
    }
}