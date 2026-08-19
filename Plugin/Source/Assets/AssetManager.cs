using Common;
using FezEngine.Effects.Structures;
using FezEngine.Services;
using FezEngine.Structure;
using FezEngine.Tools;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using System.Reflection;
using MonoMod.RuntimeDetour;

namespace HatModLoader.Source.Assets
{
    public class AssetManager : IDisposable
    {
        private readonly Hat _hat;

        private readonly FieldInfo _cachedAssetsField;
        private readonly FieldInfo _musicCacheField;
        private readonly FieldInfo _readLockField;
        private readonly FieldInfo _commonField;
        private readonly FieldInfo _referencesField;
        private readonly FieldInfo _assetField;
        
        private Hook _cmProviderCtorDetour;
        private Hook _smInitializeLibraryDetour;

        private readonly Dictionary<string, byte[]> _originalAssets = new();
        private readonly Dictionary<string, byte[]> _originalMusic = new();

        public AssetManager(Hat hat)
        {
            _hat = hat;

            _cachedAssetsField = typeof(MemoryContentManager)
                .GetField("cachedAssets", BindingFlags.NonPublic | BindingFlags.Static);
            _musicCacheField = typeof(SoundManager)
                .GetField("MusicCache", BindingFlags.NonPublic | BindingFlags.Instance);
            _readLockField = typeof(MemoryContentManager)
                .GetField("ReadLock", BindingFlags.NonPublic | BindingFlags.Static);
            _commonField = typeof(SharedContentManager)
                .GetField("Common", BindingFlags.NonPublic | BindingFlags.Static);
            _referencesField = _commonField!.FieldType
                .GetField("references", BindingFlags.NonPublic | BindingFlags.Instance);
            var referencedAssetType = _commonField.FieldType
                .GetNestedType("ReferencedAsset", BindingFlags.NonPublic);
            _assetField = referencedAssetType!
                .GetField("Asset", BindingFlags.Public | BindingFlags.Instance);
        }

        public void InitializeHooks()
        {
            _cmProviderCtorDetour = new Hook(
                typeof(ContentManagerProvider).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null,
                    CallingConventions.HasThis, new Type[] { typeof(Game) }, null),
                new Action<Action<ContentManagerProvider, Game>, ContentManagerProvider, Game>((orig, self, game) =>
                {
                    orig(self, game);
                    InitialInjectAllAssets(self);
                })
            );

            _smInitializeLibraryDetour = new Hook(
                typeof(SoundManager).GetMethod("InitializeLibrary"),
                new Action<Action<SoundManager>, SoundManager>((orig, self) =>
                {
                    orig(self);
                    InitialInjectAllMusic(self);
                })
            );
        }

        private IEnumerable<Asset> GetOrderedAssets()
        {
            var assets = new List<Asset>();
            foreach (var mod in _hat.Mods)
            {
                assets.AddRange(mod.GetAssets());
            }
            return assets;
        }

        private void InitialInjectAllAssets(ContentManagerProvider cmProvider)
        {
            var cachedAssets = (Dictionary<string, byte[]>)_cachedAssetsField.GetValue(null);

            foreach (var asset in GetOrderedAssets())
            {
                if (!asset.IsMusicFile && !TextResourceManager.IsTextResource(asset))
                {
                    cachedAssets[asset.AssetPath] = asset.Data;
                }
            }

            Logger.Log("HAT", "Asset injection completed!");
        }

        private void InitialInjectAllMusic(SoundManager soundManager)
        {
            var musicCache = (Dictionary<string, byte[]>)_musicCacheField.GetValue(soundManager);

            foreach (var asset in GetOrderedAssets())
            {
                if (!asset.IsMusicFile) continue;
                musicCache[asset.AssetPath] = asset.Data;
            }

            Logger.Log("HAT", "Music injection completed!");
        }

        public void InjectAsset(Asset asset)
        {
            if (asset.IsMusicFile)
            {
                var soundManager = (SoundManager)ServiceHelper.Get<ISoundManager>();
                var musicCache = (Dictionary<string, byte[]>)_musicCacheField.GetValue(soundManager);
                if (!_originalMusic.ContainsKey(asset.AssetPath) &&
                    musicCache.TryGetValue(asset.AssetPath, out var original))
                {
                    _originalMusic[asset.AssetPath] = original;
                }

                musicCache[asset.AssetPath] = asset.Data;
            }
            else
            {
                var cachedAssets = (Dictionary<string, byte[]>)_cachedAssetsField.GetValue(null);
                var readLock = _readLockField.GetValue(null);
                lock (readLock)
                {
                    if (!_originalAssets.ContainsKey(asset.AssetPath) &&
                        cachedAssets.TryGetValue(asset.AssetPath, out var original))
                    {
                        _originalAssets[asset.AssetPath] = original;
                    }

                    cachedAssets[asset.AssetPath] = asset.Data;
                }
            }
        }

