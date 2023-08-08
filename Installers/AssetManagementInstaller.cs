using Common;
using FezEngine.Services;
using FezEngine.Tools;
using HatModLoader.Source;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace HatModLoader.Installers
{
    internal class AssetManagementInstaller : IHatInstaller
    {
        public static IDetour CMProviderCtorDetour;
        public static IDetour SMInitializeLibraryDetour;

        public void Install()
        {
            CMProviderCtorDetour = new Hook(
                typeof(ContentManagerProvider).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null,
                CallingConventions.HasThis, new Type[] { typeof(Game) }, null),
                new Action<Action<ContentManagerProvider, Game>, ContentManagerProvider, Game>((orig, self, game) => {
                    orig(self, game);
                    InjectAssets(self);
                })
            );

            SMInitializeLibraryDetour = new Hook(
                typeof(SoundManager).GetMethod("InitializeLibrary"),
                new Action<Action<SoundManager>, SoundManager>((orig, self) => {
                    orig(self);
                    InjectMusic(self);
                })
            );
        }
        public void Uninstall()
        {
            CMProviderCtorDetour.Dispose();
            SMInitializeLibraryDetour.Dispose();
        }

        private static void InjectAssets(ContentManagerProvider CMProvider)
        {
            var cachedAssetsField = typeof(MemoryContentManager).GetField("cachedAssets", BindingFlags.NonPublic | BindingFlags.Static);
            var cachedAssets = cachedAssetsField.GetValue(null) as Dictionary<string, byte[]>;

            foreach(var asset in Hat.Instance.GetFullAssetList())
            {
                if (asset.IsMusicFile) continue;
                cachedAssets[asset.AssetPath] = asset.Data;
            }

            Logger.Log("HAT", "Asset injection completed!");
        }

        private static void InjectMusic(SoundManager soundManager)
        {
            var musicCacheField = typeof(SoundManager).GetField("MusicCache", BindingFlags.NonPublic | BindingFlags.Instance);
            var musicCache = musicCacheField.GetValue(soundManager) as Dictionary<string, byte[]>;

            foreach (var asset in Hat.Instance.GetFullAssetList())
            {
                if (!asset.IsMusicFile) continue;
                musicCache[asset.AssetPath] = asset.Data;
            }

            Logger.Log("HAT", "Music injection completed!");
        }

    }
}
