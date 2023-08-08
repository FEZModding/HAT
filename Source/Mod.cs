using Common;
using FezEngine.Tools;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;

namespace HatModLoader.Source
{
    public class Mod : IDisposable
    {
        public static readonly string ModsDirectoryName = "Mods";

        public static readonly string AssetsDirectoryName = "Assets";
        public static readonly string ModMetadataFileName = "Metadata.xml";

        [Serializable]
        public struct DependencyInfo
        {
            [XmlAttribute] public string Name;
            [XmlAttribute] public string MinimumVersion;
        }

        [Serializable]
        public struct Metadata
        {
            public string Name;
            public string Description;
            public string Author;
            public string Version;
            public string LibraryName;
            public DependencyInfo[] Dependencies;
        }

        public enum DependencyStatus
        {
            None,
            Valid,
            InvalidVersion,
            InvalidNotFound,
            InvalidRecursive,
            InvalidDependencyTree
        }

        public struct Dependency
        {
            public DependencyInfo Info;
            public Mod Instance;
            public DependencyStatus Status;
            public bool IsModLoaderDependency => Info.Name == "HAT";
            public string DetectedVersion => IsModLoaderDependency ? Hat.Version : (Instance != null ? Instance.Info.Version : null);

            public void SetStatus(DependencyStatus status)
            {
                this.Status = status;
            }
        }

        public Hat ModLoader;

        public byte[] RawAssembly { get; private set; }
        public Assembly Assembly { get; private set; }
        public Metadata Info { get; private set; }
        public string DirectoryName { get; private set; }
        public List<Dependency> Dependencies { get; private set; }
        public bool IsZip { get; private set; }
        public List<Asset> Assets { get; private set; }
        public List<IGameComponent> Components { get; private set; }

        public bool IsAssetMod => Assets.Count > 0;
        public bool IsCodeMod => RawAssembly != null;

        public Mod(Hat modLoader)
        {
            ModLoader = modLoader;

            RawAssembly = null;
            Assembly = null;
            Assets = new List<Asset>();
            Components = new List<IGameComponent>();
            Dependencies = new List<Dependency>();
        }
        
        public void InitializeComponents()
        {
            if (RawAssembly == null || Assembly == null) return;

            foreach (Type type in Assembly.GetExportedTypes())
            {
                if (!typeof(IGameComponent).IsAssignableFrom(type) || !type.IsPublic) continue;
                var gameComponent = (IGameComponent)Activator.CreateInstance(type, new object[] { ModLoader.Game });
                Components.Add(gameComponent);
            }
        }

        // injects custom components of this mod into the game
        public void InjectComponents()
        {
            // add game components
            foreach (var component in Components)
            {
                ServiceHelper.AddComponent(component);
                // initialization is done by just adding component if they're added past game init
                //component.Initialize();
            }
        }

        // loads mod's assemblies
        public void InitializeAssembly()
        {
            if (RawAssembly == null) return;
            Assembly = Assembly.Load(RawAssembly);
        }

        public void Dispose()
        {
            // TODO: dispose assets

            // remove mod's components
            foreach(var component in Components)
            {
                ServiceHelper.RemoveComponent(component);
            }
        }

