using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Source cloak colors extracted from Hornet's texture (front and underside). Pixels near
    /// either color are recolored to the user's chosen tint. Values load from
    /// <c>cloak_palette.json</c> next to the plugin DLL (optional); built-in defaults match the
    /// shipped JSON so the mod works if the file is missing. Also holds the debug logging toggle.
    ///
    /// Parsing is intentionally dependency-free (regex only): Unity/BepInEx does not ship
    /// <c>System.Text.Json</c> next to game plugins, so referencing it caused TypeLoadException.
    /// </summary>
    internal static class CloakPaletteConfig
    {
        public static Color FrontUnity { get; private set; }
        public static Color UnderUnity { get; private set; }
        public static float MatchRadius { get; private set; }

        /// <summary>Verbose log lines (e.g. color changes). Editable in <c>cloak_palette.json</c>.</summary>
        public static bool DebugLogging { get; private set; }

        public static void Load()
        {
            ApplyDefaults();

            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(dir)) return;

            var diskPath = Path.Combine(dir, "cloak_palette.json");
            if (File.Exists(diskPath))
            {
                try
                {
                    var json = File.ReadAllText(diskPath);
                    if (TryApplyPaletteJson(json))
                    {
                        Log.Info($"Loaded cloak palette from {diskPath}");
                        return;
                    }

                    Log.Warn($"cloak_palette.json was not valid; using built-in defaults (#79404b / #501f3b).");
                }
                catch (Exception ex)
                {
                    Log.Warn($"Could not read cloak_palette.json: {ex.Message}. Using defaults.");
                }
            }
        }

        private static void ApplyDefaults()
        {
            // #79404b and #501f3b — must stay in sync with Config/cloak_palette.json
            FrontUnity = ToUnity(new CloakColor(0x79, 0x40, 0x4b));
            UnderUnity = ToUnity(new CloakColor(0x50, 0x1f, 0x3b));
            MatchRadius = 0.18f;
            DebugLogging = false;
        }

        /// <summary>
        /// Minimal JSON extraction for our fixed schema — no external JSON library.
        /// </summary>
        private static bool TryApplyPaletteJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            var trimmed = json.TrimStart();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return false;

            if (TryExtractHex(trimmed, "cloakFront", out var front))
                FrontUnity = ToUnity(front);

            if (TryExtractHex(trimmed, "cloakUnder", out var under))
                UnderUnity = ToUnity(under);

            if (TryExtractFloat(trimmed, "matchRadius", out var mr) && mr > 0f && mr <= 1f)
                MatchRadius = mr;

            if (TryExtractBool(trimmed, "debugLogging", out var dbg))
                DebugLogging = dbg;

            return true;
        }

        private static bool TryExtractBool(string json, string key, out bool value)
        {
            value = false;
            var pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*(?<v>true|false)";
            var m = Regex.Match(json, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success) return false;

            value = m.Groups["v"].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private static bool TryExtractHex(string json, string key, out CloakColor color)
        {
            color = CloakColor.Default;
            var pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*\"(?<hex>#?[0-9a-fA-F]{{6}})\"";
            var m = Regex.Match(json, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success) return false;

            var hex = m.Groups["hex"].Value;
            if (!hex.StartsWith("#", StringComparison.Ordinal))
                hex = "#" + hex;

            return CloakColor.TryParse(hex, out color);
        }

        private static bool TryExtractFloat(string json, string key, out float value)
        {
            value = 0f;
            var pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*(?<n>[0-9]+(?:\\.[0-9]+)?(?:[eE][-+]?[0-9]+)?)";
            var m = Regex.Match(json, pattern, RegexOptions.CultureInvariant);
            if (!m.Success) return false;

            return float.TryParse(
                m.Groups["n"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static Color ToUnity(CloakColor c) => c.ToUnityColor();
    }
}
