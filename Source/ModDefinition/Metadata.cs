using System.Runtime.InteropServices;
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

        [XmlIgnore] public Version Version { get; private set; }

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
        
        public NativeLibrary[] NativeDependencies { get; set; }
        
        public static bool TryLoad(IFileProxy proxy, out Metadata metadata)
        {
            if (!proxy.FileExists(ModMetadataFile))
            {
                Logger.Log("HAT", LogSeverity.Warning, $"No mod metadata found in \"{proxy.ContainerName}\"");
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
                    Logger.Log("HAT", LogSeverity.Warning, $"Invalid mod metadata in \"{proxy.ContainerName}\"");
                    metadata = default;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("HAT", LogSeverity.Warning, $"Failed to load mod metadata \"{proxy.ContainerName}\": {ex.Message}");
                metadata = default;
                return false;
            }
        }

        [Serializable]
        public struct DependencyInfo
        {
            [XmlAttribute] public string Name { get; set; }

            [XmlIgnore] public Version MinimumVersion { get; private set; }

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
        
        [Serializable]
        public struct NativeLibrary
        {
            [XmlAttribute] public Architecture Architecture { get; set; }
    
            [XmlIgnore] public OSPlatform Platform { get; set; }
            
            [XmlAttribute("Platform")]
            public string PlatformString
            {
                get
                {
                    if (Platform == OSPlatform.Windows) return "Windows";
                    if (Platform == OSPlatform.Linux) return "Linux";
                    return Platform == OSPlatform.OSX ? "OSX" : "Unknown";
                }
                set
                {
                    Platform = value switch
                    {
                        "Windows" => OSPlatform.Windows,
                        "Linux" => OSPlatform.Linux,
                        "OSX" => OSPlatform.OSX,
                        _ => throw new ArgumentException($"Unknown platform: {value}")
                    };
                }
            }

            [XmlText]
            public string Path
            {
                get => _path;
                set => _path = value?.Trim();
            }
    
            private string _path;
        }
    }
}