using System.Globalization;
using System.Reflection;
using EasyStorage;
using FezEngine.Components;
using FezEngine.Tools;
using FezGame;
using FezGame.Services;
using FezGame.Structure;
using FezGame.Tools;

namespace HatModLoader.Source.Menu;

internal static class HatSaveManagementMenuBuilder
{
    private const int MaxSaveSlotsCount = 64;
    
    private static int _pickedCopySlot = 0;
    private static readonly Dictionary<SelectionMode, ListMenuHandler> _listMenuHandlersCache = new();
    
    public static void InitializeWithSaveSelectionMenuLevel(object saveSelectionMenuLevel)
    {
        var saveSelectionMenuHandler = CreateMenuHandler(
            SelectionMode.Load, index => HandleSaveSelectionAction(saveSelectionMenuLevel, index));
        
        MenuMediator.BuildMenuLevelObject(saveSelectionMenuHandler.LevelTemplate, saveSelectionMenuLevel);
    }

    public static void InitializeWithSaveManagementMenuLevel(object saveManagementMenuLevel)
    {
        MenuMediator.BuildMenuLevelObject(new MenuMediator.LevelTemplate() {
            Title = "SaveManagementTitle",
            Items = new List<MenuMediator.ItemTemplate>() {
                new() {
                    Text = "SaveChangeSlot",
                    OnSelect = () => 
                        OpenSaveManagementSubMenuForMode(saveManagementMenuLevel, SelectionMode.Change)
                },
                new() {
                    Text = "SaveCopyTitle",
                    OnSelect = () =>
                        OpenSaveManagementSubMenuForMode(saveManagementMenuLevel, SelectionMode.CopyPickSource)
                }, 
                new() {
                    Text = "SaveClearTitle",
                    OnSelect = () => 
                        OpenSaveManagementSubMenuForMode(saveManagementMenuLevel, SelectionMode.Clear)
                },
            }
        }, saveManagementMenuLevel);
    }

    private static ListMenuHandler CreateMenuHandler(SelectionMode selectionMode, Action<int> onSelect)
    {
        var title = selectionMode switch
        {
            SelectionMode.Load => "SaveSlotTitle",
            SelectionMode.Change => "SaveChangeSlot",
            SelectionMode.Clear => "SaveClearTitle",
            SelectionMode.CopyPickSource => "SaveCopySourceTitle",
            SelectionMode.CopyPickTarget => "SaveCopyDestTitle",
            _ => ""
        };
        
        var menuHandler = new ListMenuHandler(new MenuMediator.LevelTemplate
        {
            Title = title,
            AButtonString = selectionMode is SelectionMode.Change ? "ChangeWithGlyph" : "ChooseWithGlyph",
            BButtonString = selectionMode is SelectionMode.Load ? "ExitWithGlyph" : null,
        });

        menuHandler.ScrollPrefix = StaticText.GetString("SaveSlotPrefix");
        menuHandler.DefaultIndex = () => GetDefaultItemIndex(selectionMode);
        menuHandler.Items = BuildSaveSlotInfoList(selectionMode);
        menuHandler.OnSelect = onSelect;
        
        menuHandler.Initialize();

        if (_listMenuHandlersCache.TryGetValue(selectionMode, out var oldMenuHandler))
        {
            oldMenuHandler.Dispose();
            _listMenuHandlersCache.Remove(selectionMode);
        }
        _listMenuHandlersCache.Add(selectionMode, menuHandler);

        return menuHandler;
    }

    private static int GetDefaultItemIndex(SelectionMode selectionMode)
    {
        return selectionMode switch
        {
            SelectionMode.Load => Fez.SpeedRunMode ? 1 : 0,
            SelectionMode.CopyPickTarget => _pickedCopySlot,
            _ => ServiceHelper.Get<IGameStateManager>().SaveSlot
        };
    }

    private static List<ListMenuHandler.Item> BuildSaveSlotInfoList(SelectionMode selectionMode)
    {
        var saveSlotInfoList = new List<ListMenuHandler.Item>();
        var gameState = ServiceHelper.Get<IGameStateManager>();

        if (Fez.SpeedRunMode && selectionMode == SelectionMode.Load)
        {
            saveSlotInfoList.Add(new ListMenuHandler.Item
            {
                Labels = new List<string>(),
                CustomTitle = "SPEEDRUN",
                ThumbnailPath = "Other Textures/GottaGomezFast",
            });
        }
        
        var shownSlotsCount = GetSlotsCountToShow();
        for (var i = 0; i < shownSlotsCount; i++)
        {
            var slotInfo = BuildSaveSlotInfo(i);
            
            var item = new ListMenuHandler.Item
            {
                Labels = new List<string>(),
            };

            if (slotInfo.Empty)
            {
                item.Labels.Add(StaticText.GetString("NewSlot"));
            }
            else
            {
                item.ThumbnailPath = $"Other Textures/map_screens/{slotInfo.LevelName}";
                item.Labels.Add(slotInfo.ExtraInformation);
            }
            
            item.Disabled = selectionMode switch
            {
                SelectionMode.CopyPickSource => slotInfo.Empty,
                SelectionMode.CopyPickTarget => i == _pickedCopySlot,
                SelectionMode.Clear => slotInfo.Empty,
                SelectionMode.Change => i == gameState.SaveSlot,
                _ => false
            };
            
            saveSlotInfoList.Add(item);
        }
        
        return saveSlotInfoList;
    }

