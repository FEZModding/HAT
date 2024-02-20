using Common;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace HatModLoader.Source.ModDefinition
{
    [Serializable]
    [XmlType(TypeName = "Metadata")]
    public struct ModMetadata
    {
        public string Name;
        public string Description;
        public string Author;
        public string Version;
        public string LibraryName;
        public ModDependencyInfo[] Dependencies;

        public static bool TryLoadFrom(Stream stream, out ModMetadata metadata)
        {
            try
            {
                var serializer = new XmlSerializer(typeof(ModMetadata));
                using var reader = new StreamReader(stream);
                metadata = (ModMetadata)serializer.Deserialize(reader);

                if (metadata.Name == null || metadata.Name.Length == 0) return false;
                if (metadata.Version == null || metadata.Version.Length == 0) return false;

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("HAT", LogSeverity.Warning, $"Failed to load mod metadata: {ex.Message}");
                metadata = default;
                return false;
            }
        }

        public static int CompareVersions(string ver1, string ver2)
        {
            string tokensPattern = @"(\d+|\D+)";
            string[] TokensVer1 = Regex.Split(ver1, tokensPattern);
            string[] TokensVer2 = Regex.Split(ver2, tokensPattern);

            for (int i = 0; i < Math.Min(TokensVer1.Length, TokensVer2.Length); i++)
            {
                if (int.TryParse(TokensVer1[i], out int tokenInt1) && int.TryParse(TokensVer2[i], out int tokenInt2))
                {
                    if (tokenInt1 > tokenInt2) return 1;
                    if (tokenInt1 < tokenInt2) return -1;
                    continue;
                }
                int comparison = TokensVer1[i].CompareTo(TokensVer2[i]);
                if (comparison < 0) return 1;
                if (comparison > 0) return -1;
            }
            if (TokensVer1.Length > TokensVer2.Length) return 1;
            if (TokensVer1.Length < TokensVer2.Length) return -1;
            return 0;
        }
    }
}
