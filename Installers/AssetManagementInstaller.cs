using Common;
using FezEngine.Services;
using FezEngine.Tools;
using HatModLoader.Source;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using System.Reflection;
using FezEngine.Structure;

using GetCleanPath_orig = On.FezEngine.Tools.SharedContentManager.orig_GetCleanPath;
using OpenStream_orig = On.FezEngine.Tools.MemoryContentManager.orig_OpenStream;
using GetCue_orig = On.FezEngine.Services.SoundManager.orig_GetCue;
using System.Collections;

namespace HatModLoader.Installers
{
    internal class AssetManagementInstaller : IHatInstaller
    {
        public void Install()
        {
            On.FezEngine.Tools.SharedContentManager.GetCleanPath += OnGetCleanPath;
            On.FezEngine.Tools.MemoryContentManager.OpenStream += OnOpenStream;
            On.FezEngine.Services.SoundManager.GetCue += OnGetCue;

        }
        public void Uninstall()
        {
            On.FezEngine.Tools.SharedContentManager.GetCleanPath -= OnGetCleanPath;
            On.FezEngine.Tools.MemoryContentManager.OpenStream -= OnOpenStream;
            On.FezEngine.Services.SoundManager.GetCue -= OnGetCue;
        }

        private string OnGetCleanPath(GetCleanPath_orig orig, string path)
        {
            ClearCommonContentManagerReferences();
            return orig(path);
        }

        private Stream OnOpenStream(OpenStream_orig orig, MemoryContentManager self, string assetName)
        {
            var asset = TryGetModdedAsset(assetName);

            if (asset != null)
            {
                return new MemoryStream(asset.Data, 0, asset.Data.Length);
            }
            else
            {
                return orig(self, assetName);
            }
        }

        private OggStream OnGetCue(GetCue_orig orig, SoundManager self, string name, bool asyncPrecache)
        {
            var musicCacheField = typeof(SoundManager).GetField("MusicCache", BindingFlags.NonPublic | BindingFlags.Instance);
            var musicCache = musicCacheField.GetValue(self) as Dictionary<string, byte[]>;

            bool hadOldCache = musicCache.TryGetValue(name, out var oldCache);

            var asset = TryGetModdedAsset(name);
            if(asset != null && asset.IsMusicFile)
            {
                musicCache[name] = asset.Data;
            }

            var stream = orig(self, name, asyncPrecache);

            if (hadOldCache)
            {
                musicCache[name] = oldCache;
            }

            return stream;
        }


        private void ClearCommonContentManagerReferences()
        {
            var CommonField = typeof(SharedContentManager).GetField("Common", BindingFlags.NonPublic | BindingFlags.Static);
            var Common = CommonField.GetValue(null);
            var referencesField = Common.GetType().GetField("references", BindingFlags.Instance | BindingFlags.NonPublic);
            var references = referencesField.GetValue(Common) as IDictionary;

            references?.Clear();
        }

        private Asset TryGetModdedAsset(string assetName)
        {
            var assetProviders = Hat.Instance.GetAllAssetProviders();

            foreach(var provider in assetProviders)
            {
                var asset = provider.LoadAsset(assetName);
                if(asset != null)
                {
                    return asset;
                }
            }

            return null;
        }
    }
}
