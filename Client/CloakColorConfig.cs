using System;
using BepInEx.Configuration;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// BepInEx configuration for the local player's cloak color. The user edits this via the
    /// in-game F1 configuration menu (BepInExConfigManager) or by hand in the .cfg file.
    ///
    /// We expose a preset enum (convenient) plus an explicit "Custom hex" string (powerful) so
    /// users who want a specific RGB value aren't restricted to the presets.
    /// </summary>
    internal class CloakColorConfig
    {
        public enum Preset
        {
            /// <summary>Use the custom hex color from <see cref="CustomHex"/>.</summary>
            Custom = 0,
            Default,    // No tint
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
        public ConfigEntry<bool> DebugLogging { get; }

        /// <summary>Fires when the effective cloak color changes.</summary>
        public event Action<CloakColor>? ColorChanged;

        public CloakColorConfig(ConfigFile config)
        {
            PresetChoice = config.Bind(
                "Appearance",
                "Cloak Color Preset",
                Preset.Default,
                "Choose a preset cloak color, or pick 'Custom' to use the hex value below.");

            CustomHex = config.Bind(
                "Appearance",
                "Custom Cloak Color",
                DefaultCustom.ToString(),
                "Custom cloak color used when preset is set to 'Custom'. Accepts #RRGGBB, RRGGBB, or 'r,g,b' (0-255 each).");

            DebugLogging = config.Bind(
                "Debug",
                "Debug Logging",
                false,
                "Enable verbose logging for troubleshooting.");

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
