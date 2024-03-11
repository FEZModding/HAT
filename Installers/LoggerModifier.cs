using Common;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using System.Reflection;

namespace HatModLoader.Installers
{
    internal class LoggerModifier : IHatInstaller
    {
        private static readonly string LogDirectory = "Debug Logs";
        private static string CustomLoggerPath => Path.Combine(Util.LocalSaveFolder, LogDirectory);
        
        private static readonly int MaximumLogDays = 30;

        public static Hook LogDetour;

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
            MoveOriginalLogsToCustomLoggerPath();
            RemoveFilesOlderThanDays(MaximumLogDays);
        }

        private static string GetTimestampedLogFileName(DateTime date, int index = 0)
        {
            return
                index == 0
                ? $"[{date.ToString("yyyy-MM-dd_HH-mm-ss")}] Debug Log.txt"
                : $"[{date.ToString("yyyy-MM-dd_HH-mm-ss")}] Debug Log #{index+1}.txt";
        }

        private static string GetUniqueCustomLogFileName(DateTime date)
        {
            string path;
            int i = 0;
            do path = Path.Combine(CustomLoggerPath, GetTimestampedLogFileName(date, i++));
            while (File.Exists(path));
            return path;
        }

        private static void SetCustomLoggerPath()
        {
            if (!Directory.Exists(CustomLoggerPath))
            {
                Directory.CreateDirectory(CustomLoggerPath);
            }

            var logFilePath = GetUniqueCustomLogFileName(DateTime.Now);

            typeof(Logger).GetField("FirstLog", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
            typeof(Logger).GetField("LogFilePath", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, logFilePath);
        }

        private static void MoveOriginalLogsToCustomLoggerPath()
        {
            foreach(var file in Directory.EnumerateFiles(Util.LocalSaveFolder, "*Debug Log*.txt"))
            {
                var fileCreationDate = File.GetCreationTime(file);
                var newPath = GetUniqueCustomLogFileName(fileCreationDate);
                File.Move(file, newPath);
            }
        }

        private static void RemoveFilesOlderThanDays(int days)
        {
            foreach (var file in Directory.EnumerateFiles(CustomLoggerPath, "*Debug Log*.txt"))
            {
                if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalDays > days)
                {
                    File.Delete(file);
                }
            }
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
