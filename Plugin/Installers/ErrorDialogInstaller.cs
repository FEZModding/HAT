using System.Reflection;
using FezEngine.Tools;
using FezGame.Components;
using HatModLoader.Source;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using SDL2;

namespace HatModLoader.Installers;

internal class ErrorDialogInstaller : IHatInstaller
{
    private IDetour _doSetupHook;

    public void Install(Hat hat)
    {
        var doSetup = typeof(GameLightingPostProcess).GetMethod(
            "DoSetup",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        _doSetupHook = new ILHook(doSetup, PatchDoSetup);
    }

    private static void PatchDoSetup(ILContext il)
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(
                MoveType.Before,
                instruction => instruction.OpCode == OpCodes.Call &&
                               instruction.Operand is MethodReference
                               {
                                   Name: "LogError",
                                   DeclaringType.FullName: "Common.Logger"
                               }))
        {
            throw new InvalidOperationException("Could not find GameLightingPostProcess's error log call.");
        }

        var logErrorCall = cursor.Next;
        // LogError starts the catch handler, so the injected dup must become its first instruction.
        cursor.Goto(logErrorCall, MoveType.AfterLabel);
        cursor.Emit(OpCodes.Dup);

        // AssemblyConverter removes the WinForms call; the original catch still logs and exits.
        cursor.Goto(logErrorCall, MoveType.After);
        cursor.EmitDelegate(ShowFatalErrorDialog);
    }

    private static void ShowFatalErrorDialog(Exception exception)
    {
        SDL.SDL_ShowSimpleMessageBox(
            SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR,
            "FEZ - Fatal Error",
            exception.ToString(),
            ServiceHelper.Game.Window.Handle
        );
    }

    public void Uninstall()
    {
        _doSetupHook?.Dispose();
    }
}