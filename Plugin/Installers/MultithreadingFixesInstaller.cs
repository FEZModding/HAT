using System.Reflection;
using FezGame.Components;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace HatModLoader.Installers;

public class MultithreadingFixesInstaller : IHatInstaller
{
    private static IDetour IntroDoPanDownHook;
    
    public void Install()
    {
        IntroDoPanDownHook = PatchIntroPanDownDeadlock();
    }
    
    public void Uninstall()
    {
        IntroDoPanDownHook.Dispose();
    }
    
    private IDetour PatchIntroPanDownDeadlock()
    {
        // Intro's phase progress in UpdateLogo is dependent on "IntroPanDown.DoPanDown" flag to not yet be set.
        // However, with multithreading and performant enough setup, ScreenCapture callback of TileTransition
        // in DoPanDown can set this flag before phase could be progressed from Wait state, causing Intro to be
        // stuck in this state and to never end. Most notable result of that is camera being locked in one place,
        // having difficulties following Gomez.
        
        // I have strong belief that phase progression should not be dependent on that flag being false, but true
        // (as in, "if we ARE panning down, move to next phase where we actually check for panning down to end").
        // We're making ILHook here to invert that flag check in a conditional statement.

        var UpdateLogoFunc = typeof(Intro).GetMethod("UpdateLogo",
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        return new ILHook(UpdateLogoFunc, il =>
        {
            var cursor = new ILCursor(il);

            // "if (... && !IntroPanDown.DoPanDown)"...
            cursor.GotoNext(MoveType.After, i =>
                i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference { Name: "DoPanDown" });

            // ...turned into "if (... && IntroPanDown.DoPanDown)" 
            cursor.Next.OpCode = OpCodes.Brfalse_S; // (skip next logic if statement false)
        });
    }
}