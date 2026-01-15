using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace HatModLoader.Source.ModDefinition
{
    [Serializable]
    public struct Metadata
    {
        public string Name { get; set; }
        
        public string Description { get; set; }
        
        public string Author { get; set; }
        
        public string Version { get; set; }
        
        public string LibraryName { get; set; }
        
        public DependencyInfo[] Dependencies { get; set; }

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
        
        [Serializable]
        public struct DependencyInfo
        {
            [XmlAttribute]
            public string Name { get; set; }
            
            [XmlAttribute]
            public string MinimumVersion { get; set; }
        }
    }
}
