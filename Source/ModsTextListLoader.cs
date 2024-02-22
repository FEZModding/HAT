
namespace HatModLoader.Source
{
    internal static class ModsTextListLoader
    {
        private static bool Exists(string path)
        {
            return File.Exists(path);
        }

        public static HashSet<string> Load(string path)
        {
            var modsList = new HashSet<string>();

            if(!Exists(path)) return modsList;

            var fileContents = File.ReadAllText(path);

            foreach(var line in fileContents.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var clearedLine = line.Trim();
                if(clearedLine.StartsWith("#")) continue;
                if(clearedLine.Length > 0) modsList.Add(clearedLine);
            }

            return modsList;
        }

        public static HashSet<string> LoadOrCreateDefault(string path, string defaultContent)
        {
            if(!Exists(path))
            {
                File.WriteAllText(path, defaultContent);
            }
            return Load(path);
        }
    }
}
