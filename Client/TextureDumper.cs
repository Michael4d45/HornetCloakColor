using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Diagnostic helper: dumps a PNG of every distinct Hornet atlas the mod sees to
    /// <c>BepInEx/plugins/HornetCloakColor/TextureDumps/</c> so you can correlate
    /// runtime <see cref="Texture.GetInstanceID"/>s with actual sprite sheets and
    /// sample colors for <c>cloakColors</c>.
    ///
    /// Disabled by default. Set <c>"dumpDiscoveredTextures": true</c> in
    /// <c>cloak_palette.json</c> to enable. Each texture is dumped at most once per
    /// session (deduped by instance ID).
    ///
    /// Implementation note: most game atlases are <c>Texture.isReadable == false</c>,
    /// so we can't call <see cref="Texture2D.GetPixels"/> directly. We <see cref="Graphics.Blit"/>
    /// the source through a temporary <see cref="RenderTexture"/> and read it back, which
    /// works on any non-compressed format.
    /// </summary>
    internal static class TextureDumper
    {
        private static readonly HashSet<int> _dumped = new();
        private static string? _dumpDir;
        private static bool _warnedDir;

        /// <summary>
        /// Dump <paramref name="tex"/> to disk if it's a Texture2D, dumping is enabled,
        /// and we haven't already dumped this instance ID.
        /// </summary>
        /// <param name="source">Short tag for where we found it (e.g. "hero").</param>
        public static void TryDump(Texture? tex, string source)
        {
            if (!CloakPaletteConfig.DumpDiscoveredTextures) return;
            if (tex == null) return;

            var id = tex.GetInstanceID();
            if (!_dumped.Add(id)) return;

            var dir = EnsureDumpDir();
            if (dir == null) return;

            Texture2D? readable = null;
            try
            {
                readable = MakeReadable(tex);
                if (readable == null)
                {
                    Log.Warn($"[Dumper] Could not blit '{tex.name}' (id={id}, {tex.width}x{tex.height}) to a readable texture.");
                    return;
                }

                var bytes = readable.EncodeToPNG();
                if (bytes == null || bytes.Length == 0)
                {
                    Log.Warn($"[Dumper] EncodeToPNG returned no bytes for '{tex.name}' (id={id}).");
                    return;
                }

                var fileName = $"{Sanitize(tex.name)}_id{id}_{tex.width}x{tex.height}_{Sanitize(source)}.png";
                var path = Path.Combine(dir, fileName);
                File.WriteAllBytes(path, bytes);
                Log.Info($"[Dumper] Wrote {path} ({bytes.Length:N0} bytes).");
            }
            catch (Exception ex)
            {
                Log.Warn($"[Dumper] Failed to dump '{tex.name}' (id={id}): {ex.Message}");
            }
            finally
            {
                if (readable != null) UnityEngine.Object.Destroy(readable);
            }
        }

        private static Texture2D? MakeReadable(Texture src)
        {
            var w = src.width;
            var h = src.height;
            if (w <= 0 || h <= 0) return null;

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;

                var dst = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false);
                dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                dst.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                return dst;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static string? EnsureDumpDir()
        {
            if (_dumpDir != null) return _dumpDir;
            try
            {
                var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(asmDir))
                {
                    if (!_warnedDir) { Log.Warn("[Dumper] Could not resolve assembly directory."); _warnedDir = true; }
                    return null;
                }

                _dumpDir = Path.Combine(asmDir, "TextureDumps");
                Directory.CreateDirectory(_dumpDir);
                return _dumpDir;
            }
            catch (Exception ex)
            {
                if (!_warnedDir) { Log.Warn($"[Dumper] Could not create dump folder: {ex.Message}"); _warnedDir = true; }
                return null;
            }
        }

        private static string Sanitize(string? name)
        {
            if (string.IsNullOrEmpty(name)) return "tex";
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", name!.Split(invalid));
        }
    }
}