    private static int GetSlotsCountToShow()
    {
        var saveDevice = new PCSaveDevice("FEZ");
        var maxExistingSlot = 0;
        for (var i = 0; i < MaxSaveSlotsCount; i++)
        {
            if (saveDevice.FileExists("SaveSlot" + i))
            {
                maxExistingSlot = i + 1;
            }
        }
        
        // extra empty slot at the end to create new ones
        var shownSlotsCount = Math.Min(maxExistingSlot + 1, MaxSaveSlotsCount);
        return shownSlotsCount;
    }

    private static SaveSlotInfo BuildSaveSlotInfo(int slot)
    {
        var slotInfo = new SaveSlotInfo();
        
        SaveData saveData = LoadSlot(slot);
        if (saveData == null)
        {
            slotInfo.Empty = true;
            return slotInfo;
        }

        var worldName = Hat.Instance.Worlds.TryGetFromSave(saveData, out var world) ? world.DisplayName : "[MISSING]";
        slotInfo.ExtraInformation = string.Format(CultureInfo.InvariantCulture, "{0} ({1:P1} - {2:dd\\.hh\\:mm})", new object[]
        {
            worldName,
            (saveData.CubeShards + saveData.SecretCubes + saveData.PiecesOfHeart + saveData.CollectedParts / 8f) / 32f,
            new TimeSpan(saveData.PlayTime)
        });
        
        // rules copied from original SaveSlotSelectionLevel
        var level = saveData.Level;
        if (string.IsNullOrEmpty(level)) level = "";
        if (level.Contains("GOMEZ_HOUSE")) level = "GOMEZ_HOUSE";
        if (level.Contains("VILLAGEVILLE") || level == "ELDERS") level = "VILLAGEVILLE_3D";
        if (level == "PYRAMID" || level == "HEX_REBUILD") level = "STARGATE";
        slotInfo.LevelName = level;

        return slotInfo;
    }

    private static void HandleSaveSelectionAction(object saveSelectionMenuLevel, int index)
    {
        var speedrunModSelected = Fez.SpeedRunMode && index == 0;
        if (speedrunModSelected)
        {
            WorldSelectionMenuBuilder.OpenSubMenu(() => InvokeBeginSpeedRun(saveSelectionMenuLevel));
            return;
        }
        
        var slotIndex = Fez.SpeedRunMode ? index -1 : index;

        if (LoadSlot(slotIndex) == null)
        {
            ServiceHelper.Get<IGameStateManager>().SaveSlot = slotIndex;
            WorldSelectionMenuBuilder.OpenSubMenu(() => InvokeChooseSaveSlot(saveSelectionMenuLevel, slotIndex));
        }
        else
        {
            InvokeChooseSaveSlot(saveSelectionMenuLevel, slotIndex);
        }
    }
    
    // save selection menu callbacks - bit hacky but easier than scrapping some of methods called in there
    private static void InvokeChooseSaveSlot(object saveSelectionMenuLevel, int index)
    {
        // ChooseSaveSlot takes SaveSlotInfo as an argument, but uses just index anyway, so dummy object works just fine
        var saveSlotInfoType = Assembly.GetAssembly(typeof(Fez))
            .GetType("FezGame.Structure.SaveSlotInfo");
        var saveSlotInfo = Activator.CreateInstance(saveSlotInfoType);
        saveSlotInfoType.GetField("Index").SetValue(saveSlotInfo, index);
        
        saveSelectionMenuLevel.GetType()?
            .GetMethod("ChooseSaveSlot", BindingFlags.NonPublic | BindingFlags.Instance)?
            .Invoke(saveSelectionMenuLevel, new[] { saveSlotInfo });
    }

    private static void InvokeBeginSpeedRun(object saveSelectionMenuLevel)
    {
        saveSelectionMenuLevel.GetType()?
            .GetMethod("BeginSpeedRun", BindingFlags.Instance | BindingFlags.NonPublic)?
            .Invoke(saveSelectionMenuLevel, null);
    }

