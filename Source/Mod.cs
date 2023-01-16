using Common;
using FezEngine.Tools;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
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

        public Assembly Assembly { get; private set; }
        public Metadata Info { get; private set; }
        public string DirectoryName { get; private set; }
        public bool IsZip { get; private set; }
        public Dictionary<string, byte[]> Assets { get; private set; }
        public List<IGameComponent> Components { get; private set; }

        public bool IsAssetMod => Assets.Count > 0;
        public bool IsCodeMod => Assembly != null;

        public Mod()
        {
            Assembly = null;
            Assets = new Dictionary<string, byte[]>();
            Components = new List<IGameComponent>();
        }

        // injects custom components and assets of this mod into the game
        public void Initialize()
        {
            // override custom assets
            foreach(var asset in Assets)
            {
                AssetsHelper.InjectAsset(asset.Key, asset.Value);
            }

            // add game components
            foreach(var component in Components)
            {
                ServiceHelper.AddComponent(component);
                component.Initialize();
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
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void SetAssembly(Assembly assembly)
        {
            if (Assembly != null) return;
            Assembly = assembly;

            foreach (Type type in Assembly.GetExportedTypes())
            {
                if (!typeof(IGameComponent).IsAssignableFrom(type) || !type.IsPublic) continue;
                var gameComponent = (IGameComponent)Activator.CreateInstance(type, new object[] { Hat.Instance.Game });
                Components.Add(gameComponent);
            }
        }

        // attempts to load a valid mod directory within Mods directory
        public static bool TryLoadFromDirectory(string directoryName, out Mod mod)
        {
            mod = new Mod();
            mod.DirectoryName = directoryName;

            var modDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModsDirectoryName, directoryName);
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

            if (mod.Info.Name == null) return false;

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
                    mod.SetAssembly(Assembly.LoadFile(libraryPath));
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

            var zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModsDirectoryName, zipName);
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

                if (mod.Info.Name == null) return false;

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
                        byte[] bytes = new byte[zipFile.Length];
                        zipFile.Read(bytes, 0, bytes.Length);
                        mod.SetAssembly(Assembly.Load(bytes));
                    }
                }
            }

            return mod.IsAssetMod || mod.IsCodeMod;
        }

        // returns list of directory names in mod directory
        public static List<string> GetModDirectories()
        {
            if (!Directory.Exists(ModsDirectoryName)) return new List<string>();
            return Directory.GetDirectories(ModsDirectoryName)
                .Select(path => new DirectoryInfo(path).Name)
                .ToList();
        }

        public static List<string> GetModArchives()
        {
            if (!Directory.Exists(ModsDirectoryName)) return new List<string>();
            return Directory.GetFiles(ModsDirectoryName)
                .Where(file => Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.GetFileName(file))
                .ToList();
        }
    }
}