        public void RemoveAsset(Asset asset)
        {
            if (asset.IsMusicFile)
            {
                var soundManager = (SoundManager)ServiceHelper.Get<ISoundManager>();
                var musicCache = (Dictionary<string, byte[]>)_musicCacheField.GetValue(soundManager);
                if (_originalMusic.TryGetValue(asset.AssetPath, out var original))
                {
                    musicCache[asset.AssetPath] = original;
                }
                else
                {
                    musicCache.Remove(asset.AssetPath);
                }
            }
            else
            {
                var cachedAssets = (Dictionary<string, byte[]>)_cachedAssetsField.GetValue(null);
                var readLock = _readLockField.GetValue(null);
                lock (readLock)
                {
                    if (_originalAssets.TryGetValue(asset.AssetPath, out var original))
                    {
                        cachedAssets[asset.AssetPath] = original;
                    }
                    else
                    {
                        cachedAssets.Remove(asset.AssetPath);
                    }
                }
            }
        }

        public void EvictFromCommon(string assetPath)
        {
            var common = _commonField.GetValue(null);
            var references = (System.Collections.IDictionary)_referencesField.GetValue(common);
            lock (common)
            {
                var key = FindReferencesKey(references, assetPath);
                if (key == null)
                {
                    return;
                }

                var asset = _assetField.GetValue(references[key]);
                if (asset is Texture texture)
                {
                    texture.Unhook();
                }

                if (asset is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                references.Remove(key);
            }
        }

        public void PatchInCommon(string assetPath)
        {
            var common = _commonField.GetValue(null);
            var references = (System.Collections.IDictionary)_referencesField.GetValue(common);
            lock (common)
            {
                var key = FindReferencesKey(references, assetPath);
                if (key == null)
                {
                    return;
                }

                var entry = references[key];
                var existing = _assetField.GetValue(entry);

                var mcm = new MemoryContentManager(ServiceHelper.Game.Services,
                    ServiceHelper.Game.Content.RootDirectory);

                switch (existing)
                {
                    case AnimatedTexture existingAt:
                    {
                        var tempAt = mcm.Load<AnimatedTexture>(assetPath);
                        var inPlace = PatchTexture2D(existingAt.Texture, tempAt.Texture, out var replacement);
                        if (!inPlace)
                        {
                            existingAt.Texture.Unhook();
                            existingAt.Texture.Dispose();
                            existingAt.Texture = replacement;
                        }
                        else
                        {
                            tempAt.Texture.Dispose();
                        }

                        existingAt.Offsets = tempAt.Offsets;
                        existingAt.FrameWidth = tempAt.FrameWidth;
                        existingAt.FrameHeight = tempAt.FrameHeight;
                        existingAt.Timing = tempAt.Timing;
                        existingAt.PotOffset = tempAt.PotOffset;
                        return;
                    }

                    case Texture2D existingTex:
                    {
                        var tempTex = mcm.Load<Texture2D>(assetPath);
                        var inPlace = PatchTexture2D(existingTex, tempTex, out var replacement);
                        if (!inPlace)
                        {
                            existingTex.Unhook();
                            existingTex.Dispose();
                            _assetField.SetValue(entry, replacement);
                        }
                        else
                        {
                            tempTex.Dispose();
                        }

                        return;
                    }

                    case SoundEffect existingSfx:
                    {
                        var tempSfx = mcm.Load<SoundEffect>(assetPath);
                        existingSfx.Dispose();
                        _assetField.SetValue(entry, tempSfx);
                        return;
                    }

                    default:
                    {
                        if (existing is Texture texture)
                        {
                            texture.Unhook();
                        }

                        if (existing is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }

                        references.Remove(key);
                        return;
                    }
                }
            }
        }

        private static string FindReferencesKey(System.Collections.IDictionary references, string assetPath)
        {
            foreach (System.Collections.DictionaryEntry kv in references)
            {
                if (string.Equals((string)kv.Key, assetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return (string)kv.Key;
                }
            }

            return null;
        }

        private static bool PatchTexture2D(Texture2D existing, Texture2D temp, out Texture2D replacement)
        {
            replacement = temp;
            if (existing.Width != temp.Width || existing.Height != temp.Height ||
                existing.Format != temp.Format || existing.LevelCount != temp.LevelCount)
            {
                return false;
            }

            const int bytesPerPixel = 4; // All FEZ textures are SurfaceFormat.Color
            for (var level = 0; level < existing.LevelCount; level++)
            {
                var w = Math.Max(existing.Width >> level, 1);
                var h = Math.Max(existing.Height >> level, 1);
                var size = w * h * bytesPerPixel;
                var buf = new byte[size];
                temp.GetData(level, null, buf, 0, size);
                existing.SetData(level, null, buf, 0, size);
            }

            return true;
        }

        public void Dispose()
        {
            _cmProviderCtorDetour?.Dispose();
            _smInitializeLibraryDetour?.Dispose();
        }
    }
}
