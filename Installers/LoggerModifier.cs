using Common;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using System.Reflection;

namespace HatModLoader.Installers
{
    internal class LoggerModifier : IHatInstaller
    {
        private static readonly string LogDirectory = "Debug Logs";

        public static IDetour LogDetour;

        public void Install()
        {
            LogDetour = new Hook(
                typeof(Logger).GetMethod("Log", new Type[] { typeof(string), typeof(LogSeverity), typeof(string) }),
                new Action<Action<string, LogSeverity, string>, string, LogSeverity, string>((orig, component, severity, message) => {
                    orig(component, severity, message);
                    LogCrashHandler(component, severity, message);
                })
            );

            SetCustomLoggerPath();
        }

        private static void SetCustomLoggerPath()
        {
            var logPath = Path.Combine(Util.LocalSaveFolder, LogDirectory);

            if (!Directory.Exists(logPath))
            {
                Directory.CreateDirectory(logPath);
            }

            var logFilePath = Path.Combine(logPath, $"[{DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")}] Debug Log.txt");

            typeof(Logger).GetField("FirstLog", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
            typeof(Logger).GetField("LogFilePath", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, logFilePath);
        }

        private static void LogCrashHandler(string component, LogSeverity severity, string message)
        {
            if (severity != LogSeverity.Error) return;
            var FNAPlatformType = Assembly.GetAssembly(typeof(Game)).GetType("Microsoft.Xna.Framework.SDL2_FNAPlatform");
            var ShowRuntimeErrorFunc = FNAPlatformType.GetMethod("ShowRuntimeError", BindingFlags.Public | BindingFlags.Static);
            ShowRuntimeErrorFunc.Invoke(null, new object[] { $"FEZ [{component}]", message });
        }

        public void Uninstall()
        {
            LogDetour.Dispose();
        }
    }
}
