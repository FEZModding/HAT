using System.Reflection;
using System.Text.Json;
using FezEngine.Tools;

namespace HatModLoader.Source.Assets
{
    public sealed class TextResourceManager
    {
        private readonly Hat _hat;

        private readonly Dictionary<string, Dictionary<string, string>> _hatResources;

        public TextResourceManager(Hat hat)
        {
            _hat = hat;
            _hatResources = GetHatResources();
        }

        public bool TryGetString(string tag, out string text, bool fallbackOnly = false)
        {
            if (tag != null && tag.StartsWith('@'))
            {
                text = tag[1..];
                return true;
            }

            if (tag != null && !fallbackOnly && TryGetLanguageString(Culture.TwoLetterISOLanguageName, tag, out text))
            {
                return true;
            }

            if (tag != null && TryGetLanguageString(string.Empty, tag, out text))
            {
                return true;
            }

            text = null;
            return false;
        }

        public string GetString(string tag, Func<string, string> origGetString, bool fallbackOnly = false)
        {
            return TryGetString(tag, out var text, fallbackOnly) ? text : origGetString(tag);
        }

        public string GetStringRaw(string tag, Func<string, string> origGetStringRaw, bool fallbackOnly = true)
        {
            return GetString(tag, origGetStringRaw, fallbackOnly);
        }

        private bool TryGetLanguageString(string language, string tag, out string text)
        {
            for (var i = _hat.Mods.Count - 1; i >= 0; i--)
            {
                var resources = _hat.Mods[i].ModText?.Resources;
                if (resources != null && resources.TryGetValue(language, out var entries) &&
                    entries.TryGetValue(tag, out text) && text != null)
                {
                    return true;
                }
            }

            if (_hatResources.TryGetValue(language, out var defaults) &&
                defaults.TryGetValue(tag, out text) && text != null)
            {
                return true;
            }

            text = null;
            return false;
        }

        private static Dictionary<string, Dictionary<string, string>> GetHatResources()
        {
            const string hatTextResource = "HatText.feztxt.json";

            using var defaults = Assembly.GetExecutingAssembly().GetManifestResourceStream(hatTextResource);
            if (defaults == null)
            {
                throw new InvalidOperationException($"Missing embedded resource {hatTextResource}");
            }

            var resources = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(defaults);
            if (resources == null)
            {
                throw new InvalidDataException($"Invalid embedded resource {hatTextResource}");
            }

            return resources;
        }
    }
}