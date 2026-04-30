using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using HornetCloakColor.Shared;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Runtime toggles loaded from <c>cloak_palette.json</c> next to the plugin DLL.
    ///
    /// Parsing is intentionally dependency-free (regex only): Unity/BepInEx does not ship
    /// <c>System.Text.Json</c> next to game plugins, so referencing it caused TypeLoadException.
    /// </summary>
    internal static class CloakPaletteConfig
    {
        /// <summary>Verbose log lines (e.g. color changes). Editable in <c>cloak_palette.json</c>.</summary>
        public static bool DebugLogging { get; private set; }

        /// <summary>
        /// SSMP map / compass icon sync (broadcast, late-join replay, deferred <c>CreatePlayerIcon</c>).
        /// Independent of <see cref="DebugLogging"/> so you can trace multiplayer pins without cloak spam.
        /// </summary>
        public static bool MapIconDebugLogging { get; private set; }

        /// <summary>True when either cloak debug or map-icon diagnostics is enabled.</summary>
        public static bool LogMapIconDiagnostics => DebugLogging || MapIconDebugLogging;

        /// <summary>
        /// When true, the first time a mask is resolved for an atlas, writes the in-game
        /// <c>mainTexture</c> next to the mask as <c>&lt;mask-stem&gt;-original.png</c> (same folder as the
        /// mask file on disk). Skips if that file already exists.
        /// </summary>
        public static bool DumpDiscoveredTextures { get; private set; }

        /// <summary>
        /// How often <see cref="CloakRecolor"/> re-runs <c>GetComponentsInChildren</c> for <see cref="MeshRenderer"/> to
        /// refresh its cache (material re-apply still runs every <c>LateUpdate</c>). Higher = cheaper;
        /// lower picks up new child meshes from animations sooner. 1 = previous behavior (full scan every frame).
        /// </summary>
        public static int HeroMeshRescanIntervalFrames { get; private set; }

        public static void Load()
        {
            ApplyDefaults();

            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(dir))
            {
                var diskPath = Path.Combine(dir, "cloak_palette.json");
                if (File.Exists(diskPath))
                {
                    try
                    {
                        var json = File.ReadAllText(diskPath);
                        if (TryApplyPaletteJson(json))
                        {
                            Log.Info($"Loaded cloak runtime config from {diskPath}.");
                            if (MapIconDebugLogging)
                                Log.Info("[MapIcon] mapIconDebugLogging is true — tracing map/compass sync; grep log for \"[MapIcon]\".");
                        }
                        else
                            Log.Warn("cloak_palette.json was not valid; using built-in defaults from the mod DLL.");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Could not read cloak_palette.json: {ex.Message}. Using defaults.");
                    }
                }
            }

            CloakMaskManager.OnPaletteReloaded();
        }

        private static void ApplyDefaults()
        {
            DebugLogging = false;
            MapIconDebugLogging = false;
            HeroMeshRescanIntervalFrames = 30;
            DumpDiscoveredTextures = false;
        }

        /// <summary>
        /// Minimal JSON extraction for our fixed schema — no external JSON library.
        /// </summary>
        private static bool TryApplyPaletteJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            var trimmed = json.TrimStart();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return false;

            if (TryExtractBool(trimmed, "debugLogging", out var dbg))
                DebugLogging = dbg;

            if (TryExtractBool(trimmed, "mapIconDebugLogging", out var mapIconDbg))
                MapIconDebugLogging = mapIconDbg;

            if (TryExtractInt(trimmed, "heroMeshRescanIntervalFrames", out var heroIv) && heroIv > 0 && heroIv <= 600)
                HeroMeshRescanIntervalFrames = heroIv;

            if (TryExtractBool(trimmed, "dumpDiscoveredTextures", out var dumpTex))
                DumpDiscoveredTextures = dumpTex;

            return true;
        }

        private static bool TryExtractInt(string json, string key, out int value)
        {
            value = 0;
            var pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*(?<n>-?[0-9]+)";
            var m = Regex.Match(json, pattern, RegexOptions.CultureInvariant);
            if (!m.Success) return false;
            return int.TryParse(m.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
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

    }
}
