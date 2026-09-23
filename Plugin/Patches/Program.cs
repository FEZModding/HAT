using Common;
using HatModLoader.Installers;
using HatModLoader.Source;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace FezGame
{
    internal static class patch_Program
    {
        [MethodImpl(MethodImplOptions.ForwardRef)]
        private static extern void orig_Main(string[] args);

        private static void Main(string[] args)
        {
            // Ensuring that required dependencies can be resolved before anything else.
            Hat.RegisterRequiredDependencyResolvers();

            // Ensure uniform culture
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            LoggerModifier.InstallConsoleLogging();

            // The game is encapsulating the main game component in a Logger-based try-catch.
            // However, occasionally, error can occur during HAT initialisation, or when the
            // game is shutting down. We want to keep track of it.

            // Use the game's own option to disable its sometimes buggy multithreaded mode on every launch.
            var gameArgs = new string[args.Length + 1];
            Array.Copy(args, gameArgs, args.Length);
            gameArgs[^1] = "--singlethreaded";
            Logger.Try(orig_Main, gameArgs);
        }
    }
}
