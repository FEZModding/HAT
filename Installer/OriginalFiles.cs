using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Serialization;

// ReSharper disable NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract

namespace FEZ.HAT.Installer;

[Serializable]
[XmlRoot]
public sealed class OriginalFiles
{
    [XmlElement("Group")] public Group[] Groups { get; set; } = [];

    public static Group Load(Stream stream, Platform platform)
    {
        var serializer = new XmlSerializer(typeof(OriginalFiles));
        using var reader = XmlReader.Create(stream);
        var config = (OriginalFiles?)serializer.Deserialize(reader);

        var selected = config!.Groups
            .Where(group => group.Platform == Platform.Common || group.Platform == platform)
            .ToArray();

        return new Group
        {
            Platform = platform,
            Files = selected.SelectMany(group => group.Files ?? []).ToArray(),
            Directories = selected.SelectMany(group => group.Directories ?? []).ToArray()
        };
    }

    [Serializable]
    public sealed class Group
    {
        [XmlAttribute] public Platform Platform { get; set; }

        [XmlElement("File")] public string[] Files { get; set; } = [];

        [XmlElement("Directory")] public string[] Directories { get; set; } = [];
    }

    public enum Platform
    {
        [XmlEnum("common")] Common,
        [XmlEnum("windows")] Windows,
        [XmlEnum("linux")] Linux,
        [XmlEnum("osx")] Osx
    }
}

public static class OriginalFilesExtensions
{
    public static OriginalFiles.Platform GetPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return OriginalFiles.Platform.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return OriginalFiles.Platform.Linux;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return OriginalFiles.Platform.Osx;
        return OriginalFiles.Platform.Common;
    }
}
