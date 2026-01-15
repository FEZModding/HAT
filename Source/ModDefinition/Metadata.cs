using System.Xml.Serialization;

namespace HatModLoader.Source.ModDefinition
{
    [Serializable]
    public struct Metadata
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public string Author { get; set; }

        [XmlIgnore] public Version Version { get; set; }

        [XmlElement("Version")]
        public string VersionString
        {
            get => Version.ToString();
            set
            {
                if (!string.IsNullOrEmpty(value) && Version.TryParse(value, out var version))
                {
                    Version = version;
                }
            }
        }

        public string LibraryName { get; set; }

        public DependencyInfo[] Dependencies { get; set; }

        [Serializable]
        public struct DependencyInfo
        {
            [XmlAttribute] public string Name { get; set; }

            [XmlIgnore] public Version MinimumVersion { get; set; }

            [XmlAttribute("MinimumVersion")]
            public string MinimumVersionString
            {
                get => MinimumVersion.ToString();
                set
                {
                    if (!string.IsNullOrEmpty(value) && Version.TryParse(value, out var version))
                    {
                        MinimumVersion = version;
                    }
                }
            }
        }
    }
}