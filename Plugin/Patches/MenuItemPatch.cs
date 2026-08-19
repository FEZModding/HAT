using System.Reflection;
using System.Runtime.CompilerServices;
using FezEngine.Tools;
using HatModLoader.Source;
using HatModLoader.Source.Languages;

namespace FezGame.Structure
{
    internal class patch_MenuItem<T>
    {
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public extern void orig_Slide(int direction);

        public void Slide(int direction)
        {
            if (typeof(T) != typeof(Language) || !IsLanguageSlider(this))
            {
                orig_Slide(direction);
                return;
            }

            var language = (Language)GetSliderValue(this);
            var nextLanguage = Hat.Instance.LanguagePacks.ChangeLanguage(language, direction);
            SetLanguageToSet(this, nextLanguage);
        }

        [MethodImpl(MethodImplOptions.ForwardRef)]
        public extern void orig_OnSelected();

        public void OnSelected()
        {
            orig_OnSelected();
            if (typeof(T) == typeof(Language) && IsLanguageSlider(this))
            {
                // FEZ serializes custom enum values as null, so correct the file after saving.
                SettingsManager.Save();
                LanguagePackManager.PersistSelectedLanguage();
            }
        }

        private static bool IsLanguageSlider(object item)
        {
            var type = item.GetType();
            var localized = (bool)type.GetProperty("LocalizeSliderValue")!.GetValue(item);
            var format = (string)type.GetProperty("LocalizationTagFormat")!.GetValue(item);
            return localized && string.Equals(format, "Language{0}", StringComparison.Ordinal);
        }

        private static object GetSliderValue(object item)
        {
            var getter = (Delegate)item.GetType().GetField("SliderValueGetter")!.GetValue(item);
            return getter.DynamicInvoke();
        }

        private static void SetLanguageToSet(object item, Language language)
        {
            var getter = (Delegate)item.GetType().GetField("SliderValueGetter")!.GetValue(item);
            var field = getter.Target.GetType().GetField("languageToSet",
                BindingFlags.Instance | BindingFlags.NonPublic);
            field!.SetValue(getter.Target, language);
        }
    }
}
