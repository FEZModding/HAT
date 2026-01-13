using Common;
using HatModLoader.Source;
using System.Globalization;

namespace FezGame
{
    internal static class patch_Program
    {
        private static extern void orig_Main(string[] args);

        private static void Main(string[] args)
        {
            // Ensuring that required dependencies can be resolved before anything else.
            Hat.RegisterRequiredDependencyResolvers();

            // Ensure uniform culture
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");

            // The game is encapsulating the main game component in a Logger-based try-catch.
            // However, occasionally, error can occur during HAT initialisation, or when the
            // game is shutting down. We want to keep track of it.

            Logger.Try(orig_Main, args);
        }
    }
}
