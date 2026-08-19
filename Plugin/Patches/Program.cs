using Common;
using HatModLoader.Source;
using System.Globalization;
using System.Runtime.CompilerServices;
using HatModLoader.Source.Languages;

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

            // Resolve a pack display name before FEZ deserializes its settings enum.
            LanguagePackManager.RestorePersistedLanguage();

            // Ensure uniform culture
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            // The game is encapsulating the main game component in a Logger-based try-catch.
            // However, occasionally, error can occur during HAT initialisation, or when the
            // game is shutting down. We want to keep track of it.

            Logger.Try(orig_Main, args);

            // FEZ's final save writes custom enum values as null.
            LanguagePackManager.PersistSelectedLanguage();
        }
    }
}
