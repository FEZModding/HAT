using Common;
using FezEngine.Components;
using FezEngine.Tools;
using HatModLoader.Source.FileProxies;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.RuntimeDetour;
using System.Text.RegularExpressions;

namespace HatModLoader.Source.Languages
{
    internal class LanguagePackManager : IDisposable
    {
        private const string LanguagesDirectoryName = "Languages";

        private const string StaticTextAssetPath = "Resources\\StaticText";

        private const string GameTextAssetPath = "Resources\\GameText";

        private const string CreditsTextAssetPath = "Resources\\CreditsText";

        private static readonly string LanguagesDirectory =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LanguagesDirectoryName);

        private readonly Dictionary<Language, LanguagePack> _packs = new();

        private readonly Dictionary<string, Dictionary<string, string>> _textResources = new();

        private Language _loadedLanguage;

        private int _loadedRevision = -1;

        private Language _fontLanguage;

        private int _fontRevision = -1;

        private bool _wasUsingLanguagePack;

        private bool _loaded;

        private readonly Hook _cultureHook;

        private readonly Hook _fontUpdateHook;

        public LanguagePackManager()
        {
            var cultureGetter = typeof(Culture).GetProperty(nameof(Culture.TwoLetterISOLanguageName))?.GetMethod;
            _cultureHook = new Hook(cultureGetter, new Func<Func<string>, string>(GetLanguageCode));

            var updateMethod = typeof(FontManager).GetMethod(nameof(FontManager.Update), new[] { typeof(GameTime) });
            _fontUpdateHook = new Hook(updateMethod, new Action<Action<FontManager, GameTime>, FontManager, GameTime>(UpdateFonts));
        }

        public static void PersistSelectedLanguage()
        {
            var settings = SettingsManager.Settings;
            if (settings == null || (int)settings.Language <= (int)Language.Korean)
            {
                return;
            }

            var settingsPath = Path.Combine(Util.LocalConfigFolder, "Settings");
            if (!File.Exists(settingsPath))
            {
                return;
            }

            var contents = File.ReadAllText(settingsPath);
            const string nullLanguage = "language null";
            if (!contents.Contains(nullLanguage))
            {
                return;
            }

            var displayName = Hat.Instance?.LanguagePacks.GetDisplayName(settings.Language);
            if (displayName == null)
            {
                return;
            }

            // Display names are stable English identifiers that remain readable in settings.
            var serializedLanguage = $"language \"{displayName}\"";
            File.WriteAllText(settingsPath, contents.Replace(nullLanguage, serializedLanguage));
        }

        public static void RestorePersistedLanguage()
        {
            var settingsPath = Path.Combine(Util.LocalConfigFolder, "Settings");
            if (!File.Exists(settingsPath))
            {
                return;
            }

            // BUG: The serializer writes null when saving a custom language, even with hooks.
            //      Therefore, after saving, we use a regex to replace that value, bruh.
            var contents = File.ReadAllText(settingsPath);
            const string indentationGroup = "indent";
            const string nameGroup = "name";
            var languagePattern = new Regex(
                $"(?m)^(?<{indentationGroup}>[ \\t]*)language[ \\t]+\"(?<{nameGroup}>[^\"]*)\"[ \\t]*(?=\\r?$)");
            var match = languagePattern.Match(contents);
            if (!match.Success)
            {
                return;
            }

            var persistedName = match.Groups[nameGroup].Value;
            if (int.TryParse(persistedName, out _) ||
                Enum.TryParse(persistedName, out Language builtInLanguage) &&
                (int)builtInLanguage <= (int)Language.Korean)
            {
                return;
            }

            var language = FindPersistedLanguage(persistedName);
            var serializedValue = language.HasValue
                ? ((int)language.Value).ToString()
                : nameof(Language.English);
            var replacement = $"{match.Groups[indentationGroup].Value}language \"{serializedValue}\"";
            File.WriteAllText(settingsPath, languagePattern.Replace(contents, replacement, 1));
        }

