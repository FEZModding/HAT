using Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace HatModLoader.Helpers
{
    public static class ConfigHelper
    {
        public const string HatConfigFileName = "HatConfig.xml";

        [Serializable]
        public struct ModConfig
        {
            [XmlAttribute] public string Name;
            [XmlAttribute] public string Version;
            public bool? Disabled;
        }

        [Serializable]
        public struct HatConfig
        {
            public List<ModConfig> Mods;
        }

        public static HatConfig Config { get; private set; }

        public static ModConfig GetModConfig(string Name, string Version)
        {
            foreach (ModConfig modConfig in Config.Mods)
            {
                if (modConfig.Name == Name && modConfig.Version == Version)
                    return modConfig;
            }
            ModConfig newModConfig = new ModConfig();
            newModConfig.Name = Name;
            newModConfig.Version = Version;
            newModConfig.Disabled = false;
            Config.Mods.Add(newModConfig);
            return newModConfig;
        }

        public static void SetModConfig(ModConfig newConfig)
        {
            for (int i = 0; i < Config.Mods.Count; i++)
            {
                if (Config.Mods[i].Name == newConfig.Name && Config.Mods[i].Version == newConfig.Version)
                {
                    Config.Mods[i] = newConfig;
                    return;
                }
            }
            Config.Mods.Add(newConfig);
        }

        public static void LoadHatConfig()
        {
            HatConfig NewConfig;
            if (!File.Exists(GetHatConfigFilePath()))
            {
                Logger.Log("HAT", "Config file doesn't exist, loading blank configuration");
                NewConfig = new HatConfig();
            }
            else
            {
                try
                {
                    using (StreamReader reader = new StreamReader(GetHatConfigFilePath()))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(HatConfig));
                        NewConfig = (HatConfig)serializer.Deserialize(reader);
                    }
                }
                catch (Exception e)
                {
                    Logger.Log("HAT", $"Exception deserializing config file, {e.Message}");
                    NewConfig = new HatConfig();
                }
            }

            // Cleanup configuration and fill in missing details
            if (NewConfig.Mods == null)
                NewConfig.Mods = new List<ModConfig>();
            for (int i = 0; i < NewConfig.Mods.Count; i++)
            {
                ModConfig mod = NewConfig.Mods[i];
                if (!mod.Disabled.HasValue)
                    mod.Disabled = false;
                NewConfig.Mods[i] = mod;
            }

            Config = NewConfig;
        }

        public static bool SaveHatConfig()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(GetHatConfigFilePath()))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(HatConfig));
                    serializer.Serialize(writer, Config);
                }
            }
            catch (Exception e)
            {
                Logger.Log("HAT", $"FAILED to save HAT config, ${e.Message}");
                return false;
            }
            return true;
        }

        public static string GetHatConfigFilePath()
        {
            return Path.Combine(Util.LocalConfigFolder, HatConfigFileName);
        }
    }
}
