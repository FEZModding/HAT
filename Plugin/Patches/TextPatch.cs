using System.Runtime.CompilerServices;
using HatModLoader.Source;

namespace FezGame.Tools
{
    public static class patch_StaticText
    {
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern bool orig_TryGetString(string tag, out string text);
        public static bool TryGetString(string tag, out string text) =>
            Hat.Instance.TextResources.TryGetString(tag, out text) || orig_TryGetString(tag, out text);

        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern string orig_GetString(string tag);
        public static string GetString(string tag) =>
            Hat.Instance.TextResources.GetString(tag, orig_GetString, fallbackOnly: false);
    }

    public static class patch_GameText
    {
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern string orig_GetString(string tag);
        public static string GetString(string tag) =>
            Hat.Instance.TextResources.GetString(tag, orig_GetString, fallbackOnly: false);

        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern string orig_GetStringRaw(string tag);
        public static string GetStringRaw(string tag) =>
            Hat.Instance.TextResources.GetStringRaw(tag, orig_GetStringRaw, fallbackOnly: true);
    }

    public static class patch_CreditsText
    {
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern string orig_GetString(string tag);
        public static string GetString(string tag) =>
            Hat.Instance.TextResources.GetString(tag, orig_GetString, fallbackOnly: true);
    }
}