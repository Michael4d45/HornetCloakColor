using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Reference cloak colors extracted from Hornet's atlases (front, underside, recoil, etc.).
    /// Texels close (within <see cref="MatchRadius"/>) to <i>any</i> reference color get recolored.
    /// Values load from <c>cloak_palette.json</c> next to the plugin DLL; built-in defaults match
    /// the shipped JSON so the mod still works if the file is missing.
    ///
    /// Schema (preferred):
    /// <code>
    /// {
    ///   "cloakColors": ["#79404b", "#501f3b"],
    ///   "avoidColors": [],
    ///   "matchRadius": 0.18,
    ///   "avoidMatchRadius": 0.18,
    ///   "debugLogging": false,
    ///   "mapIconDebugLogging": false,
    ///   "perfDiagnostics": false
    /// }
    /// </code>
    ///
    /// Parsing is intentionally dependency-free (regex only): Unity/BepInEx does not ship
    /// <c>System.Text.Json</c> next to game plugins, so referencing it caused TypeLoadException.
    /// </summary>
    internal static class CloakPaletteConfig
    {
        /// <summary>
        /// Length-<see cref="CloakShaderManager.MaxCloakColors"/> Vector4 array uploaded to the
        /// shader. Unused slots are pushed far away (rgb = 10) so distance() never matches.
        /// </summary>
        public static Vector4[] SrcColors { get; private set; } = new Vector4[CloakShaderManager.MaxCloakColors];

        /// <summary>How many slots in <see cref="SrcColors"/> represent real cloak colors.</summary>
        public static int SrcCount { get; private set; }

        /// <summary>
        /// Length-<see cref="CloakShaderManager.MaxAvoidColors"/> — texels close to any of these
        /// in RGB get their recolor mask reduced (e.g. skin, armor). Unused slots use the sentinel.
        /// </summary>
        public static Vector4[] AvoidColors { get; private set; } = new Vector4[CloakShaderManager.MaxAvoidColors];

        /// <summary>How many entries in <see cref="AvoidColors"/> are real avoid colors.</summary>
        public static int AvoidCount { get; private set; }

        public static float MatchRadius { get; private set; }

        /// <summary>
        /// Smoothstep outer radius for the avoid mask (same semantics as <see cref="MatchRadius"/>).
        /// If omitted from JSON, defaults to <see cref="MatchRadius"/> after that value is loaded.
        /// </summary>
        public static float AvoidMatchRadius { get; private set; }

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
        /// When true, logs aggregated <c>[HCC/Perf]</c> lines every ~2s for
        /// <see cref="CloakRecolor"/>, <see cref="CloakSceneScanner"/>, and map sync. Off by default.
        /// </summary>
        public static bool PerfDiagnostics { get; private set; }

        /// <summary>
        /// Substrings (case-insensitive) used by <see cref="CloakSceneScanner"/> to decide
        /// whether a tk2dSprite's main texture belongs to Hornet. Default: <c>["hornet"]</c>.
        /// </summary>
        public static string[] SceneScanTextureContains { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// Substrings (case-insensitive) used by <see cref="CloakSceneScanner"/> to match
        /// against the full GameObject path of a tk2dSprite. Useful for scene-specific
        /// Hornet poses (e.g. resting in bed at a bench) whose atlas instance the active
        /// hero never renders directly. Default: <c>["hornet"]</c>.
        /// </summary>
        public static string[] SceneScanPathContains { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// How often (in frames) the scene scanner walks every tk2dSprite. 1 = every frame,
        /// 3 is a good default. Higher = cheaper but slightly slower to color new sprites.
        /// </summary>
        public static int SceneScanIntervalFrames { get; private set; }

        /// <summary>
        /// When true, the first time the mod sees each Hornet atlas it dumps a PNG to
        /// <c>BepInEx/plugins/HornetCloakColor/TextureDumps/</c>. Useful for figuring out
        /// which sprite sheet maps to a runtime <c>InstanceID</c> and for sampling colors
        /// to add to <c>cloakColors</c>. Default: false (cheap), turn on temporarily.
        /// </summary>
        public static bool DumpDiscoveredTextures { get; private set; }

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
                        Log.Info($"Loaded cloak palette from {diskPath} ({SrcCount} cloak / {AvoidCount} avoid reference color(s)).");
                        if (MapIconDebugLogging)
                            Log.Info("[MapIcon] mapIconDebugLogging is true — tracing map/compass sync; grep log for \"[MapIcon]\".");
                        if (PerfDiagnostics)
                            Log.Info("[HCC/Perf] perfDiagnostics is true — grep BepInEx log for \"[HCC/Perf]\" (≈2s rolling window).");
                        return;
                    }

                    Log.Warn("cloak_palette.json was not valid; using built-in defaults from the mod DLL.");
                }
                catch (Exception ex)
                {
                    Log.Warn($"Could not read cloak_palette.json: {ex.Message}. Using defaults.");
                }
            }
        }

        private static void ApplyDefaults()
        {
            // Must stay in sync with Config/cloak_palette.json (shipped defaults).
            SetSources(ParseHexColors(
                "#79404b", "#d4b8b8", "#7a414c", "#b2808c", "#351c20", "#501f3b", "#562d35", "#3b162b",
                "#ec7f92", "#955a70", "#efbdd1", "#ae5c6c", "#994d5c", "#a7807b", "#be485e", "#592439"));
            SetAvoidSources(ParseHexColors(
                "#ffffff", "#000000", "#c7cbb5", "#16162c", "#808080", "#5b4133", "#231914", "#644662",
                "#282c34", "#8a6187"));
            MatchRadius = 0.135f;
            AvoidMatchRadius = 0.120f;
            DebugLogging = false;
            MapIconDebugLogging = false;
            SceneScanTextureContains = new[] { "hornet" };
            SceneScanPathContains = new[] { "hornet" };
            SceneScanIntervalFrames = 3;
            DumpDiscoveredTextures = false;
            PerfDiagnostics = false;
        }

        /// <summary>
        /// Minimal JSON extraction for our fixed schema — no external JSON library.
        /// </summary>
        private static bool TryApplyPaletteJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            var trimmed = json.TrimStart();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return false;

            var arrayList = ExtractHexArray(trimmed, "cloakColors");
            if (arrayList.Count > 0)
                SetSources(arrayList);

            if (TryExtractFloat(trimmed, "matchRadius", out var mr) && mr > 0f && mr <= 1f)
                MatchRadius = mr;

            var avoidList = ExtractHexArray(trimmed, "avoidColors");
            SetAvoidSources(avoidList);

            if (TryExtractFloat(trimmed, "avoidMatchRadius", out var amr) && amr > 0f && amr <= 1f)
                AvoidMatchRadius = amr;
            else
                AvoidMatchRadius = MatchRadius;

            if (TryExtractBool(trimmed, "debugLogging", out var dbg))
                DebugLogging = dbg;

            if (TryExtractBool(trimmed, "mapIconDebugLogging", out var mapIconDbg))
                MapIconDebugLogging = mapIconDbg;

            var scanFilters = ExtractStringArray(trimmed, "sceneScanTextureContains");
            if (scanFilters.Count > 0)
                SceneScanTextureContains = scanFilters.ToArray();

            var pathFilters = ExtractStringArray(trimmed, "sceneScanPathContains");
            if (pathFilters.Count > 0)
                SceneScanPathContains = pathFilters.ToArray();

            if (TryExtractInt(trimmed, "sceneScanIntervalFrames", out var iv) && iv > 0 && iv <= 240)
                SceneScanIntervalFrames = iv;

            if (TryExtractBool(trimmed, "dumpDiscoveredTextures", out var dump))
                DumpDiscoveredTextures = dump;

            if (TryExtractBool(trimmed, "perfDiagnostics", out var perf))
                PerfDiagnostics = perf;

            return true;
        }

        private static List<string> ExtractStringArray(string json, string key)
        {
            var result = new List<string>();
            var arrPattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*\\[(?<arr>[^\\]]*)\\]";
            var arrMatch = Regex.Match(json, arrPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!arrMatch.Success) return result;

            var inner = arrMatch.Groups["arr"].Value;
            foreach (Match m in Regex.Matches(inner, "\"(?<s>[^\"]*)\"", RegexOptions.CultureInvariant))
            {
                var s = m.Groups["s"].Value;
                if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
            }
            return result;
        }

        private static bool TryExtractInt(string json, string key, out int value)
        {
            value = 0;
            var pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*(?<n>-?[0-9]+)";
            var m = Regex.Match(json, pattern, RegexOptions.CultureInvariant);
            if (!m.Success) return false;
            return int.TryParse(m.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static CloakColor[] ParseHexColors(params string[] hexes)
        {
            var list = new List<CloakColor>(hexes.Length);
            foreach (var hex in hexes)
            {
                if (CloakColor.TryParse(hex, out var c))
                    list.Add(c);
            }

            return list.ToArray();
        }

        private static void SetSources(IList<CloakColor> colors)
        {
            // Reset everything to "far away" before filling so old entries can't leak through.
            for (var i = 0; i < SrcColors.Length; i++)
                SrcColors[i] = new Vector4(10f, 10f, 10f, 1f);

            var count = Math.Min(colors.Count, SrcColors.Length);
            for (var i = 0; i < count; i++)
            {
                var c = colors[i].ToUnityColor();
                SrcColors[i] = new Vector4(c.r, c.g, c.b, 1f);
            }
            SrcCount = count;

            if (colors.Count > SrcColors.Length)
            {
                Log.Warn($"cloak_palette.json: only the first {SrcColors.Length} cloakColors are used " +
                         $"(found {colors.Count}). Increase MaxCloakColors in the shader to lift this.");
            }
        }

        private static void SetAvoidSources(IList<CloakColor> colors)
        {
            for (var i = 0; i < AvoidColors.Length; i++)
                AvoidColors[i] = new Vector4(10f, 10f, 10f, 1f);

            var count = Math.Min(colors.Count, AvoidColors.Length);
            for (var i = 0; i < count; i++)
            {
                var c = colors[i].ToUnityColor();
                AvoidColors[i] = new Vector4(c.r, c.g, c.b, 1f);
            }

            AvoidCount = count;

            if (colors.Count > AvoidColors.Length)
            {
                Log.Warn($"cloak_palette.json: only the first {AvoidColors.Length} avoidColors are used " +
                         $"(found {colors.Count}).");
            }
        }

        private static List<CloakColor> ExtractHexArray(string json, string key)
        {
            var result = new List<CloakColor>();
            var arrPattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*\\[(?<arr>[^\\]]*)\\]";
            var arrMatch = Regex.Match(json, arrPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!arrMatch.Success) return result;

            var inner = arrMatch.Groups["arr"].Value;
            foreach (Match hexMatch in Regex.Matches(inner, "\"(?<h>#?[0-9a-fA-F]{6})\"", RegexOptions.CultureInvariant))
            {
                var hex = hexMatch.Groups["h"].Value;
                if (!hex.StartsWith("#", StringComparison.Ordinal)) hex = "#" + hex;
                if (CloakColor.TryParse(hex, out var color))
                    result.Add(color);
            }
            return result;
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
    }
}
