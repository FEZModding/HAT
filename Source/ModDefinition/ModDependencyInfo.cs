using System.Xml.Serialization;

namespace HatModLoader.Source.ModDefinition
{
    [Serializable]
    [XmlType(TypeName = "DependencyInfo")]
    public struct ModDependencyInfo
    {
        [XmlAttribute] public string Name;
        [XmlAttribute] public string MinimumVersion;
    }
}
