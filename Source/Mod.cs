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
        public struct Metadata
        {
            public string Name;
            public string Description;
            public string Author;
            public string Version;
            public string LibraryName;
        }

        public byte[] RawAssembly { get; private set; }
        public Assembly Assembly { get; private set; }
        public Metadata Info { get; private set; }
        public string DirectoryName { get; private set; }
        public bool IsZip { get; private set; }
        public Dictionary<string, byte[]> Assets { get; private set; }
        public List<IGameComponent> Components { get; private set; }

        public bool IsAssetMod => Assets.Count > 0;
        public bool IsCodeMod => RawAssembly != null;

        public Mod()
        {
            RawAssembly = null;
            Assembly = null;
            Assets = new Dictionary<string, byte[]>();
            Components = new List<IGameComponent>();
        }

        // inject custom assets of this mod into the game
        public void InitializeAssets()
        {
            // override custom assets
            foreach (var asset in Assets)
            {
                AssetsHelper.InjectAsset(asset.Key, asset.Value);
            }
        }

        // injects custom components of this mod into the game
        public void InitializeComponents()
        {
            // add game components
            foreach (var component in Components)
            {
                ServiceHelper.AddComponent(component);
                component.Initialize();
            }
        }

        public void InitializeAssembly()
        {
            if (RawAssembly == null) return;
            Assembly = Assembly.Load(RawAssembly);

            foreach (Type type in Assembly.GetExportedTypes())
            {
                if (!typeof(IGameComponent).IsAssignableFrom(type) || !type.IsPublic) continue;
                var gameComponent = (IGameComponent)Activator.CreateInstance(type, new object[] { Hat.Instance.Game });
                Components.Add(gameComponent);
            }
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

        // compare versions of two mods
        // returns positive number if passed mod is older, negative if passed mod is newer
        public int CompareVersions(Mod mod)
        {
            string tokensPattern = @"(\d+|\D+)";
            string[] TokensOwn = Regex.Split(Info.Version, tokensPattern);
            string[] TokensMod = Regex.Split(mod.Info.Version, tokensPattern);

            for(int i=0; i < Math.Min(TokensOwn.Length, TokensMod.Length); i++)
            {
                if(int.TryParse(TokensOwn[i], out int tokenIntOwn) && int.TryParse(TokensMod[i], out int tokenIntMod))
                {
                    if (tokenIntOwn > tokenIntMod) return 1;
                    if (tokenIntOwn < tokenIntMod) return -1;
                    continue;
                }
                int comparison = TokensOwn[i].CompareTo(TokensMod[i]);
                if (comparison < 0) return 1;
                if (comparison > 0) return -1;
            }
            if (TokensOwn.Length > TokensMod.Length) return 1;
            if (TokensOwn.Length < TokensMod.Length) return -1;
            return 0;
        }

        // attempts to load a valid mod directory within Mods directory
        public static bool TryLoadFromDirectory(string directoryName, out Mod mod)
        {
            mod = new Mod();
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
                    mod.Assets = AssetsHelper.LoadDirectory(path);
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
        public static bool TryLoadFromZip(string zipName, out Mod mod)
        {
            mod = new Mod()
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
                        mod.Assets = AssetsHelper.LoadZip(archive, AssetsDirectoryName);
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
