using System.Runtime.InteropServices;
using System.Xml.Serialization;

namespace FEZ.HAT.Installer;

[Serializable]
[XmlRoot("configuration")]
public sealed class FnaDllConfig
{
    [XmlElement("dllmap")] public DllMap[] Dependencies { get; set; } = [];

    public static FnaDllConfig Load(Stream stream, bool leaveOpen)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: leaveOpen);
        
        var serializer = new XmlSerializer(typeof(FnaDllConfig));
        var config = (FnaDllConfig?)serializer.Deserialize(reader);
        if (config == null)
        {
            throw new InstallerException("Embedded FNA.dll.config is invalid.");
        }
        
        var platform = FnaDllConfigExtensions.GetOperatingSystem();
        var mappings = config.Dependencies.Where(map => map.Os == platform).ToArray();
        if (mappings.Length == 0)
        {
            throw new InstallerException($"Embedded FNA.dll.config has no mappings for {platform}.");
        }

        config.Dependencies = mappings;
        return config;
    }

    [Serializable]
    public sealed class DllMap
    {
        [XmlAttribute("dll")] public string Dll { get; set; } = "";

        [XmlAttribute("os")] public OperatingSystem Os { get; set; }

        [XmlAttribute("target")] public string Target { get; set; } = "";
    }

    public enum OperatingSystem
    {
        [XmlEnum("windows")] Windows,
        [XmlEnum("osx")] Osx,
        [XmlEnum("linux")] Linux
    }
}

public static class FnaDllConfigExtensions
{
    public static FnaDllConfig.OperatingSystem GetOperatingSystem()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return FnaDllConfig.OperatingSystem.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return FnaDllConfig.OperatingSystem.Linux;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return FnaDllConfig.OperatingSystem.Osx;
        throw new PlatformNotSupportedException("What, is using anything but BSD against your religion?");
    }

    public static string GetLibrariesDirectory(this FnaDllConfig.OperatingSystem platform, string dir)
    {
        return platform switch
        {
            FnaDllConfig.OperatingSystem.Windows => dir,
            FnaDllConfig.OperatingSystem.Linux => Path.Combine(dir, "lib64"),
            FnaDllConfig.OperatingSystem.Osx => Path.Combine(dir, "osx"),
            _ => throw new PlatformNotSupportedException()
        };
    }
}
