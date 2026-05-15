using FezEngine.Structure;
using MonoMod.RuntimeDetour;

namespace HatModLoader.Installers;

public class EmptyPathsFixesInstaller : IHatInstaller
{
    private static IDetour LevelPathsNullifierHook;
    
    public void Install()
    {
        // Some properties, especially in Level, are used as partial paths for assets. They're optional,
        // as the game makes sure to skip the logic if a property contains a null value. Unfortunately,
        // this doesn't prevent the logic from execution if the value is an empty string, which is also invalid.
        // This future-proofs future mistakes related to this, but also resolves backwards-compability issue
        // of older .fezlvl.json assets being serialized with non-null properties using Repacker >= 1.3.0
        
        LevelPathsNullifierHook = new Hook(
            typeof(Level).GetMethod("OnDeserialization"), 
            new Action<Action<Level>, Level>((orig, self) =>
            {
                orig(self);
                self.TrileSetName = NullIfEmpty(self.TrileSetName);
                self.SongName = NullIfEmpty(self.SongName);
                self.GomezHaloName = NullIfEmpty(self.GomezHaloName);
            })
        );
    }

    private static string NullIfEmpty(string property)
    {
        return string.IsNullOrEmpty(property) ? null : property;
    }
    
    public void Uninstall()
    {
        LevelPathsNullifierHook.Dispose();
    }
}