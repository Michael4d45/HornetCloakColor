using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Diagnostic helper: dumps PNGs of textures seen on the local hero and writes
    /// <c>texture_dump_manifest.json</c> classifying each atlas against the same allowlist
    /// rules as <see cref="CloakSceneScanner"/> plus whether it sits on the material's
    /// main-texture slot (what the cloak shader samples).
    ///
    /// Writes under the plugin: <c>TextureDumps/&lt;tk2d collection name&gt;/&lt;Texture.name&gt;.png</c>
    /// (same layout as <see cref="CloakMaskManager"/> masks under <c>CloakMasks/</c>) plus
    /// <c>texture_dump_manifest.json</c> in <c>TextureDumps</c>.
    ///
    /// Disabled by default. Set <c>"dumpDiscoveredTextures": true</c> in
    /// <c>cloak_palette.json</c> to enable. Each distinct <see cref="Texture.GetInstanceID"/>
    /// is dumped at most once per session; manifest rows merge usages across frames.
    ///
    /// Implementation note: most game atlases are <c>Texture.isReadable == false</c>,
    /// so we can't call <see cref="Texture2D.GetPixels"/> directly. We <see cref="Graphics.Blit"/>
    /// the source through a temporary <see cref="RenderTexture"/> and read it back, which
    /// works on any non-compressed format.
    /// </summary>
    internal static class TextureDumper
    {
        private static readonly HashSet<int> _dumpedPngIds = new();
        private static readonly Dictionary<string, int> _pngBaseNameToTexId = new(StringComparer.Ordinal);
        private static string? _dumpRootDir;
        private static bool _warnedDir;

        private static readonly Dictionary<int, ManifestEntry> _manifestByTexId = new();
        private static bool _manifestDirty;
        private static int _lastLoggedManifestTextureCount = -1;

        private sealed class ManifestEntry
        {
            public readonly HashSet<string> UsageKeySeen = new();
            public readonly List<UsageRow> Usages = new();
            public readonly HashSet<string> CollectionSubfoldersWritten = new(StringComparer.OrdinalIgnoreCase);
            public string? PngFileName;
            public string UnityName = "";
            public int Width;
            public int Height;
            public bool AnySceneScanAllowlistMatch;
            public bool AnyMainTextureSlotOnHero;
        }

        private readonly struct UsageRow
        {
            public readonly string RendererPath;
            public readonly string MaterialName;
            public readonly string ShaderProperty;
            public readonly string Tk2dCollectionName;
            public readonly bool IsMainTextureSlot;
            public readonly bool SceneScanAllowlistMatch;

            public UsageRow(
                string rendererPath,
                string materialName,
                string shaderProperty,
                string tk2dCollectionName,
                bool isMainTextureSlot,
                bool sceneScanAllowlistMatch)
            {
                RendererPath = rendererPath;
                MaterialName = materialName;
                ShaderProperty = shaderProperty;
                Tk2dCollectionName = tk2dCollectionName;
                IsMainTextureSlot = isMainTextureSlot;
                SceneScanAllowlistMatch = sceneScanAllowlistMatch;
            }
        }

        /// <summary>
        /// Walk every texture property on <paramref name="meshRenderer"/>'s shared materials,
        /// record manifest rows, dump PNGs once per texture id, and refresh
        /// <see cref="HornetTextureRegistry"/> for newly seen ids.
        /// </summary>
        public static void CollectHeroHierarchyTextures(MeshRenderer? meshRenderer)
        {
            if (!CloakPaletteConfig.DumpDiscoveredTextures) return;
            if (meshRenderer == null) return;

            var rendererPath = CloakSceneScanner.FormatTransformPath(meshRenderer.transform);
            var sprite = meshRenderer.GetComponent<tk2dSprite>();
            var collectionName = sprite != null && sprite.Collection != null
                ? (sprite.Collection.name ?? string.Empty)
                : string.Empty;

            var mats = meshRenderer.sharedMaterials;
            if (mats == null || mats.Length == 0) return;

            foreach (var mat in mats)
            {
                if (mat == null || mat.shader == null) continue;

                string[] propNames;
                try
                {
                    propNames = mat.GetTexturePropertyNames();
                }
                catch (Exception ex)
                {
                    Log.Warn($"[Dumper] GetTexturePropertyNames failed for material '{mat.name}': {ex.Message}");
                    continue;
                }

                if (propNames == null || propNames.Length == 0) continue;

                var mainTex = mat.mainTexture;

                foreach (var prop in propNames)
                {
                    if (string.IsNullOrEmpty(prop)) continue;
                    Texture? tex;
                    try
                    {
                        tex = mat.GetTexture(prop);
                    }
                    catch
                    {
                        continue;
                    }

                    if (tex == null) continue;

                    var texName = tex.name ?? string.Empty;
                    var allow = CloakPaletteConfig.MatchesSceneScanAllowlist(collectionName);
                    var isMainSlot = mainTex != null && tex.GetInstanceID() == mainTex.GetInstanceID();

                    RecordUsage(tex, rendererPath, mat.name, prop, collectionName, isMainSlot, allow);
                }
            }

            FlushManifestIfDirty();
        }

        private static void RecordUsage(
            Texture tex,
            string rendererPath,
            string materialName,
            string shaderProperty,
            string tk2dCollectionName,
            bool isMainTextureSlot,
            bool sceneScanAllowlistMatch)
        {
            var id = tex.GetInstanceID();

            if (!_manifestByTexId.TryGetValue(id, out var entry))
            {
                entry = new ManifestEntry
                {
                    UnityName = tex.name ?? "",
                    Width = tex.width,
                    Height = tex.height,
                };
                _manifestByTexId[id] = entry;
                _ = HornetTextureRegistry.Register(tex);
                TryWritePngOnce(tex, entry, tk2dCollectionName);
            }
            else
            {
                CopyTextureToCollectionIfNeeded(entry, tex, id, tk2dCollectionName);
            }

            var usageKey = $"{rendererPath}\u001f{materialName}\u001f{shaderProperty}\u001f{tk2dCollectionName}\u001f{isMainTextureSlot}\u001f{sceneScanAllowlistMatch}";
            if (!entry.UsageKeySeen.Add(usageKey)) return;

            entry.Usages.Add(new UsageRow(
                rendererPath,
                materialName,
                shaderProperty,
                tk2dCollectionName,
                isMainTextureSlot,
                sceneScanAllowlistMatch));

            if (sceneScanAllowlistMatch) entry.AnySceneScanAllowlistMatch = true;
            if (isMainTextureSlot) entry.AnyMainTextureSlotOnHero = true;

            _manifestDirty = true;
        }

        private static void TryWritePngOnce(Texture tex, ManifestEntry entry, string tk2dCollectionName)
        {
            var id = tex.GetInstanceID();
            if (!_dumpedPngIds.Add(id)) return;

            var dumpRoot = EnsureDumpRootDir();
            if (dumpRoot == null)
            {
                _dumpedPngIds.Remove(id);
                return;
            }

            Texture2D? readable = null;
            try
            {
                readable = TextureReadback.CopyToReadableTexture2D(tex);
                if (readable == null)
                {
                    Log.Warn($"[Dumper] Could not blit '{tex.name}' (id={id}, {tex.width}x{tex.height}) to a readable texture.");
                    _dumpedPngIds.Remove(id);
                    return;
                }

                var bytes = readable.EncodeToPNG();
                if (bytes == null || bytes.Length == 0)
                {
                    Log.Warn($"[Dumper] EncodeToPNG returned no bytes for '{tex.name}' (id={id}).");
                    _dumpedPngIds.Remove(id);
                    return;
                }

                var fileName = BuildPackTexturePngFileName(tex, id);
                var subKey = CloakDiskNames.CollectionFolder(tk2dCollectionName);
                var collDir = Path.Combine(dumpRoot, subKey);
                Directory.CreateDirectory(collDir);
                var collPath = Path.Combine(collDir, fileName);
                File.WriteAllBytes(collPath, bytes);
                entry.PngFileName = Path.Combine(subKey, fileName);
                entry.CollectionSubfoldersWritten.Add(subKey);

                Log.Info($"[Dumper] Wrote {collPath} ({bytes.Length:N0} bytes).");
            }
            catch (Exception ex)
            {
                Log.Warn($"[Dumper] Failed to dump '{tex.name}' (id={id}): {ex.Message}");
                _dumpedPngIds.Remove(id);
            }
            finally
            {
                if (readable != null) UnityEngine.Object.Destroy(readable);
            }
        }

        private static void CopyTextureToCollectionIfNeeded(ManifestEntry entry, Texture tex, int id, string tk2dCollectionName)
        {
            if (string.IsNullOrEmpty(entry.PngFileName)) return;
            var subKey = CloakDiskNames.CollectionFolder(tk2dCollectionName);
            if (!entry.CollectionSubfoldersWritten.Add(subKey)) return;

            var dumpRoot = EnsureDumpRootDir();
            if (dumpRoot == null) return;

            var src = Path.Combine(dumpRoot, entry.PngFileName);
            if (!File.Exists(src))
            {
                entry.CollectionSubfoldersWritten.Remove(subKey);
                return;
            }

            try
            {
                var destDir = Path.Combine(dumpRoot, subKey);
                Directory.CreateDirectory(destDir);
                var dest = Path.Combine(destDir, Path.GetFileName(src));
                File.Copy(src, dest, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Warn($"[Dumper] Copy to collection folder '{subKey}' for '{tex.name}' (id={id}): {ex.Message}");
                entry.CollectionSubfoldersWritten.Remove(subKey);
            }
        }

        private static string BuildPackTexturePngFileName(Texture tex, int id)
        {
            var baseName = CloakDiskNames.SanitizeFileStem(tex.name ?? string.Empty);
            if (string.IsNullOrEmpty(baseName)) baseName = "tex";
            if (_pngBaseNameToTexId.TryGetValue(baseName, out var seenId) && seenId != id)
                return $"{baseName}_id{id}.png";
            _pngBaseNameToTexId[baseName] = id;
            return baseName + ".png";
        }

        private static void FlushManifestIfDirty()
        {
            if (!_manifestDirty) return;
            _manifestDirty = false;

            var dumpRoot = EnsureDumpRootDir();
            if (dumpRoot == null) return;

            try
            {
                var path = Path.Combine(dumpRoot, "texture_dump_manifest.json");
                File.WriteAllText(path, BuildManifestJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                if (_manifestByTexId.Count != _lastLoggedManifestTextureCount)
                {
                    _lastLoggedManifestTextureCount = _manifestByTexId.Count;
                    Log.Info($"[Dumper] Updated manifest ({_manifestByTexId.Count} texture id(s)): {path}");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[Dumper] Could not write texture_dump_manifest.json: {ex.Message}");
            }
        }

        private static string BuildManifestJson()
        {
            var utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var sb = new StringBuilder(8192);
            sb.Append("{\n");
            sb.Append("  \"generatedUtc\": \"").Append(JsonEscape(utc)).Append("\",\n");
            sb.Append("  \"legend\": {\n");
            sb.Append("    \"sceneScanAllowlistMatch\": \"True if the tk2d collection name matches CloakSceneScanner allowlist rules (collectionNameContains substrings).\",\n");
            sb.Append("    \"anyMainTextureSlotOnHero\": \"True if this texture is the Material.mainTexture on at least one scanned MeshRenderer under the player — the cloak shader recolors using that slot.\",\n");
            sb.Append("    \"heuristicLikelyCloakAtlas\": \"anyMainTextureSlotOnHero && sceneScanAllowlistMatch on at least one usage — strong signal; still verify visually from the PNG.\",\n");
            sb.Append("    \"falsePositiveHint\": \"Allowlist true on a sheet you consider non-cloak (e.g. weapon UI) — tighten JSON substrings.\",\n");
            sb.Append("    \"falseNegativeHint\": \"Cloak sheet in PNG but allowlist never true — orphan detached sprites would not get scene-scanner tint until allowlist is fixed.\",\n");
            sb.Append("    \"dumpPngFileName\": \"Relative to TextureDumps: <collection>/<Texture.name>.png — mirrors CloakMasks layout.\"\n");
            sb.Append("  },\n");
            sb.Append("  \"textures\": [\n");

            var first = true;
            foreach (var kv in _manifestByTexId.OrderBy(static x => x.Key))
            {
                var id = kv.Key;
                var e = kv.Value;
                var unityName = e.UnityName;
                var w = e.Width;
                var h = e.Height;
                var png = e.PngFileName ?? "";
                var anyMain = e.AnyMainTextureSlotOnHero;
                var anyAllow = e.AnySceneScanAllowlistMatch;
                var heuristic = anyMain && anyAllow;

                if (!first) sb.Append(",\n");
                first = false;

                sb.Append("    {\n");
                sb.Append("      \"instanceId\": ").Append(id.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("      \"unityName\": \"").Append(JsonEscape(unityName)).Append("\",\n");
                sb.Append("      \"width\": ").Append(w.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("      \"height\": ").Append(h.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("      \"dumpPngFileName\": \"").Append(JsonEscape(png)).Append("\",\n");
                sb.Append("      \"anySceneScanAllowlistMatch\": ").Append(anyAllow ? "true" : "false").Append(",\n");
                sb.Append("      \"anyMainTextureSlotOnHero\": ").Append(anyMain ? "true" : "false").Append(",\n");
                sb.Append("      \"heuristicLikelyCloakAtlas\": ").Append(heuristic ? "true" : "false").Append(",\n");
                sb.Append("      \"usages\": [\n");

                for (var i = 0; i < e.Usages.Count; i++)
                {
                    var u = e.Usages[i];
                    if (i > 0) sb.Append(",\n");
                    sb.Append("        {\n");
                    sb.Append("          \"rendererPath\": \"").Append(JsonEscape(u.RendererPath)).Append("\",\n");
                    sb.Append("          \"materialName\": \"").Append(JsonEscape(u.MaterialName)).Append("\",\n");
                    sb.Append("          \"shaderProperty\": \"").Append(JsonEscape(u.ShaderProperty)).Append("\",\n");
                    sb.Append("          \"tk2dCollectionName\": \"").Append(JsonEscape(u.Tk2dCollectionName)).Append("\",\n");
                    sb.Append("          \"isMainTextureSlot\": ").Append(u.IsMainTextureSlot ? "true" : "false").Append(",\n");
                    sb.Append("          \"sceneScanAllowlistMatch\": ").Append(u.SceneScanAllowlistMatch ? "true" : "false").Append("\n");
                    sb.Append("        }");
                }

                sb.Append("\n      ]\n");
                sb.Append("    }");
            }

            sb.Append("\n  ]\n}\n");
            return sb.ToString();
        }

        private static string? EnsureDumpRootDir()
        {
            if (_dumpRootDir != null) return _dumpRootDir;
            try
            {
                var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(asmDir))
                {
                    if (!_warnedDir) { Log.Warn("[Dumper] Could not resolve assembly directory."); _warnedDir = true; }
                    return null;
                }

                _dumpRootDir = Path.Combine(asmDir, "TextureDumps");
                Directory.CreateDirectory(_dumpRootDir);
                return _dumpRootDir;
            }
            catch (Exception ex)
            {
                if (!_warnedDir) { Log.Warn($"[Dumper] Could not create dump folder: {ex.Message}"); _warnedDir = true; }
                return null;
            }
        }

        private static string JsonEscape(string s)
        {
            return s
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\t", "\\t", StringComparison.Ordinal);
        }
    }
}
