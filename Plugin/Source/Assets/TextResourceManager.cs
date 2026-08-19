using Common;
using FezEngine.Tools;
using Microsoft.Xna.Framework.Content;

namespace HatModLoader.Source.Assets
{
    internal enum TextLookupScope
    {
        Localized,
        FallbackOnly
    }

    internal class TextResourceManager
    {
        private const string AssetPath = "Resources\\ModText";

        private readonly Hat _hat;

        private Dictionary<string, Dictionary<string, string>> _resources = new();

        private bool _loaded;

        public TextResourceManager(Hat hat)
        {
            _hat = hat;
        }

        public static bool IsTextResource(Asset asset)
        {
            return string.Equals(asset.AssetPath, AssetPath, StringComparison.OrdinalIgnoreCase);
        }

        public bool TryGetString(string tag, TextLookupScope scope, out string text)
        {
            if (!_loaded)
            {
                Reload();
            }

            if (tag == null)
            {
                text = null;
                return false;
            }

            if (scope != TextLookupScope.FallbackOnly &&
                _resources.TryGetValue(Culture.TwoLetterISOLanguageName, out var localized) &&
                localized.TryGetValue(tag, out text))
            {
                return true;
            }

            if (_resources.TryGetValue(string.Empty, out var fallback) && fallback.TryGetValue(tag, out text))
            {
                return true;
            }

            text = null;
            return false;
        }

        public void Reload()
        {
            _resources = new Dictionary<string, Dictionary<string, string>>();

            foreach (var mod in _hat.Mods)
            {
                var asset = mod.GetAssets().FirstOrDefault(IsTextResource);
                if (asset == null)
                {
                    continue;
                }

                try
                {
                    using var content = new TextResourceContentManager(
                        ServiceHelper.Game.Services, ServiceHelper.Game.Content.RootDirectory, asset.Data);
                    var resource = content.Load<Dictionary<string, Dictionary<string, string>>>(AssetPath);

                    foreach (var language in resource)
                    {
                        if (!_resources.TryGetValue(language.Key, out var mergedLanguage))
                        {
                            mergedLanguage = new Dictionary<string, string>();
                            _resources.Add(language.Key, mergedLanguage);
                        }

                        foreach (var entry in language.Value)
                        {
                            // Later mods override earlier resources in the resolved load order.
                            mergedLanguage[entry.Key] = entry.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("HAT", LogSeverity.Warning,
                        $"Could not load ModText from '{mod.Metadata.Name}': {ex.Message}");
                }
            }

            _loaded = true;
        }

        private class TextResourceContentManager : ContentManager
        {
            private readonly byte[] _data;

            public TextResourceContentManager(IServiceProvider serviceProvider, string rootDirectory, byte[] data)
                : base(serviceProvider, rootDirectory)
            {
                _data = data;
            }

            protected override Stream OpenStream(string assetName)
            {
                return new MemoryStream(_data, false);
            }
        }
    }
}