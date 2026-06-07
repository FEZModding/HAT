using FezGame.Structure;

namespace HatModLoader.Source.Storage;

// HAT is storing dummy level data with its ScriptingState as custom data container.
// Almost nothing in the game is iterating over the level data, so this should be safe operation.
public static class CustomSaveDataUtil
{
    private static string CustomDataPrefix = "$HAT$_";

    public static string GetCustomData(this SaveData saveData, string key, string defaultValue)
    {
        return saveData.TryGetCustomData(key, out var value) ? value : defaultValue;
    }

    public static bool TryGetCustomData(this SaveData saveData, string key, out string value)
    {
        if (saveData.World.TryGetValue($"{CustomDataPrefix}{key}", out var levelSaveData))
        {
            value = levelSaveData.ScriptingState;
            return true;
        }
        value = null;
        return false;
    }

    public static void SetCustomData(this SaveData saveData, string key, string value)
    {
        if (!saveData.World.TryGetValue($"{CustomDataPrefix}{key}", out var levelSaveData))
        {
            levelSaveData = new LevelSaveData();
            saveData.World.Add($"{CustomDataPrefix}{key}", levelSaveData);
        }
        levelSaveData.ScriptingState = value;
    }
}