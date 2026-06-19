
namespace HatModLoader.Source.Menu;

internal static class ModListMenuBuilder
{
    private static ListMenuHandler _modListMenuHandler;

    internal static void InjectIntoMenuBase(object menuBase)
    {
        SetupMenuHandler();
        var openModsMenuItem = new MenuMediator.ItemTemplate
        {
            Text = "@MODS",
            OnSelect = () => MenuMediator.OpenSubMenuLevel(menuBase, _modListMenuHandler.LevelTemplate)
        };
        MenuMediator.AddItemToMenuLevel(openModsMenuItem, MenuMediator.GetMenuRoot(menuBase), -3);
    }
    
    internal static void SetupMenuHandler()
    {
        _modListMenuHandler = new ListMenuHandler(new MenuMediator.LevelTemplate
        {
            Title = "@MODS",
            Oversized = true,
        });

        _modListMenuHandler.LoopOver = true;
        _modListMenuHandler.NoThumbnail = true;
        _modListMenuHandler.NoItemsText = "@No HAT Mods Installed";

        _modListMenuHandler.Items = Hat.Instance.Mods.Select(mod => new ListMenuHandler.Item
        {
            Labels = new List<string>(){
                mod.Metadata.Name,
                mod.Metadata.Description,
                $"made by {mod.Metadata.Author}",
                $"version {mod.Metadata.Version}"
            }
        }).ToList();

        _modListMenuHandler.Initialize();
    }
}