        private bool TryLoadMetadata(StreamReader reader)
        {
            try
            {
                var serializer = new XmlSerializer(typeof(Metadata));
                Info = (Metadata)serializer.Deserialize(reader);
                if (Info.Name == null || Info.Name.Length == 0) return false;
                if (Info.Version == null || Info.Version.Length == 0) return false; 
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // compare two version strings
        // returns positive number if first version is newer, negative if first version is older
        public static int CompareVersions(string ver1, string ver2)
        {
            string tokensPattern = @"(\d+|\D+)";
            string[] TokensVer1 = Regex.Split(ver1, tokensPattern);
            string[] TokensVer2 = Regex.Split(ver2, tokensPattern);

            for(int i=0; i < Math.Min(TokensVer1.Length, TokensVer2.Length); i++)
            {
                if(int.TryParse(TokensVer1[i], out int tokenInt1) && int.TryParse(TokensVer2[i], out int tokenInt2))
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

        public int CompareVersionsWith(Mod mod)
        {
            return CompareVersions(Info.Version, mod.Info.Version);
        }

        public void InitializeDependencies()
        {
            if (Info.Dependencies == null || Info.Dependencies.Count() == 0) return;
            if (Dependencies.Count() == Info.Dependencies.Length) return;

            Dependencies.Clear();
            foreach (var dependencyInfo in Info.Dependencies)
            {
                var matchingMod = ModLoader.Mods.FirstOrDefault(mod => mod.Info.Name == dependencyInfo.Name);
                var dependency = new Dependency
                {
                    Info = dependencyInfo,
                    Instance = matchingMod
                };

                // verify dependency
                if (dependency.IsModLoaderDependency || dependency.Instance != null)
                {
                    if (CompareVersions(dependency.DetectedVersion, dependency.Info.MinimumVersion) < 0)
                    {
                        dependency.Status = DependencyStatus.InvalidVersion;
                    }
                    else
                    {
                        dependency.Status = DependencyStatus.Valid;
                    }
                }

                if (!dependency.IsModLoaderDependency)
                {
                    if (dependency.Instance == null)
                    {
                        dependency.Status = DependencyStatus.InvalidNotFound;
                    }
                    else if (dependency.Instance.AreDependenciesValid())
                    {
                        dependency.Status = DependencyStatus.Valid;
                    }

                    if (IsDependencyRecursive(ref dependency))
                    {
                        dependency.Status = DependencyStatus.InvalidRecursive;
                    }
                }

                Dependencies.Add(dependency);
            }
        }

        private bool IsDependencyRecursive(ref Dependency dependency)
        {
            var currentModQueue = new List<Mod>() { dependency.Instance };

            var iterationsCount = ModLoader.Mods.Count();
            
            while(currentModQueue.Count > 0)
            {
                var newDependencyMods = currentModQueue.SelectMany(mod => mod.Dependencies).Select(dep => dep.Instance).ToList();
                if (newDependencyMods.Contains(this))
                {
                    return true;
                }

                currentModQueue = newDependencyMods;

                iterationsCount--;

                if(iterationsCount <= 0) {
                    break;
                }
            }

            return false;
        }

        public bool TryFinalizeDependencies()
        {
            foreach(var dependency in Dependencies)
            {
                if (dependency.IsModLoaderDependency) continue;

                if (!dependency.Instance.AreDependenciesFinalized())
                {
                    return false;
                }

                dependency.SetStatus(
                    dependency.Instance.AreDependenciesValid()
                    ? DependencyStatus.Valid
                    : DependencyStatus.InvalidDependencyTree
                );
            }

            return true;
        }

        public bool AreDependenciesFinalized()
        {
            return Dependencies.All(dependency => dependency.Status != DependencyStatus.None);
        }

        public bool AreDependenciesValid()
        {
            if (Info.Dependencies == null) return true; // if mod has no dependencies, they are "valid"
            if (Info.Dependencies.Count() != Dependencies.Count()) return false;

            return Dependencies.All(dependency => dependency.Status == DependencyStatus.Valid);
        }

        // attempts to load a valid mod directory within Mods directory
        public static bool TryLoadFromDirectory(Hat modLoader, string directoryName, out Mod mod)
        {
            mod = new Mod(modLoader);
            mod.DirectoryName = directoryName;

            var modDir = Path.Combine(GetModsDirectory(), directoryName);
            if (!Directory.Exists(modDir)) return false;

            foreach (var path in Directory.EnumerateFiles(modDir))
            {
                var relativeFileName = Path.GetFileName(path);

                if (relativeFileName.Equals(ModMetadataFileName, StringComparison.OrdinalIgnoreCase))
                {
                    using (var reader = new StreamReader(path))
                    {
                        if (!mod.TryLoadMetadata(reader)) return false;
                    }
                    break;
                }
            }

            foreach (var path in Directory.EnumerateDirectories(modDir))
            {
                var relativeDirName = new DirectoryInfo(path).Name;
                if (relativeDirName.Equals(AssetsDirectoryName, StringComparison.OrdinalIgnoreCase))
                {
                    mod.Assets = Asset.LoadDirectory(path);
                    break;
                }
            }

            var libraryName = mod.Info.LibraryName;
            if (libraryName != null && libraryName.Length > 0 && libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                var libraryPath = Path.Combine(modDir, libraryName);

                if (File.Exists(libraryPath))
                {
                    mod.RawAssembly = File.ReadAllBytes(libraryPath);
                }
            }

            return mod.IsAssetMod || mod.IsCodeMod;
        }

        // attempts to load a valid mod zip package within Mods directory
        public static bool TryLoadFromZip(Hat modLoader, string zipName, out Mod mod)
        {
            mod = new Mod(modLoader)
            {
                DirectoryName = zipName,
                IsZip = true
            };

            var zipPath = Path.Combine(GetModsDirectory(), zipName);
            if (!File.Exists(zipPath)) return false;

            using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Update))
            {
                foreach (var zipEntry in archive.Entries.Where(e => !e.FullName.Contains("/")))
                {
                    if (zipEntry.Name.Equals(ModMetadataFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        using (var reader = new StreamReader(zipEntry.Open()))
                        {
                            if (!mod.TryLoadMetadata(reader)) return false;
                        }
                        break;
                    }
                }

                foreach (var zipEntry in archive.Entries)
                {
                    if (zipEntry.FullName.StartsWith(AssetsDirectoryName, StringComparison.OrdinalIgnoreCase))
                    {
                        mod.Assets = Asset.LoadZip(archive, AssetsDirectoryName);
                        break;
                    }
                }

                var libraryName = mod.Info.LibraryName;
                if (libraryName != null && libraryName.Length > 0 && libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    var dllMatches = archive.Entries.Where(entry => entry.Name.Equals(libraryName, StringComparison.OrdinalIgnoreCase));

                    if (dllMatches.Count() > 0)
                    {
                        var zipFile = dllMatches.First().Open();
                        mod.RawAssembly = new byte[zipFile.Length];
                        zipFile.Read(mod.RawAssembly, 0, mod.RawAssembly.Length);
                    }
                }
            }

            return mod.IsAssetMod || mod.IsCodeMod;
        }

        public static string GetModsDirectory()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModsDirectoryName);
        }

        // returns list of directory names in mod directory
        public static List<string> GetModDirectories()
        {
            if (!Directory.Exists(GetModsDirectory())) return new List<string>();
            return Directory.GetDirectories(ModsDirectoryName)
                .Select(path => new DirectoryInfo(path).Name)
                .ToList();
        }

        public static List<string> GetModArchives()
        {
            if (!Directory.Exists(GetModsDirectory())) return new List<string>();
            return Directory.GetFiles(ModsDirectoryName)
                .Where(file => Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.GetFileName(file))
                .ToList();
        }
    }
}
