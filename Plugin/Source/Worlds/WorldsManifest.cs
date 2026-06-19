using Common;
using FezGame.Structure;
using HatModLoader.Source.ModDefinition;
using HatModLoader.Source.Storage;

namespace HatModLoader.Source.Worlds;

public class WorldsManifest
{
    public const string CustomSaveDataKey = "WorldModName";
    
    private Dictionary<ModContainer, WorldMetadata> _worlds = new();
    
    public WorldsManifest(List<ModContainer> mods)
    {
        foreach (var mod in mods)
        {
            if (WorldMetadata.TryLoad(mod.FileProxy, out WorldMetadata world))
            {
                _worlds.Add(mod, world);
                Logger.Log("HAT", $"World metadata found in mod {mod.Metadata.Name} - {world.DisplayName} ({world.Description})");
            }
        }
        Logger.Log("HAT", $"Worlds manifest initialized (custom worlds: {_worlds.Count})");
    }

    public IEnumerable<WorldMetadata> EnumerateAllWorlds()
    {
        yield return WorldMetadata.Fez;
        foreach (var mod in _worlds)
        {
            yield return mod.Value;
        }
    }

    public bool TryGet(ModContainer mod, out WorldMetadata metadata)
    {
        return _worlds.TryGetValue(mod, out metadata);
    }

    public bool TryGet(string modName, out WorldMetadata world)
    {
        var matchingNameMod = _worlds.Keys.FirstOrDefault(mod => mod.Metadata.Name.Equals(modName));
        if (matchingNameMod == null)
        {
            world = null;
            return false;
        }
        return TryGet(matchingNameMod, out world);
    }

    public bool TryGetOwningMod(WorldMetadata ownedWorld, out ModContainer modContainer)
    {
        foreach (var modPair in _worlds)
        {
            if (modPair.Value == ownedWorld)
            {
                modContainer = modPair.Key;
                return true;
            }
        }

        modContainer = null;
        return false;
    }

    public bool TryGetFromSave(SaveData saveData, out WorldMetadata world)
    {
        if (saveData == null)
        {
            world = null;
            return false;
        }
        
        if (!saveData.TryGetCustomData(CustomSaveDataKey, out var modName))
        {
            world = WorldMetadata.Fez;
            return true;
        }

        if (!TryGet(modName, out var metadata))
        {
            world = null;
            return false;
        }
        
        world = metadata;
        return true;
    }

    public void SetInSave(SaveData saveData, WorldMetadata world)
    {
        if (saveData == null)
        {
            return;
        }
        
        if (world == WorldMetadata.Fez)
        {
            // lack of world identifier already qualifies as FEZ world.
            return;
        }

        if (!TryGetOwningMod(world, out var modContainer))
        {
            // this should not happen!
            throw new Exception($"Metadata for world \"{world.DisplayName}\" doesn't exist in manifest.");
        }
        
        saveData.SetCustomData(CustomSaveDataKey, modContainer.Metadata.Name);
    }
}