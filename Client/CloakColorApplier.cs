using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Entry point that applies a <see cref="CloakColor"/> to a player GameObject.
    ///
    /// All real work happens inside the per-player <see cref="CloakRecolor"/> MonoBehaviour
    /// — this static helper just attaches/configures it. The MonoBehaviour reasserts the
    /// shader and tint each frame so animation-driven material swaps don't undo our work.
    ///
    /// Two render paths are supported:
    /// <list type="bullet">
    ///   <item>Cloak-only: the embedded <c>CloakHueShift</c> shader recolors red pixels only.</item>
    ///   <item>Legacy: the entire tk2dSprite is tinted via vertex color.</item>
    /// </list>
    /// </summary>
    internal static class CloakColorApplier
    {
        /// <summary>
        /// Apply the given color to the player GameObject using the user's current config.
        /// Safe to call with a null target or before the plugin is fully initialized.
        /// </summary>
        public static void Apply(GameObject? playerObject, CloakColor color)
        {
            if (playerObject == null) return;

            var config = HornetCloakColorPlugin.Instance?.ColorConfig;

            // Defaults match the shader so things still work if the plugin instance isn't ready yet.
            var cloakOnly = config?.CloakOnlyMode.Value ?? true;
            var centerHue = config?.CloakCenterHue.Value ?? 0.98f;
            var hueWidth  = config?.CloakHueWidth.Value ?? 0.50f;
            var minSat    = config?.CloakMinSaturation.Value ?? 0.30f;
            var strength  = config?.CloakStrength.Value ?? 1.0f;

            CloakRecolor.AttachOrUpdate(playerObject, color, cloakOnly,
                                        centerHue, hueWidth, minSat, strength);
        }

        /// <summary>
        /// Reapply the current shader/tint settings to a player without changing its color.
        /// Used when the user toggles Cloak Only Mode or tweaks the hue range live.
        /// </summary>
        public static void RefreshSettings(GameObject? playerObject)
        {
            if (playerObject == null) return;
            var existing = playerObject.GetComponent<CloakRecolor>();
            if (existing == null) return;

            var config = HornetCloakColorPlugin.Instance?.ColorConfig;
            var cloakOnly = config?.CloakOnlyMode.Value ?? true;
            var centerHue = config?.CloakCenterHue.Value ?? 0.98f;
            var hueWidth  = config?.CloakHueWidth.Value ?? 0.50f;
            var minSat    = config?.CloakMinSaturation.Value ?? 0.30f;
            var strength  = config?.CloakStrength.Value ?? 1.0f;

            existing.Configure(existing.Color, cloakOnly, centerHue, hueWidth, minSat, strength);
        }
    }
}
