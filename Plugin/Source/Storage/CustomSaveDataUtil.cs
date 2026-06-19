using FezGame.Structure;

namespace HatModLoader.Source.Storage;

// HAT is storing custom save data in what seems to be unused list of strings "EarnedGamerPictures".
// If it turns out to be used on any platform (presumably Xbox?), we should write a patch to prevent
// our custom data from colliding with any logic (if even).
public static class CustomSaveDataUtil
{
    public static string GetCustomData(this SaveData saveData, string key, string defaultValue)
    {
        return saveData.TryGetCustomData(key, out var value) ? value : defaultValue;
    }

    public static bool TryGetCustomData(this SaveData saveData, string key, out string value)
    {
        var dataPrefix = GetCustomDataPrefix(key);
        var existingRecord = saveData.EarnedGamerPictures.FindIndex(text => text.StartsWith(dataPrefix));
        
        if (existingRecord >= 0)
        {
            value = saveData.EarnedGamerPictures[existingRecord].Substring(dataPrefix.Length);
            return true;
        }
        value = null;
        return false;
    }

    public static void SetCustomData(this SaveData saveData, string key, string value)
    {
        var dataPrefix = GetCustomDataPrefix(key);
        var newValue = dataPrefix + value;
        var existingRecord = saveData.EarnedGamerPictures.FindIndex(text => text.StartsWith(dataPrefix));
        
        if (existingRecord >= 0)
        {
            saveData.EarnedGamerPictures[existingRecord] = newValue;
        }
        else
        {
            saveData.EarnedGamerPictures.Add(newValue);
        }
    }

    private static string GetCustomDataPrefix(string key)
    {
        return $"{key}:";
    }
}