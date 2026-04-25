using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Optional per-atlas R masks under <c>CloakMasks/&lt;tk2d collection name&gt;/&lt;MainTex.name&gt;.png</c>
    /// next to the plugin DLL (legacy flat <c>CloakMasks/&lt;MainTex.name&gt;.png</c> is still loaded if present).
    /// When <see cref="CloakPaletteConfig.UseCloakMaskTextures"/> is true, weights come from the PNG
    /// (same 0–1 weight as the procedural cloak+avoid mask with <c>_Strength = 1</c> in the shader).
    /// Missing files are generated once via GPU readback and saved for hand-editing.
    /// </summary>
    internal static class CloakMaskManager
    {
        /// <summary>Canonical key: <c>CloakMasks/&lt;collection&gt;/&lt;texture&gt;.png</c> (full path).</summary>
        private static readonly Dictionary<string, Texture2D> ByMaskFilePath = new(StringComparer.OrdinalIgnoreCase);
        private static string? _pluginDir;
        private static bool _warnedMissingBakeShader;

        /// <summary>Drop cached <see cref="Texture2D"/> masks after <see cref="CloakPaletteConfig.Load"/>.</summary>
        public static void OnPaletteReloaded()
        {
            foreach (var tex in ByMaskFilePath.Values)
            {
                if (tex != null)
                    UnityEngine.Object.Destroy(tex);
            }

            ByMaskFilePath.Clear();
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
        /// Returns a linear <see cref="Texture2D"/> mask for <paramref name="mainTex"/> when file or cache exists;
        /// <paramref name="useMask"/> is false when falling back to procedural shader math only.
        /// </summary>
        /// <param name="tk2dCollectionName"><c>tk2dSprite.Collection.name</c> for on-disk layout (sanitized folder).</param>
        public static Texture2D? GetMaskForMainTexture(Texture? mainTex, string? tk2dCollectionName, out bool useMask)
        {
            useMask = false;
            if (!CloakPaletteConfig.UseCloakMaskTextures || mainTex == null || string.IsNullOrEmpty(PluginDir))
                return null;

            if (mainTex.width <= 0 || mainTex.height <= 0)
                return null;

            var collectionStem = CloakDiskNames.CollectionFolder(tk2dCollectionName);
            var texStem = CloakDiskNames.SanitizeFileStem(
                string.IsNullOrEmpty(mainTex.name) ? $"tex_{mainTex.GetInstanceID()}" : mainTex.name);

            var masksDir = Path.Combine(PluginDir, "CloakMasks");
            try
            {
                Directory.CreateDirectory(masksDir);
            }
            catch (Exception ex)
            {
                Log.Warn($"[CloakMasks] Could not create '{masksDir}': {ex.Message}");
                return null;
            }

            var collectionDir = Path.Combine(masksDir, collectionStem);
            var preferredPath = Path.Combine(collectionDir, $"{texStem}.png");
            var legacyFlatPath = Path.Combine(masksDir, $"{texStem}.png");

            if (ByMaskFilePath.TryGetValue(preferredPath, out var cached) && cached != null)
            {
                useMask = true;
                return cached;
            }

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
                             $"'{mainTex.name}' ({mainTex.width}x{mainTex.height}). Using procedural mask for this atlas.");
                    UnityEngine.Object.Destroy(maskTex);
                    maskTex = null;
                }
            }

            if (maskTex == null)
            {
                maskTex = GenerateProceduralMask(mainTex);
                if (maskTex == null)
                    return null;

                try
                {
                    Directory.CreateDirectory(collectionDir);
                    File.WriteAllBytes(preferredPath, maskTex.EncodeToPNG());
                    Log.Info($"[CloakMasks] Wrote auto-generated mask '{preferredPath}' (collection '{tk2dCollectionName ?? "(none)"}', atlas '{mainTex.name}'). " +
                             "Edit the PNG (R channel = recolor weight) and restart to pick up changes.");
                }
                catch (Exception ex)
                {
                    Log.Warn($"[CloakMasks] Could not save '{preferredPath}': {ex.Message}. Using in-memory mask for this session.");
                }
            }

            maskTex.wrapMode = TextureWrapMode.Clamp;
            maskTex.filterMode = FilterMode.Bilinear;
            maskTex.name = $"CloakMask:{collectionStem}/{texStem}";
            ByMaskFilePath[preferredPath] = maskTex;
            useMask = true;
            return maskTex;
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

        /// <summary>
        /// Bakes the procedural mask using <see cref="CloakShaderManager.MaskBakeShader"/> (same math as the in-game
        /// cloak shader). CPU readback was unreliable for compressed atlases; this path matches the GPU exactly.
        /// </summary>
        private static Texture2D? GenerateProceduralMask(Texture mainTex)
        {
            if (CloakPaletteConfig.SrcCount <= 0)
            {
                Log.Warn("[CloakMasks] cloakColors produced SrcCount=0 — cannot generate a mask; fix cloak_palette.json.");
                return null;
            }

            var bakeShader = CloakShaderManager.MaskBakeShader;
            if (bakeShader == null)
            {
                if (!_warnedMissingBakeShader)
                {
                    _warnedMissingBakeShader = true;
                    Log.Warn("[CloakMasks] CloakMaskBake shader is missing from the embedded bundle — cannot write mask PNGs. " +
                             "Rebuild cloakshader.bundle in Unity (include CloakMaskBake.shader; see Shaders/README.md), " +
                             "copy to HornetCloakColor/Resources/cloakshader.bundle, then rebuild the mod DLL.");
                }

                return null;
            }

            var w = mainTex.width;
            var h = mainTex.height;
            if (w <= 0 || h <= 0) return null;

            var mat = new Material(bakeShader);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            try
            {
                mat.SetVectorArray(CloakShaderManager.SrcColorsId, CloakPaletteConfig.SrcColors);
                mat.SetVectorArray(CloakShaderManager.AvoidColorsId, CloakPaletteConfig.AvoidColors);
                mat.SetFloat(CloakShaderManager.MatchRadiusId, CloakPaletteConfig.MatchRadius);
                mat.SetFloat(CloakShaderManager.AvoidMatchRadiusId, CloakPaletteConfig.AvoidMatchRadius);
                mat.SetFloat(CloakShaderManager.StrengthId, 1f);

                Graphics.Blit(mainTex, rt, mat);
                RenderTexture.active = rt;
                var dst = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                dst.Apply(false, false);
                return dst;
            }
            catch (Exception ex)
            {
                Log.Warn($"[CloakMasks] GPU mask bake failed for '{mainTex.name}': {ex.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(mat);
            }
        }
    }
}
