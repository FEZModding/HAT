using System.Globalization;
using System.Reflection;
using Common;
using EasyStorage;
using FezEngine.Tools;
using FezGame;
using FezGame.Structure;
using FezGame.Tools;

namespace HatModLoader.Source.Menu;

internal static class HatSaveSelectionMenuBuilder
{
    private const int MaxSaveSlotsCount = 32;
    
    private static ListMenuHandler _saveSelectionMenuHandler;
    
    public static void InitializeWithSaveSelectionMenuLevel(object saveSelectionMenuLevel)
    {
        SetupSaveSelectionMenuHandler(saveSelectionMenuLevel);
        MenuMediator.BuildMenuLevelObject(_saveSelectionMenuHandler.LevelTemplate, saveSelectionMenuLevel);
    }

    private static void SetupSaveSelectionMenuHandler(object saveSelectionMenuLevel)
    {
        _saveSelectionMenuHandler = new ListMenuHandler(new MenuMediator.LevelTemplate
        {
            Title = "SaveSlotTitle",
            AButtonString = "ChooseWithGlyph",
            BButtonString = "ExitWithGlyph",
        });

        _saveSelectionMenuHandler.ScrollPrefix = StaticText.GetString("SaveSlotPrefix");
        _saveSelectionMenuHandler.DefaultIndex = GetDefaultItemIndex;
        _saveSelectionMenuHandler.Items = BuildSaveSlotInfoList();
        _saveSelectionMenuHandler.OnSelect = index => InvokeSelection(saveSelectionMenuLevel, index);
        
        _saveSelectionMenuHandler.Initialize();
    }

    private static int GetDefaultItemIndex()
    {
        return Fez.SpeedRunMode ? 1 : 0;
    }

    private static List<ListMenuHandler.Item> BuildSaveSlotInfoList()
    {
        var saveSlotInfoList = new List<ListMenuHandler.Item>();

        if (Fez.SpeedRunMode)
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
            saveSlotInfoList.Add(BuildSaveSlotInfo(i));
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

    private static ListMenuHandler.Item BuildSaveSlotInfo(int slot)
    {
        var item = new ListMenuHandler.Item
        {
            Labels = new List<string>() { StaticText.GetString("NewSlot") },
        };
        
        var saveDevice = new PCSaveDevice("FEZ");
        var fileName = "SaveSlot" + slot;
        
        SaveData saveData = null;
        void LoadSaveDataAction(BinaryReader stream) => saveData = SaveFileOperations.Read(new CrcReader(stream));
        if (!saveDevice.FileExists(fileName) || !saveDevice.Load(fileName, LoadSaveDataAction) || saveData == null)
        {
            return item;
        }

        var worldName = "FEZ";
        var saveDataInfo = string.Format(CultureInfo.InvariantCulture, "({0:P1} - {1:dd\\.hh\\:mm})", new object[]
        {
            (saveData.CubeShards + saveData.SecretCubes + saveData.PiecesOfHeart + saveData.CollectedParts / 8f) / 32f,
            new TimeSpan(saveData.PlayTime)
        });
        
        item.Labels.Clear();
        item.Labels.Add(worldName);
        item.Labels.Add(saveDataInfo);
        
        // rules copied from original SaveSlotSelectionLevel
        var level = saveData.Level;
        if (level.Contains("GOMEZ_HOUSE")) level = "GOMEZ_HOUSE";
        if (level.Contains("VILLAGEVILLE") || level == "ELDERS") level = "VILLAGEVILLE_3D";
        if (level == "PYRAMID" || level == "HEX_REBUILD") level = "STARGATE";
        item.ThumbnailPath = $"Other Textures/map_screens/{level}";

        return item;
    }

    private static void InvokeSelection(object saveSelectionMenuLevel, int index)
    {
        if (Fez.SpeedRunMode)
        {
            index -= 1;
        }

        if (index < 0)
        {
            saveSelectionMenuLevel.GetType()?
                .GetMethod("BeginSpeedRun", BindingFlags.Instance | BindingFlags.NonPublic)?
                .Invoke(saveSelectionMenuLevel, null);
            return;
        }
        
        // ChooseSaveSlot takes SaveSlotInfo as an argument, but uses just index anyway, so dummy object works just fine
        var saveSlotInfoType = Assembly.GetAssembly(typeof(Fez))
            .GetType("FezGame.Structure.SaveSlotInfo");
        var saveSlotInfo = Activator.CreateInstance(saveSlotInfoType);
        saveSlotInfoType.GetField("Index").SetValue(saveSlotInfo, index);
        
        Logger.Log("DEBUG", $"{index}, {saveSlotInfo}, {saveSelectionMenuLevel}");
        
        saveSelectionMenuLevel.GetType()?
            .GetMethod("ChooseSaveSlot", BindingFlags.NonPublic | BindingFlags.Instance)?
            .Invoke(saveSelectionMenuLevel, new[] { saveSlotInfo });
    }
}