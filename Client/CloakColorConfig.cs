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

        // Cloak-only shader controls. When CloakOnlyMode is enabled and the embedded
        // shader bundle is available, only red-dominant pixels are recolored. Otherwise
        // the legacy whole-character vertex tint is used as a fallback.
        public ConfigEntry<bool> CloakOnlyMode { get; }
        public ConfigEntry<float> CloakCenterHue { get; }
        public ConfigEntry<float> CloakHueWidth { get; }
        public ConfigEntry<float> CloakMinSaturation { get; }
        public ConfigEntry<float> CloakStrength { get; }

        /// <summary>Fires when the effective cloak color changes.</summary>
        public event Action<CloakColor>? ColorChanged;

        /// <summary>Fires when any of the cloak-shader parameters change (mode, hue range, strength).</summary>
        public event Action? ShaderSettingsChanged;

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

            CloakOnlyMode = config.Bind(
                "Appearance",
                "Cloak Only Mode",
                true,
                "When true, only the red cloak is recolored (requires the embedded shader bundle). "
                + "When false, the entire character sprite is tinted the chosen color.");

            CloakCenterHue = config.Bind(
                "Advanced",
                "Cloak Center Hue",
                0.98f,
                new ConfigDescription(
                    "Center of the hue range that counts as 'cloak' in the source texture. "
                    + "0/1 = red, 0.33 = green, 0.66 = blue.",
                    new AcceptableValueRange<float>(0f, 1f)));

            CloakHueWidth = config.Bind(
                "Advanced",
                "Cloak Hue Width",
                0.50f,
                new ConfigDescription(
                    "Half-width of the matched hue band on each side of the center hue. "
                    + "Larger values include more pixels (and risk catching non-cloak reds).",
                    new AcceptableValueRange<float>(0f, 0.5f)));

            CloakMinSaturation = config.Bind(
                "Advanced",
                "Cloak Min Saturation",
                0.30f,
                new ConfigDescription(
                    "Minimum saturation a pixel needs to be considered part of the cloak. "
                    + "Keeps the tint off white/grey pixels (mask, eyes, etc).",
                    new AcceptableValueRange<float>(0f, 1f)));

            CloakStrength = config.Bind(
                "Advanced",
                "Cloak Recolor Strength",
                1.0f,
                new ConfigDescription(
                    "How strongly the cloak is recolored. 0 = original cloak, 1 = full recolor.",
                    new AcceptableValueRange<float>(0f, 1f)));

            DebugLogging = config.Bind(
                "Debug",
                "Debug Logging",
                false,
                "Enable verbose logging for troubleshooting.");

            PresetChoice.SettingChanged += (_, _) => ColorChanged?.Invoke(CurrentColor);
            CustomHex.SettingChanged += (_, _) => ColorChanged?.Invoke(CurrentColor);

            CloakOnlyMode.SettingChanged       += (_, _) => ShaderSettingsChanged?.Invoke();
            CloakCenterHue.SettingChanged      += (_, _) => ShaderSettingsChanged?.Invoke();
            CloakHueWidth.SettingChanged       += (_, _) => ShaderSettingsChanged?.Invoke();
            CloakMinSaturation.SettingChanged  += (_, _) => ShaderSettingsChanged?.Invoke();
            CloakStrength.SettingChanged       += (_, _) => ShaderSettingsChanged?.Invoke();
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
