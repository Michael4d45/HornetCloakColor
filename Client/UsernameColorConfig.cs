using System;
using BepInEx.Configuration;
using HornetCloakColor.Shared;

namespace HornetCloakColor.Client
{
    /// <summary>Username tint preset list mirrors <see cref="CloakColorConfig.Preset"/> plus off/match-cloak.</summary>
    internal enum UsernameColorPreset
    {
        Disabled,
        MatchCloak,
        Custom,
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

    /// <summary>SSMP multiplayer name tint (separate RGB from cloak).</summary>
    internal class UsernameColorConfig
    {
        private static readonly CloakColor DefaultCustom = new CloakColor(200, 60, 60);

        public ConfigEntry<UsernameColorPreset> PresetChoice { get; }
        public ConfigEntry<string> CustomHex { get; }

        public event Action? UsernameTintChanged;

        public UsernameColorConfig(ConfigFile config)
        {
            PresetChoice = config.Bind(
                "Multiplayer Username",
                "Username Color Preset",
                UsernameColorPreset.Disabled,
                "Off / match cloak / same presets as cloak / custom hex.");

            CustomHex = config.Bind(
                "Multiplayer Username",
                "Custom Username Color",
                DefaultCustom.ToString(),
                "Only used when preset is Custom. #RRGGBB, RRGGBB, or r,g,b.");

            PresetChoice.SettingChanged += (_, _) => UsernameTintChanged?.Invoke();
            CustomHex.SettingChanged += (_, _) => UsernameTintChanged?.Invoke();
        }

        internal bool IsDisabled => PresetChoice.Value == UsernameColorPreset.Disabled;

        internal CloakColor ResolveRgb(CloakColorConfig cloakConfig) =>
            PresetChoice.Value switch
            {
                UsernameColorPreset.Disabled => CloakColor.Default,
                UsernameColorPreset.MatchCloak => cloakConfig.EffectiveColor,
                UsernameColorPreset.Custom => CloakColor.TryParse(CustomHex.Value, out var c) ? c : CloakColor.Default,
                UsernameColorPreset.Default => CloakColorConfig.ResolvePresetColor(
                    CloakColorConfig.Preset.Default,
                    CustomHex.Value),
                UsernameColorPreset.Crimson => CloakColorConfig.ResolvePresetColor(
                    CloakColorConfig.Preset.Crimson,
                    CustomHex.Value),
                UsernameColorPreset.Scarlet => CloakColorConfig.ResolvePresetColor(
                    CloakColorConfig.Preset.Scarlet,
                    CustomHex.Value),
                UsernameColorPreset.Amber => CloakColorConfig.ResolvePresetColor(
                    CloakColorConfig.Preset.Amber,
                    CustomHex.Value),
                UsernameColorPreset.Gold => CloakColorConfig.ResolvePresetColor(
                    CloakColorConfig.Preset.Gold,
                    CustomHex.Value),
                UsernameColorPreset.Emerald => CloakColorConfig.ResolvePresetColor(
                    CloakColorConfig.Preset.Emerald,
                    CustomHex.Value),
                UsernameColorPreset.Teal => CloakColorConfig.ResolvePresetColor(
                    CloakColorConfig.Preset.Teal,
                    CustomHex.Value),
                UsernameColorPreset.Azure => CloakColorConfig.ResolvePresetColor(
                    CloakColorConfig.Preset.Azure,
                    CustomHex.Value),
                UsernameColorPreset.Royal => CloakColorConfig.ResolvePresetColor(
                    CloakColorConfig.Preset.Royal,
                    CustomHex.Value),
                UsernameColorPreset.Violet => CloakColorConfig.ResolvePresetColor(
                    CloakColorConfig.Preset.Violet,
                    CustomHex.Value),
                UsernameColorPreset.Magenta => CloakColorConfig.ResolvePresetColor(
                    CloakColorConfig.Preset.Magenta,
                    CustomHex.Value),
                UsernameColorPreset.Obsidian => CloakColorConfig.ResolvePresetColor(
                    CloakColorConfig.Preset.Obsidian,
                    CustomHex.Value),
                UsernameColorPreset.Ivory => CloakColorConfig.ResolvePresetColor(
                    CloakColorConfig.Preset.Ivory,
                    CustomHex.Value),
                _ => CloakColor.Default,
            };
    }
}
