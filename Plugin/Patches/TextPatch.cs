using System.Runtime.CompilerServices;

using HatModLoader.Source;
using HatModLoader.Source.Assets;

namespace FezGame.Tools
{
    internal static class TextPatch
    {
        public static bool TryGetRawString(string tag, out string text)
        {
            if (tag == null || !tag.StartsWith("@"))
            {
                text = null;
                return false;
            }

            text = tag.Substring(1);
            return true;
        }
    }

    public static class patch_StaticText
    {
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern bool orig_TryGetString(string tag, out string text);
        public static bool TryGetString(string tag, out string text)
        {
            return Hat.Instance.TextResources.TryGetString(tag, TextLookupScope.Localized, out text) || orig_TryGetString(tag, out text);
        }

        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern string orig_GetString(string tag);
        public static string GetString(string tag)
        {
            if (TextPatch.TryGetRawString(tag, out var text))
            {
                return text;
            }

            return TryGetString(tag, out text) 
                ? text 
                : orig_GetString(tag);
        }
    }

    public static class patch_GameText
    {
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern string orig_GetString(string tag);
        public static string GetString(string tag)
        {
            if (TextPatch.TryGetRawString(tag, out var text))
            {
                return text;
            }

            return ModText.TryGetString(tag, out text) 
                ? text :
                orig_GetString(tag);
        }

        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern string orig_GetStringRaw(string tag);
        public static string GetStringRaw(string tag)
        {
            if (TextPatch.TryGetRawString(tag, out var text))
            {
                return text;
            }

            return ModText.TryGetStringRaw(tag, out text) 
                ? text 
                : orig_GetStringRaw(tag);
        }
    }

    public static class patch_CreditsText
    {
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern string orig_GetString(string tag);
        public static string GetString(string tag)
        {
            if (TextPatch.TryGetRawString(tag, out var text))
            {
                return text;
            }

            return ModText.TryGetStringRaw(tag, out text) 
                ? text 
                : orig_GetString(tag);
        }
    }

    public static class ModText
    {
        private const string MissingText = "[MISSING TEXT]";

        public static bool TryGetString(string tag, out string text)
        {
            return Hat.Instance.TextResources.TryGetString(tag, TextLookupScope.Localized, out text);
        }

        public static bool TryGetStringRaw(string tag, out string text)
        {
            return Hat.Instance.TextResources.TryGetString(tag, TextLookupScope.FallbackOnly, out text);
        }

        public static string GetString(string tag)
        {
            if (TryGetString(tag, out var text))
            {
                return text;
            }

            return MissingText;
        }

        public static string GetStringRaw(string tag)
        {
            if (TryGetStringRaw(tag, out var text))
            {
                return text;
            }

            return MissingText;
        }
    }
}
