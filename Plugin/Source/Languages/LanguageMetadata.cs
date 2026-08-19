using System.Xml.Serialization;

namespace HatModLoader.Source.Languages
{
    [Serializable]
    [XmlRoot("Language")]
    public class LanguageMetadata
    {
        public string Code { get; set; }

        public string DisplayName { get; set; }

        public string Locale { get; set; }

        public string SmallFont { get; set; }

        public string BigFont { get; set; }
    }
}
