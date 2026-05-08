using System;
using System.Collections.Generic;
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
        /// Per-atlas mask file resolution (primary collection folder, compatibility alias). Independent of
        /// <see cref="DebugLogging"/> so you can trace <see cref="CloakMaskManager"/> without scanner spam.
        /// </summary>
        public static bool MaskResolutionDebugLogging { get; private set; }

        /// <summary>Mask path tracing when diagnosing SSMP remote bodies / empty tk2d collection names.</summary>
        internal static bool LogMaskResolutionDiagnostics => DebugLogging || MaskResolutionDebugLogging;

        /// <summary>
        /// SSMP map / compass icon sync (broadcast, late-join replay, deferred <c>CreatePlayerIcon</c>).
        /// Independent of <see cref="DebugLogging"/> so you can trace multiplayer pins without cloak spam.
        /// </summary>
        public static bool MapIconDebugLogging { get; private set; }

        /// <summary>True when either cloak debug or map-icon diagnostics is enabled.</summary>
        public static bool LogMapIconDiagnostics => DebugLogging || MapIconDebugLogging;

        /// <summary>
        /// Master switch for all mask-related PNG dumps (<c>-original.png</c> and optional empty templates).
        /// When false, nothing is written regardless of <see cref="MissingMaskDumpAllowlist"/>.
        /// When true, missing-mask auto-dumps are still limited to <see cref="MissingMaskDumpAllowlist"/> stems.
        /// </summary>
        public static bool DumpDiscoveredTextures { get; private set; }

        /// <summary>
        /// Sanitized <c>Texture.name</c> stems allowed for automatic missing-mask dumps (<c>-original.png</c> + empty template)
        /// when <see cref="DumpDiscoveredTextures"/> is true.
        /// When non-empty: only listed stems dump from tk2d collection folders and <c>CloakMasks/Texture2D/</c>.
        /// When null/empty: no missing-mask dumps.
        /// </summary>
        public static HashSet<string>? MissingMaskDumpAllowlist { get; private set; }

        /// <summary>
        /// When non-null and non-empty, seconds after plugin load (each value triggers one sweep) to scan
        /// <c>UnityEngine.Resources.FindObjectsOfTypeAll&lt;Texture2D&gt;()</c> for loaded textures whose names match
        /// <see cref="MissingMaskDumpAllowlist"/> and dump missing-mask files under <c>CloakMasks/Texture2D/</c>
        /// when <see cref="DumpDiscoveredTextures"/> is true.
        /// Repeat delays cover atlases that load later (open menus, enter areas). Cannot dump textures Unity has never loaded into memory.
        /// </summary>
        public static float[]? MissingMaskDumpAllowlistSweepDelaysSec { get; private set; }

        /// <summary>
        /// How often <see cref="CloakRecolor"/> re-runs <c>GetComponentsInChildren</c> for <see cref="MeshRenderer"/> to
        /// refresh its cache (material re-apply still runs every <c>LateUpdate</c>). Higher = cheaper;
        /// lower picks up new child meshes from animations sooner. 1 = full hierarchy scan every frame (most responsive).
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
                            if (MaskResolutionDebugLogging)
                                Log.Info("[CloakMasksDiag] maskResolutionDebugLogging is true — tracing mask path resolution; grep \"[CloakMasksDiag]\".");
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
            LogMissingMaskDumpConfigSummary();
        }

        private static void ApplyDefaults()
        {
            DebugLogging = false;
            MaskResolutionDebugLogging = false;
            MapIconDebugLogging = false;
            // Default was 30 (~500 ms at 60 Hz before newly spawned attack/death meshes appeared in the cache).
            HeroMeshRescanIntervalFrames = 4;
            DumpDiscoveredTextures = false;
            MissingMaskDumpAllowlist = null;
            MissingMaskDumpAllowlistSweepDelaysSec = null;
        }

        private static void LogMissingMaskDumpConfigSummary()
        {
            var n = MissingMaskDumpAllowlist?.Count ?? 0;
            var sweep = MissingMaskDumpAllowlistSweepDelaysSec;
            var sweepNote = sweep == null || sweep.Length == 0
                ? "sweep=off"
                : $"sweep={string.Join(",", sweep)}s";
            Log.Info(
                $"[CloakMasks] texture dump policy: dumpDiscoveredTextures={DumpDiscoveredTextures} " +
                $"(off = no PNG dumps); allowlist={n} stems, {sweepNote} " +
                "(when dumps on: allowlist + stem match → missing-mask dump; else no missing-mask dump).");
        }

        /// <summary>
        /// Stem is allowlisted for missing-mask auto-dump (<c>-original.png</c> + empty template when no PNG exists yet).
        /// Actual file writes occur only when <see cref="DumpDiscoveredTextures"/> is true.
        /// </summary>
        /// <param name="textureName">
        /// For tk2d collection masks: <c>mainTex.name</c>. For <c>CloakMasks/Texture2D/</c>: the lookup stem for that PNG
        /// (sprite name or atlas texture name — whichever this resolve pass is using), not necessarily <c>Texture.name</c>
        /// when one atlas is shared by many sprites.
        /// </param>
        internal static bool ShouldAutoDumpMissingMask(string? textureName)
        {
            var stem = NormalizeMaskDumpStem(textureName);
            if (stem.Length == 0)
                return false;

            if (MissingMaskDumpAllowlist != null && MissingMaskDumpAllowlist.Count > 0)
                return MissingMaskDumpAllowlist.Contains(stem);

            return false;
        }

        /// <summary>Comparable stem for allowlist entries and runtime texture names (drops <c>.png</c>, sanitizes).</summary>
        internal static string NormalizeMaskDumpStem(string? textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return string.Empty;
            var t = textureName.Trim();
            if (t.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                t = t[..^4];
            return CloakDiskNames.SanitizeFileStem(t);
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

            if (TryExtractBool(trimmed, "maskResolutionDebugLogging", out var maskDiag))
                MaskResolutionDebugLogging = maskDiag;

            if (TryExtractBool(trimmed, "mapIconDebugLogging", out var mapIconDbg))
                MapIconDebugLogging = mapIconDbg;

            if (TryExtractInt(trimmed, "heroMeshRescanIntervalFrames", out var heroIv) && heroIv > 0 && heroIv <= 600)
                HeroMeshRescanIntervalFrames = heroIv;

            if (TryExtractBool(trimmed, "dumpDiscoveredTextures", out var dumpTex))
                DumpDiscoveredTextures = dumpTex;

            if (TryExtractStringArray(trimmed, "missingMaskDumpAllowlist", out var allow))
            {
                MissingMaskDumpAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in allow)
                {
                    var stem = NormalizeMaskDumpStem(raw);
                    if (stem.Length > 0)
                        MissingMaskDumpAllowlist.Add(stem);
                }
            }

            if (TryExtractFloatArray(trimmed, "missingMaskDumpAllowlistSweepDelaysSec", out var sweepDelays))
                MissingMaskDumpAllowlistSweepDelaysSec = sweepDelays.Length > 0 ? sweepDelays : null;

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

        /// <summary>Parses a JSON string array for <paramref name="key"/> — values only, no nested objects.</summary>
        private static bool TryExtractStringArray(string json, string key, out string[] values)
        {
            values = Array.Empty<string>();
            if (!TryExtractJsonArraySlice(json, key, out var inner))
                return false;

            var list = new List<string>();
            foreach (Match m in Regex.Matches(inner, "\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.CultureInvariant))
                list.Add(UnescapeJsonString(m.Groups[1].Value));

            values = list.ToArray();
            return true;
        }

        /// <summary>Parses a JSON number array for <paramref name="key"/>.</summary>
        private static bool TryExtractFloatArray(string json, string key, out float[] values)
        {
            values = Array.Empty<float>();
            if (!TryExtractJsonArraySlice(json, key, out var inner))
                return false;

            var parts = inner.Split(',');
            var list = new List<float>(parts.Length);
            foreach (var p in parts)
            {
                var t = p.Trim();
                if (t.Length == 0)
                    continue;
                if (!float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    return false;
                list.Add(f);
            }

            values = list.ToArray();
            return true;
        }

        /// <summary>Raw payload inside [...] for a top-level string key (no nested arrays).</summary>
        private static bool TryExtractJsonArraySlice(string json, string key, out string inner)
        {
            inner = string.Empty;
            var needle = $"\"{key}\"";
            var idx = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0)
                return false;

            var bracket = json.IndexOf('[', idx + needle.Length);
            if (bracket < 0)
                return false;

            var depth = 0;
            var end = -1;
            for (var i = bracket; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }

            if (end < 0)
                return false;

            inner = json.Substring(bracket + 1, end - bracket - 1);
            return true;
        }

        private static string UnescapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0)
                return s;
            return s.Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

    }
}
