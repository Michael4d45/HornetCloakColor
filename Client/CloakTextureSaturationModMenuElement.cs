using System;
using BepInEx.Configuration;
using Silksong.ModMenu.Elements;
using Silksong.ModMenu.Models;
using Silksong.ModMenu.Plugin;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Registers a <see cref="ConfigEntryFactory.MenuElementGenerator"/> on
    /// <see cref="CloakColorConfig.TextureSaturationBoost"/> so Silksong Mod Menu shows a slider
    /// (the default factory uses a text field for ranged floats).
    /// </summary>
    internal static class CloakTextureSaturationModMenuElement
    {
        internal const string ConfigKey = "Cloak Texture Saturation Boost";
        internal const string ConfigSection = "Appearance";

        /// <summary>41 ticks → 0.05 steps from 0 to 2.</summary>
        private const int SliderTicks = 41;

        /// <summary>
        /// Short in-game label so the Mod Menu row does not overlap the slider track
        /// (the full name stays on the BepInEx config key / cfg file).
        /// </summary>
        private const string SliderMenuLabel = "Cloak saturation";

        internal static bool TryCreateElement(ConfigEntryBase entry, out MenuElement menuElement)
        {
            menuElement = null!;
            if (entry is not ConfigEntry<float> floatEntry) return false;
            if (entry.Definition.Section != ConfigSection || entry.Definition.Key != ConfigKey) return false;

            var model = new LinearFloatSliderModel(0f, 2f, SliderTicks);
            if (!model.SetValue(Mathf.Clamp(floatEntry.Value, 0f, 2f)))
                model.SetValue(1f);

            var slider = new SliderElement<float>(SliderMenuLabel, model);

            void PushModelToConfig(float _)
            {
                var v = Mathf.Clamp(slider.Value, 0f, 2f);
                if (!Mathf.Approximately(floatEntry.Value, v))
                    floatEntry.Value = v;
            }

            void OnEntryChanged(object? _, SettingChangedEventArgs e)
            {
                if (e.ChangedSetting != floatEntry) return;
                var v = Mathf.Clamp(floatEntry.Value, 0f, 2f);
                if (!Mathf.Approximately(slider.Value, v))
                    model.SetValue(v);
            }

            slider.OnValueChanged += PushModelToConfig;
            floatEntry.ConfigFile.SettingChanged += OnEntryChanged;
            slider.OnDispose += () =>
            {
                slider.OnValueChanged -= PushModelToConfig;
                floatEntry.ConfigFile.SettingChanged -= OnEntryChanged;
            };

            menuElement = slider;
            return true;
        }
    }
}
