
using System.ComponentModel;
using System.Xml.Serialization;
using Common;
using FezGame.Structure;
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
            StartingSaveFields = new List<SaveFieldDefinition>(),
            AlwaysBlackHoleLevels = new List<string> { "NUZU_ABANDONED_B", "STARGATE_RUINS", "WALL_INTERIOR_HOLE" },
        };

        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string MapTree { get; set; }
        public string Thumbnail { get; set; }
        public string StartingLevel { get; set; }
        
        [XmlArray, XmlArrayItem("Field")]
        public List<SaveFieldDefinition> StartingSaveFields { get; set; } = new();
        
        [XmlArray, XmlArrayItem("Level")]
        public List<string> AlwaysBlackHoleLevels { get; set; } = new();

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

        public void AssignStartingSaveFieldsTo(SaveData saveData)
        {
            foreach (var fieldDefinition in StartingSaveFields)
            {
                if (string.IsNullOrEmpty(fieldDefinition.Value) || string.IsNullOrEmpty(fieldDefinition.Value))
                {
                    continue;
                }
                
                var field = saveData.GetType().GetField(fieldDefinition.Name);
                if (field == null)
                {
                    Logger.Log("HAT", LogSeverity.Warning,
                        $"Unknown field \"{fieldDefinition.Name}\" defined in world \"{DisplayName}\"");
                    continue;
                }

                var fieldType = field.FieldType;
                if (!fieldType.IsPrimitive && fieldType != typeof(string))
                {
                    Logger.Log("HAT", LogSeverity.Warning,
                        $"Invalid field \"{fieldDefinition.Name}\" defined in world \"{DisplayName}\"");
                    continue;
                }
                var converter = TypeDescriptor.GetConverter(fieldType);
                var value = converter.ConvertFromInvariantString(fieldDefinition.Value);
                field.SetValue(saveData, value);
            }
        }

        [Serializable]
        public class SaveFieldDefinition
        {
            [XmlAttribute]
            public string Name { get; set; }
            [XmlAttribute]
            public string Value { get; set; }
        }
    }
}