        public void Load()
        {
            Directory.CreateDirectory(LanguagesDirectory);

            var proxies = DirectoryFileProxy.EnumerateInDirectory(LanguagesDirectory)
                .Cast<IFileProxy>()
                .Concat(ZipFileProxy.EnumerateInDirectory(LanguagesDirectory))
                .OrderBy(proxy => proxy.ContainerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(proxy => proxy.RootPath, StringComparer.Ordinal);

            foreach (var proxy in proxies)
            {
                if (!LanguagePack.TryLoad(proxy, out var pack))
                {
                    Logger.Log("HAT", LogSeverity.Warning,
                        $"Invalid language pack in '{proxy.ContainerName}'.");
                    proxy.Dispose();
                    continue;
                }

                var language = GetLanguage(pack.Metadata.Code);
                var duplicateCode = _packs.ContainsKey(language);
                var duplicateDisplayName = _packs.Values.Any(registeredPack =>
                    string.Equals(registeredPack.Metadata.DisplayName, pack.Metadata.DisplayName,
                        StringComparison.OrdinalIgnoreCase));
                if (duplicateCode || duplicateDisplayName)
                {
                    Logger.Log("HAT", LogSeverity.Warning,
                        $"Ignoring '{proxy.ContainerName}': language code or display name is already registered.");
                    pack.Dispose();
                    continue;
                }

                _packs.Add(language, pack);
                Logger.Log("HAT", $"Loaded language pack '{pack.Metadata.DisplayName}' ({pack.Metadata.Code})");
            }

            _loaded = true;
            if ((int)Culture.Language > (int)Language.Korean && !IsRegistered(Culture.Language))
            {
                Culture.Language = Language.English;
                SettingsManager.Settings.Language = Language.English;
                SettingsManager.Save();
                return;
            }

            PersistSelectedLanguage();
        }

        public bool IsRegistered(Language language)
        {
            return _packs.ContainsKey(language);
        }

        public bool HasActivePack => IsRegistered(Culture.Language);

        public string GetCode(Language language)
        {
            return _packs.TryGetValue(language, out var pack) ? pack.Metadata.Code : null;
        }

        public string GetDisplayName(Language language)
        {
            return _packs.TryGetValue(language, out var pack) ? pack.Metadata.DisplayName : null;
        }

        public bool TryGetDisplayName(string tag, out string displayName)
        {
            const string languageTagPrefix = "Language";
            if (tag == null ||
                !tag.StartsWith(languageTagPrefix, StringComparison.Ordinal) ||
                !int.TryParse(tag.Substring(languageTagPrefix.Length), out var value) || 
                !_packs.TryGetValue((Language)value, out var pack))
            {
                displayName = null;
                return false;
            }

            displayName = pack.Metadata.DisplayName;
            if (string.IsNullOrWhiteSpace(pack.Metadata.Locale))
            {
                return true;
            }

            if (TryGetText(StaticTextAssetPath, pack.Metadata.Locale, out var localizedName))
            {
                displayName = localizedName;
            }

            return true;
        }

        public Language ChangeLanguage(Language current, int direction)
        {
            var languages = Enumerable.Range((int)Language.English, (int)Language.Korean + 1)
                .Select(value => (Language)value)
                .Concat(_packs.Keys)
                .OrderBy(language => language)
                .ToList();
            var currentIndex = languages.IndexOf(current);
            if (currentIndex < 0)
            {
                return Language.English;
            }

            var nextIndex = (currentIndex + Math.Sign(direction) + languages.Count) % languages.Count;
            return languages[nextIndex];
        }

        public bool TryGetText(string assetPath, string tag, out string text)
        {
            text = null;
            var pack = GetActivePack();
            if (pack == null || tag == null)
            {
                return false;
            }

            EnsureTextLoaded(pack);
            return _textResources.TryGetValue(assetPath, out var resource) && resource.TryGetValue(tag, out text);
        }

        public void ApplyFonts(object fontManager)
        {
            var pack = GetActivePack();
            if (pack == null || (_fontLanguage == Culture.Language && _fontRevision == pack.Revision))
            {
                return;
            }

            try
            {
                var content = new PackContentManager(ServiceHelper.Game.Services,
                    ServiceHelper.Game.Content.RootDirectory, pack);
                var small = content.Load<SpriteFont>(pack.Metadata.SmallFont);
                var big = content.Load<SpriteFont>(pack.Metadata.BigFont);
                small.DefaultCharacter = ' ';
                big.DefaultCharacter = ' ';

                SetFontProperty(fontManager, "Small", small);
                SetFontProperty(fontManager, "Big", big);
                SetFontProperty(fontManager, "SmallFactor", 1.5f);
                SetFontProperty(fontManager, "BigFactor", 2f);
                SetFontProperty(fontManager, "SideSpacing", 8f);
                SetFontProperty(fontManager, "TopSpacing", 0f);
                _fontLanguage = Culture.Language;
                _fontRevision = pack.Revision;
            }
            catch (Exception ex)
            {
                Logger.Log("HAT", LogSeverity.Warning,
                    $"Could not load fonts from '{pack.ContainerName}': {ex.Message}");
            }
        }

        public void ReloadActivePack()
        {
            var pack = GetActivePack();
            if (pack == null || !pack.Reload())
            {
                return;
            }

            _loadedRevision = -1;
            _fontRevision = -1;
            Logger.Log("HAT", $"Reloaded language pack '{pack.Metadata.DisplayName}'.");
        }

        public void Dispose()
        {
            _cultureHook.Dispose();
            _fontUpdateHook.Dispose();
            foreach (var pack in _packs.Values)
            {
                pack.Dispose();
            }
        }

        private LanguagePack GetActivePack()
        {
            return _packs.TryGetValue(Culture.Language, out var pack) ? pack : null;
        }

        private void EnsureTextLoaded(LanguagePack pack)
        {
            if (_loadedLanguage == Culture.Language && _loadedRevision == pack.Revision)
            {
                return;
            }

            _textResources.Clear();
            LoadTextResource(pack, StaticTextAssetPath);
            LoadTextResource(pack, GameTextAssetPath);
            LoadTextResource(pack, CreditsTextAssetPath);
            _loadedLanguage = Culture.Language;
            _loadedRevision = pack.Revision;
        }

        private void LoadTextResource(LanguagePack pack, string assetPath)
        {
            if (!pack.TryGetAsset(assetPath, out _))
            {
                return;
            }

            using var content = new PackContentManager(ServiceHelper.Game.Services,
                ServiceHelper.Game.Content.RootDirectory, pack);
            var allResources = content.Load<Dictionary<string, Dictionary<string, string>>>(assetPath);
            if (allResources.TryGetValue(pack.Metadata.Code, out var localized))
            {
                _textResources[assetPath] = localized;
                return;
            }

            if (allResources.TryGetValue(string.Empty, out var fallback))
            {
                _textResources[assetPath] = fallback;
            }
        }

        private static Language GetLanguage(string code)
        {
            const int alphabetLength = 26;
            const int firstCustomLanguage = (int)Language.Korean + 1;
            return (Language)(firstCustomLanguage + ((code[0] - 'a') * alphabetLength) + (code[1] - 'a'));
        }

        private static Language? FindPersistedLanguage(string displayName)
        {
            if (!Directory.Exists(LanguagesDirectory))
            {
                return null;
            }

            var proxies = DirectoryFileProxy.EnumerateInDirectory(LanguagesDirectory)
                .Cast<IFileProxy>()
                .Concat(ZipFileProxy.EnumerateInDirectory(LanguagesDirectory))
                .OrderBy(proxy => proxy.ContainerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(proxy => proxy.RootPath, StringComparer.Ordinal);

            foreach (var proxy in proxies)
            {
                if (!LanguagePack.TryLoad(proxy, out var pack))
                {
                    proxy.Dispose();
                    continue;
                }

                using (pack)
                {
                    if (string.Equals(pack.Metadata.DisplayName, displayName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return GetLanguage(pack.Metadata.Code);
                    }
                }
            }

            return null;
        }

        private static void SetFontProperty(object fontManager, string propertyName, object value)
        {
            var property = fontManager.GetType().GetProperty(propertyName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            property?.SetValue(fontManager, value);
        }

        private static string GetLanguageCode(Func<string> orig)
        {
            var languagePacks = Hat.Instance?.LanguagePacks;
            var code = languagePacks?.GetCode(Culture.Language);
            if (code != null)
            {
                return code;
            }

            if ((int)Culture.Language > (int)Language.Korean)
            {
                if (languagePacks?._loaded != true)
                {
                    return "en";
                }

                // A removed pack must not leave FEZ with an invalid culture value.
                Culture.Language = Language.English;
                return "en";
            }

            return orig();
        }

        private static void UpdateFonts(Action<FontManager, GameTime> orig, FontManager fontManager,
            GameTime gameTime)
        {
            orig(fontManager, gameTime);
            Hat.Instance?.LanguagePacks.UpdateFonts(fontManager);
        }

        private void UpdateFonts(FontManager fontManager)
        {
            if (_wasUsingLanguagePack && !HasActivePack)
            {
                var reloadFont = fontManager.GetType().GetMethod("ReloadFont",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                reloadFont?.Invoke(fontManager, new object[] { true });
                _fontRevision = -1;
            }

            if (HasActivePack)
            {
                ApplyFonts(fontManager);
            }

            _wasUsingLanguagePack = HasActivePack;
        }

        private class PackContentManager : ContentManager
        {
            private readonly LanguagePack _pack;

            public PackContentManager(IServiceProvider serviceProvider, string rootDirectory, LanguagePack pack)
                : base(serviceProvider, rootDirectory)
            {
                _pack = pack;
            }

            protected override Stream OpenStream(string assetName)
            {
                if (!_pack.TryGetAsset(assetName, out var asset))
                {
                    throw new ContentLoadException($"Language pack asset '{assetName}' was not found.");
                }

                return new MemoryStream(asset.Data, 0, asset.Data.Length, false, true);
            }
        }
    }
}
