
using System.Xml.Serialization;
using Common;
using HatModLoader.Source.FileProxies;

namespace HatModLoader.Source.Worlds
{
    [Serializable]
    public class WorldMetadata
    {
        private const string WorldMetadataFile = "World.xml";

        public static readonly WorldMetadata Fez = new()
        {
            DisplayName = "FEZ",
            Description = "Vanilla experience",
            MapTree = "maptree",
            Thumbnail = "Other Textures/map_screens/villageville_3d",
            StartingLevel = "GOMEZ_HOUSE_2D",
            StartingFlags = new List<string>(),
        };

        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string MapTree { get; set; }
        public string Thumbnail { get; set; }
        public string StartingLevel { get; set; }
        public List<string> StartingFlags { get; set; }

        public static bool TryLoad(IFileProxy proxy, out WorldMetadata metadata)
        {
            if (!proxy.FileExists(WorldMetadataFile))
            {
                metadata = default;
                return false;
            }

            try
            {
                using var stream = proxy.OpenFile(WorldMetadataFile);
                using var reader = new StreamReader(stream);

                var serializer = new XmlSerializer(typeof(WorldMetadata));
                metadata = (WorldMetadata)serializer.Deserialize(reader);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("HAT", LogSeverity.Warning,
                    $"Failed to load world metadata in \"{proxy.ContainerName}\": {ex.Message}");
                metadata = default;
                return false;
            }
        }
    }
}
