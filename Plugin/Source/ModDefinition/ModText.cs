using Common;
using System.Text.Json;
using FEZRepacker.Core.Conversion;
using FEZRepacker.Core.XNB;
using HatModLoader.Source.Assets;

namespace HatModLoader.Source.ModDefinition
{
    public class ModText
    {
        public IReadOnlyDictionary<string, Dictionary<string, string>> Resources { get; private set; } =
            new Dictionary<string, Dictionary<string, string>>();

        private ModText()
        {
        }

        public static bool TryLoad(AssetMod assetMod, out ModText modText)
        {
            modText = new ModText();
            if (modText.Reload(assetMod))
            {
                return true;
            }

            modText = null;
            return false;
        }

        public bool Reload(AssetMod assetMod)
        {
            var modTextAsset = assetMod?.Assets.LastOrDefault(asset => asset.AssetType == AssetType.ModTextResource);
            if (modTextAsset == null)
            {
                return false;
            }

            try
            {
                using var stream = new MemoryStream(modTextAsset.Data, false);
                var textStorage = XnbSerializer.Deserialize(stream);
                if (textStorage == null)
                {
                    throw new InvalidDataException("ModText has no text resource");
                }

                using var converted = FormatConversion.Convert(textStorage);
                var json = converted.RequireData(".json");
                json.Position = 0;
                var resources = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                if (resources == null || resources.Any(language => language.Value == null))
                {
                    throw new InvalidDataException("ModText must contain text dictionaries for its languages");
                }

                Resources = resources;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("HAT", LogSeverity.Warning, $"Could not load ModText: {ex.Message}");
                return false;
            }
        }
    }
}