    // reimplementation of SaveManagementLevel.ChooseSaveSlot - here it's easier than trying to hack it like above
    private static void HandleSaveManagementAction(object saveManagementMenuLevel, int slot, SelectionMode selectionMode)
    {
        var gameState = ServiceHelper.Get<IGameStateManager>();
        switch (selectionMode)
        {
            case SelectionMode.CopyPickSource:
                _pickedCopySlot = slot;
                OpenSaveManagementSubMenuForMode(saveManagementMenuLevel, SelectionMode.CopyPickTarget);
                break;
            case SelectionMode.CopyPickTarget:
                SaveData saveData = LoadSlot(_pickedCopySlot);
                if (saveData == null)
                {
                    break;
                }
                SaveSlot(slot, saveData);
                // different to original - restart save file if we've just modified it.
                if (gameState.SaveSlot == slot)
                {
                    gameState.LoadSaveFile(gameState.Restart);
                    SpeedRun.Dispose();
                    break;
                }
                ReturnToMainSaveManagementMenu(saveManagementMenuLevel);
                break;
            case SelectionMode.Clear:
                ClearSlot(slot);
                if (gameState.SaveSlot == slot)
                {
                    // different to original - boot up save selection again
                    gameState.SaveSlot = -1;
                    gameState.Restart();
                    SpeedRun.Dispose();
                    break;
                }
                ReturnToMainSaveManagementMenu(saveManagementMenuLevel);
                break;
            case SelectionMode.Change:
                void ReloadSave(int reloadedSlot)
                {
                    gameState.SaveSlot = reloadedSlot;
                    gameState.LoadSaveFile(delegate
                    {
                        gameState.Save();
                        gameState.SaveImmediately();
                        gameState.Restart();
                    });
                    SpeedRun.Dispose();
                }
                
                // different to original - open up world selection menu if opening empty slot
                if (LoadSlot(slot) == null)
                {
                    WorldSelectionMenuBuilder.OpenSubMenu(() => ReloadSave(slot));
                    break;
                }

                ReloadSave(slot);
                break;
        }
    }

    private static void AppendSaveManagementWarningCallback(
        MenuMediator.LevelTemplate levelTemplate, object saveManagementMenuLevel, string locString)
    {
        if (string.IsNullOrEmpty(locString))
        {
            return;
        }

        var spriteFont = ServiceHelper.Get<IFontManager>().Small;
        levelTemplate.OnPostDraw += (batch, font, tr, alpha) => 
            saveManagementMenuLevel.GetType()
            .GetMethod("DrawWarning",  BindingFlags.NonPublic | BindingFlags.Instance)?
            .Invoke(saveManagementMenuLevel, new object[] { batch, spriteFont, tr, alpha, locString });
    }

    private static void OpenSaveManagementSubMenuForMode(object saveManagementMenuLevel, SelectionMode selectionMode)
    {
        var menuBase = saveManagementMenuLevel.GetType()
            .GetField("Menu", BindingFlags.NonPublic | BindingFlags.Instance)?
            .GetValue(saveManagementMenuLevel);

        var saveManagementMenuHandler = CreateMenuHandler(
            selectionMode, index => HandleSaveManagementAction(saveManagementMenuLevel, index, selectionMode));

        AppendSaveManagementWarningCallback(
            saveManagementMenuHandler.LevelTemplate, saveManagementMenuLevel, selectionMode switch
            {
                SelectionMode.Change => "SaveChangeWarning",
                SelectionMode.Clear => "SaveClearWarning",
                SelectionMode.CopyPickTarget => "SaveCopyWarning",
                _ => ""
            }
        );
        
        var menuLevel = MenuMediator.OpenSubMenuLevel(menuBase, saveManagementMenuHandler.LevelTemplate);
        saveManagementMenuHandler.MarkLevelForRebuilding(menuLevel);
    }

    private static void ReturnToMainSaveManagementMenu(object saveManagementMenuLevel)
    {
        var menuBase = saveManagementMenuLevel.GetType()
            .GetField("Menu", BindingFlags.NonPublic | BindingFlags.Instance)?
            .GetValue(saveManagementMenuLevel);
        MenuMediator.OpenMenuLevel(menuBase, saveManagementMenuLevel);
    }

    private static SaveData LoadSlot(int slot)
    {
        var saveDevice = new PCSaveDevice("FEZ");
        SaveData saveData = null;
        var copyFromSlotName = "SaveSlot" + slot;
        void LoadSaveDataAction(BinaryReader stream) => saveData = SaveFileOperations.Read(new CrcReader(stream));
        if (!saveDevice.FileExists(copyFromSlotName) || !saveDevice.Load(copyFromSlotName, LoadSaveDataAction))
        {
            return null;
        }
        
        return saveData;
    }

    private static void SaveSlot(int slot, SaveData saveData)
    {
        new PCSaveDevice("FEZ").Save(
            "SaveSlot" + slot, writer => SaveFileOperations.Write(new CrcWriter(writer), saveData));
    }

    private static void ClearSlot(int slot)
    {
        new PCSaveDevice("FEZ").Delete("SaveSlot" + slot);
    }
    
    private enum SelectionMode
    {
        Load,
        Change,
        Clear,
        CopyPickSource,
        CopyPickTarget,
    }

    private struct SaveSlotInfo
    {
        public bool Empty;
        public string LevelName;
        public string ExtraInformation;
    }
}