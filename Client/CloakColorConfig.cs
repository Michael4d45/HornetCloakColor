using System;
using BepInEx.Configuration;
using HornetCloakColor.Shared;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// BepInEx configuration for the local player's cloak color. Cloak *matching* (which texture
    /// pixels count as the cloak) is driven by <c>cloak_palette.json</c> next to the DLL, not
    /// by these settings.
    /// </summary>
    internal class CloakColorConfig
    {
        public enum Preset
        {
            /// <summary>Use the custom hex color from <see cref="CustomHex"/>.</summary>
            Custom = 0,
            Default,
            Crimson,
            Scarlet,
            Amber,
            Gold,
            Emerald,
            Teal,
            Azure,
            Royal,
            Violet,
            Magenta,
            Obsidian,
            Ivory,
        }

        private static readonly CloakColor DefaultCustom = new CloakColor(200, 60, 60);

        public ConfigEntry<Preset> PresetChoice { get; }
        public ConfigEntry<string> CustomHex { get; }

        /// <summary>Fires when the effective cloak color changes.</summary>
        public event Action<CloakColor>? ColorChanged;

        public CloakColorConfig(ConfigFile config)
        {
            PresetChoice = config.Bind(
                "Appearance",
                "Cloak Color Preset",
                Preset.Default,
                "Choose 'Custom' for your own hex.");

            CustomHex = config.Bind(
                "Appearance",
                "Custom Cloak Color",
                DefaultCustom.ToString(),
                "Custom cloak color used when preset is set to 'Custom'. Accepts #RRGGBB, RRGGBB, or 'r,g,b' (0-255 each).");

            PresetChoice.SettingChanged += (_, _) => ColorChanged?.Invoke(CurrentColor);
            CustomHex.SettingChanged += (_, _) => ColorChanged?.Invoke(CurrentColor);
        }

        /// <summary>
        /// The currently selected cloak color based on the preset + custom hex fields.
        /// </summary>
        public CloakColor CurrentColor => Resolve(PresetChoice.Value, CustomHex.Value);

        private static CloakColor Resolve(Preset preset, string customHex)
        {
            return preset switch
            {
                Preset.Default => CloakColor.Default,
                Preset.Crimson => new CloakColor(156, 36, 48),
                Preset.Scarlet => new CloakColor(220, 56, 72),
                Preset.Amber => new CloakColor(230, 140, 40),
                Preset.Gold => new CloakColor(232, 190, 64),
                Preset.Emerald => new CloakColor(56, 170, 90),
                Preset.Teal => new CloakColor(60, 180, 180),
                Preset.Azure => new CloakColor(64, 148, 230),
                Preset.Royal => new CloakColor(72, 92, 210),
                Preset.Violet => new CloakColor(140, 80, 210),
                Preset.Magenta => new CloakColor(220, 80, 180),
                Preset.Obsidian => new CloakColor(40, 40, 55),
                Preset.Ivory => new CloakColor(240, 230, 205),
                Preset.Custom => CloakColor.TryParse(customHex, out var parsed) ? parsed : CloakColor.Default,
                _ => CloakColor.Default,
            };
        }
    }
}
