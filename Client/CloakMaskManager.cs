using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Per-atlas R masks under <c>CloakMasks/&lt;tk2d collection name&gt;/&lt;MainTex.name&gt;.png</c>
    /// next to the plugin DLL (legacy flat <c>CloakMasks/&lt;MainTex.name&gt;.png</c> is still loaded if present).
    /// Weights drive the in-game cloak shader; missing files mean that atlas is left untouched.
    /// </summary>
    internal static class CloakMaskManager
    {
        /// <summary>Canonical key: <c>CloakMasks/&lt;collection&gt;/&lt;texture&gt;.png</c> (full path).</summary>
        private static readonly Dictionary<string, Texture2D> ByMaskFilePath = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> MissingMaskFilePaths = new(StringComparer.OrdinalIgnoreCase);
        private static string? _pluginDir;
        private static Texture2D? _blackWeight1x1;
        private static readonly HashSet<string> DumpedOriginalPaths = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>R=0 mask so the tint shader leaves pixels unchanged when no real mask exists.</summary>
        public static Texture2D BlackWeightMask
        {
            get
            {
                if (_blackWeight1x1 != null) return _blackWeight1x1;
                var t = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                t.SetPixel(0, 0, Color.clear);
                t.Apply(false, true);
                t.name = "CloakMask:BlackFallback";
                return _blackWeight1x1 = t;
            }
        }

        /// <summary>Drop cached <see cref="Texture2D"/> masks after <see cref="CloakPaletteConfig.Load"/>.</summary>
        public static void OnPaletteReloaded()
        {
            foreach (var tex in ByMaskFilePath.Values)
            {
                if (tex != null && tex != _blackWeight1x1)
                    UnityEngine.Object.Destroy(tex);
            }

            ByMaskFilePath.Clear();
            MissingMaskFilePaths.Clear();
            DumpedOriginalPaths.Clear();

            // Mask Texture2D instance IDs change after a reload; force every memoized renderer
            // to take the slow path on its next Apply so it picks up the new (or now-missing) mask.
            CloakMaterialApplier.InvalidateAll();
        }

        private static string PluginDir
        {
            get
            {
                if (_pluginDir != null) return _pluginDir;
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                _pluginDir = string.IsNullOrEmpty(dir) ? string.Empty : dir;
                return _pluginDir;
            }
        }

        /// <summary>
        /// Returns a linear <see cref="Texture2D"/> mask for <paramref name="mainTex"/> if one exists on disk.
        /// Missing or invalid masks return <c>false</c> so callers can leave that renderer untouched.
        /// </summary>
        /// <param name="tk2dCollectionName"><c>tk2dSprite.Collection.name</c> for on-disk layout (sanitized folder).</param>
        public static bool TryGetMaskForMainTexture(Texture? mainTex, string? tk2dCollectionName, out Texture2D mask)
        {
            mask = BlackWeightMask;

            if (mainTex == null || string.IsNullOrEmpty(PluginDir))
                return false;

            if (mainTex.width <= 0 || mainTex.height <= 0)
                return false;

            var collectionStem = CloakDiskNames.CollectionFolder(tk2dCollectionName);
            var texStem = CloakDiskNames.SanitizeFileStem(
                string.IsNullOrEmpty(mainTex.name) ? $"tex_{mainTex.GetInstanceID()}" : mainTex.name);

            var masksDir = Path.Combine(PluginDir, "CloakMasks");
            var collectionDir = Path.Combine(masksDir, collectionStem);
            var preferredPath = Path.Combine(collectionDir, $"{texStem}.png");
            var legacyFlatPath = Path.Combine(masksDir, $"{texStem}.png");

            if (ByMaskFilePath.TryGetValue(preferredPath, out var cached) && cached != null)
            {
                mask = cached;
                return true;
            }

            if (MissingMaskFilePaths.Contains(preferredPath))
                return false;

            Texture2D? maskTex = null;
            string? resolvedPath = null;

            if (File.Exists(preferredPath))
                resolvedPath = preferredPath;
            else if (File.Exists(legacyFlatPath))
                resolvedPath = legacyFlatPath;

            if (resolvedPath != null)
            {
                maskTex = LoadMaskFromDisk(resolvedPath);
                if (maskTex != null &&
                    (maskTex.width != mainTex.width || maskTex.height != mainTex.height))
                {
                    Log.Warn($"[CloakMasks] '{resolvedPath}' size {maskTex.width}x{maskTex.height} does not match atlas " +
                             $"'{mainTex.name}' ({mainTex.width}x{mainTex.height}). Using zero mask (no recolor) for this atlas.");
                    UnityEngine.Object.Destroy(maskTex);
                    maskTex = null;
                }
            }

            if (maskTex == null)
            {
                MissingMaskFilePaths.Add(preferredPath);
                return false;
            }

            maskTex.wrapMode = TextureWrapMode.Clamp;
            maskTex.filterMode = FilterMode.Bilinear;
            maskTex.name = $"CloakMask:{collectionStem}/{texStem}";
            ByMaskFilePath[preferredPath] = maskTex;

            var maskPathForDump = resolvedPath ?? preferredPath;
            MaybeDumpDiscoveredSourceTexture(mainTex, maskPathForDump);

            mask = maskTex;
            return true;
        }

        /// <summary>
        /// Writes <c>&lt;mask-stem&gt;-original.png</c> beside the mask.
        /// </summary>
        private static void MaybeDumpDiscoveredSourceTexture(Texture mainTex, string siblingMaskPath)
        {
            if (!CloakPaletteConfig.DumpDiscoveredTextures)
                return;

            var dir = Path.GetDirectoryName(siblingMaskPath);
            var maskStem = Path.GetFileNameWithoutExtension(siblingMaskPath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(maskStem))
                return;

            var outPath = Path.Combine(dir, $"{maskStem}-original.png");
            if (DumpedOriginalPaths.Contains(outPath) || File.Exists(outPath))
                return;

            var w = mainTex.width;
            var h = mainTex.height;
            if (w <= 0 || h <= 0)
                return;

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(mainTex, rt);
                RenderTexture.active = rt;
                var dst = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                dst.Apply(false, false);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(outPath, dst.EncodeToPNG());
                DumpedOriginalPaths.Add(outPath);
                Log.Info($"[CloakMasks] dumpDiscoveredTextures: wrote '{outPath}'.");
                UnityEngine.Object.Destroy(dst);
            }
            catch (Exception ex)
            {
                Log.Warn($"[CloakMasks] dumpDiscoveredTextures: could not write '{outPath}': {ex.Message}");
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static Texture2D? LoadMaskFromDisk(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (!tex.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }

                return tex;
            }
            catch (Exception ex)
            {
                Log.Warn($"[CloakMasks] Failed to read '{path}': {ex.Message}");
                return null;
            }
        }

    }
}
