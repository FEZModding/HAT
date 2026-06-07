using FezEngine.Tools;
using FezGame.Services;
using HatModLoader.Source.Worlds;

namespace HatModLoader.Source.Menu;

public static class WorldSelectionMenuBuilder
{
    private static ListMenuHandler _worldListMenuHandler;
    private static List<WorldMetadata> _cachedWorldsList = new();

    internal static void OpenSubMenu(Action selectCallback)
    {
        RecacheWorldsList();
        SetupMenuHandler(selectCallback);
        MenuMediator.OpenSubMenuLevel(MenuMediator.CurrentMenuBase, _worldListMenuHandler.LevelTemplate);
    }

    private static void RecacheWorldsList()
    {
        _cachedWorldsList.Clear();
        _cachedWorldsList = Hat.Instance.Worlds.EnumerateAllWorlds().ToList();
    }
    
    private static void SetupMenuHandler(Action selectCallback)
    {
        if (_worldListMenuHandler != null)
        {
            _worldListMenuHandler.Dispose();
        }
        
        _worldListMenuHandler = new ListMenuHandler(new MenuMediator.LevelTemplate
        {
            Title = "@CHOOSE WORLD",
            AButtonString = "ChooseWithGlyph",
        });

        _worldListMenuHandler.LoopOver = true;

        _worldListMenuHandler.Items = _cachedWorldsList.Select(world => new ListMenuHandler.Item
        {
            Labels = new List<string>(){
                world.DisplayName,
                world.Description,
            },
            ThumbnailPath = world.Thumbnail
        }).ToList();

        _worldListMenuHandler.OnSelect = index => PreSaveWithWorld(_cachedWorldsList[index], selectCallback);
        _worldListMenuHandler.Initialize();
    }

    private static void PreSaveWithWorld(WorldMetadata world, Action onFinished)
    {
        var gameState = ServiceHelper.Get<IGameStateManager>();
        gameState.LoadSaveFile(() =>
        {
            Hat.Instance.Worlds.SetInSave(gameState.SaveData, world);
            gameState.SaveData.Level = world.StartingLevel;
            
            gameState.Save();
            gameState.SaveImmediately();
            onFinished?.Invoke();
        });
    }
    
}