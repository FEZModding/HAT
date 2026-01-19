using System.Xml.Serialization;
using Common;
using HatModLoader.Source.FileProxies;

namespace HatModLoader.Source.ModDefinition
{
    [Serializable]
    public struct Metadata
    {
        private const string ModMetadataFile = "Metadata.xml";
        
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
        
        public static bool TryLoad(IFileProxy proxy, out Metadata metadata)
        {
            if (!proxy.FileExists(ModMetadataFile))
            {
                metadata = default;
                return false;
            }

            try
            {
                using var stream = proxy.OpenFile(ModMetadataFile);
                using var reader = new StreamReader(stream);

                var serializer = new XmlSerializer(typeof(Metadata));
                metadata = (Metadata)serializer.Deserialize(reader);
                if (string.IsNullOrEmpty(metadata.Name) || metadata.Version == null)
                {
                    metadata = default;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("HAT", LogSeverity.Warning, $"Failed to load mod metadata: {ex.Message}");
                metadata = default;
                return false;
            }
        }

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