using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Per-atlas R masks under <c>CloakMasks/&lt;raw tk2d collection name&gt;/&lt;MainTex.name&gt;.png</c>,
    /// then compatibility alias (e.g. <c>Player Prefab</c> → <c>Knight</c>), then legacy flat
    /// <c>CloakMasks/&lt;MainTex.name&gt;.png</c>. No heuristic searching — paths must match exactly.
    /// Weights drive the in-game cloak shader; missing files mean that atlas is left untouched.
    /// </summary>
    internal static class CloakMaskManager
    {
        /// <summary>Canonical key: <c>CloakMasks/&lt;collection&gt;/&lt;texture&gt;.png</c> (full path).</summary>
        private static readonly Dictionary<string, Texture2D> ByMaskFilePath = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Hot-path cache keyed by <c>(texture instance id, collection folder stem)</c>.
        /// Mask files live under a collection-specific folder; caching only by texture id caused
        /// the first frames to resolve <see cref="CloakDiskNames.NoCollectionFolder"/> before
        /// <c>tk2dSprite.Collection</c> was bound, poisoning lookups when the real folder later
        /// contained the PNG.
        /// <para><c>null</c> means we resolved that there is no mask for that path.</para>
        /// </summary>
        private static readonly Dictionary<string, Texture2D?> ByTextureCollectionKey = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Texture2D?> ByTexture2DMaskName = new(StringComparer.OrdinalIgnoreCase);

        private static string? _pluginDir;
        private static Texture2D? _blackWeight1x1;
        private static readonly HashSet<string> DumpedOriginalPaths = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> DumpedTemplatePaths = new(StringComparer.OrdinalIgnoreCase);

        private static string TextureCollectionCacheKey(int textureInstanceId, string collectionStem) =>
            string.Concat(textureInstanceId.ToString(), "\x1f", collectionStem);

        /// <summary>
        /// True when we have cached a definitive "no mask file" result for this atlas binding.
        /// Used to avoid re-running full <see cref="CloakMaterialApplier.Apply"/> every frame on
        /// unrelated tk2d meshes while still retrying when the cache was never populated.
        /// </summary>
        internal static bool IsMaskConfirmedAbsentForBinding(Texture mainTex, string? tk2dCollectionName)
        {
            if (mainTex == null || mainTex.width <= 0)
                return false;

            var collectionStem = CloakDiskNames.CollectionFolder(tk2dCollectionName);
            var key = TextureCollectionCacheKey(mainTex.GetInstanceID(), collectionStem);
            return ByTextureCollectionKey.TryGetValue(key, out var v) && v == null;
        }

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
            ByTextureCollectionKey.Clear();
            ByTexture2DMaskName.Clear();
            DumpedOriginalPaths.Clear();
            DumpedTemplatePaths.Clear();
            MaskDiagLoggedCompositeKeys.Clear();

            // Mask Texture2D instance IDs change after a reload; force every memoized renderer
            // to take the slow path on its next Apply so it picks up the new (or now-missing) mask.
            CloakMaterialApplier.InvalidateAll();
        }

        /// <summary>
        /// SpriteRenderer fallback masks under <c>CloakMasks/Texture2D/&lt;texture-or-sprite&gt;.png</c>.
        /// Used for non-tk2d one-off animation textures such as diving-bell bench-grab frames.
        /// </summary>
        public static bool TryGetTexture2DMask(Texture? texture, string? spriteName, out Texture2D mask)
        {
            mask = BlackWeightMask;

            if (string.IsNullOrEmpty(PluginDir))
                return false;

            var textureName = texture != null ? texture.name : null;
            if (TryGetTexture2DMaskByName(textureName, texture, out mask))
                return true;

            if (!string.Equals(spriteName, textureName, StringComparison.Ordinal)
                && TryGetTexture2DMaskByName(spriteName, texture, out mask))
                return true;

            return false;
        }

        private static bool TryGetTexture2DMaskByName(string? name, Texture? referenceTexture, out Texture2D mask)
        {
            mask = BlackWeightMask;
            if (string.IsNullOrWhiteSpace(name)) return false;

            var stem = CloakDiskNames.SanitizeFileStem(name);
            var path = Path.Combine(PluginDir, "CloakMasks", "Texture2D", $"{stem}.png");

            if (ByTexture2DMaskName.TryGetValue(path, out var cached))
            {
                if (cached == null) return false;
                mask = cached;
                return true;
            }

            if (!File.Exists(path))
            {
                ByTexture2DMaskName[path] = null;
                return false;
            }

            var maskTex = LoadMaskFromDisk(path);
            if (maskTex == null)
            {
                ByTexture2DMaskName[path] = null;
                return false;
            }

            if (referenceTexture != null &&
                (maskTex.width != referenceTexture.width || maskTex.height != referenceTexture.height))
            {
                Log.Warn($"[CloakMasks] '{path}' size {maskTex.width}x{maskTex.height} does not match texture " +
                         $"'{referenceTexture.name}' ({referenceTexture.width}x{referenceTexture.height}). Ignoring this SpriteRenderer mask.");
                UnityEngine.Object.Destroy(maskTex);
                ByTexture2DMaskName[path] = null;
                return false;
            }

            maskTex.wrapMode = TextureWrapMode.Clamp;
            maskTex.filterMode = FilterMode.Bilinear;
            maskTex.name = $"CloakMask:Texture2D/{stem}";
            ByTexture2DMaskName[path] = maskTex;

            if (referenceTexture != null)
                MaybeDumpDiscoveredTextureFiles(referenceTexture, path, createEmptyMask: false);

            mask = maskTex;
            return true;
        }

        private static readonly HashSet<string> MaskDiagLoggedCompositeKeys = new(StringComparer.Ordinal);

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
        /// <param name="tk2dCollectionName"><c>tk2dSprite.Collection.name</c> (raw); on-disk layout tries this folder first, then compatibility aliases.</param>
        public static bool TryGetMaskForMainTexture(Texture? mainTex, string? tk2dCollectionName, out Texture2D mask)
        {
            mask = BlackWeightMask;

            if (mainTex == null || string.IsNullOrEmpty(PluginDir))
                return false;

            if (mainTex.width <= 0 || mainTex.height <= 0)
                return false;

            var collectionRaw = tk2dCollectionName;
            var stemPrimary = CloakDiskNames.CollectionFolder(collectionRaw);
            var stemAlias = CloakDiskNames.CollectionFolder(CloakDiskNames.MaskCollectionNameForLookup(collectionRaw));
            var texId = mainTex.GetInstanceID();
            var compositeKey = TextureCollectionCacheKey(texId, stemPrimary);

            var logDetail = CloakPaletteConfig.LogMaskResolutionDiagnostics
                            && !MaskDiagLoggedCompositeKeys.Contains(compositeKey);

            void FinishMaskDiag()
            {
                if (logDetail)
                    MaskDiagLoggedCompositeKeys.Add(compositeKey);
            }

            // Hot-path: resolved mask or proven absence for this texture + raw collection stem.
            if (ByTextureCollectionKey.TryGetValue(compositeKey, out var cachedById))
            {
                if (cachedById == null)
                {
                    if (logDetail)
                    {
                        Log.Info($"[CloakMasksDiag] cache hit — no mask for compositeKey={compositeKey} tex='{mainTex.name}' id={texId}");
                        FinishMaskDiag();
                    }

                    return false;
                }

                mask = cachedById;
                return true;
            }

            var texStem = CloakDiskNames.SanitizeFileStem(
                string.IsNullOrEmpty(mainTex.name) ? $"tex_{mainTex.GetInstanceID()}" : mainTex.name);

            var masksDir = Path.Combine(PluginDir, "CloakMasks");
            var primaryPath = Path.Combine(masksDir, stemPrimary, $"{texStem}.png");
            var aliasPath = Path.Combine(masksDir, stemAlias, $"{texStem}.png");
            var legacyFlatPath = Path.Combine(masksDir, $"{texStem}.png");

            foreach (var p in EnumerateCandidateMaskPaths(primaryPath, aliasPath, legacyFlatPath))
            {
                if (ByMaskFilePath.TryGetValue(p, out var cached) && cached != null)
                {
                    ByTextureCollectionKey[compositeKey] = cached;
                    mask = cached;
                    return true;
                }
            }

            if (logDetail)
            {
                var rawCol = collectionRaw ?? "(null)";
                var aliasNote = string.Equals(stemPrimary, stemAlias, StringComparison.Ordinal)
                    ? ""
                    : $" stemAlias='{stemAlias}'";
                Log.Info($"[CloakMasksDiag] disk resolve start compositeKey={compositeKey} tex='{mainTex.name}' id={texId} size={mainTex.width}x{mainTex.height} collectionRaw='{rawCol}' stemPrimary='{stemPrimary}'{aliasNote} texStem='{texStem}'");
                Log.Info($"[CloakMasksDiag]   primary exists={File.Exists(primaryPath)} → {primaryPath}");
                if (!string.Equals(primaryPath, aliasPath, StringComparison.OrdinalIgnoreCase))
                    Log.Info($"[CloakMasksDiag]   aliasCompat exists={File.Exists(aliasPath)} → {aliasPath}");
                Log.Info($"[CloakMasksDiag]   legacyFlat exists={File.Exists(legacyFlatPath)} → {legacyFlatPath}");
            }

            Texture2D? maskTex = null;
            string? resolvedPath = null;
            string? resolveBranch = null;

            foreach (var (path, branch) in EnumerateCandidateMaskPathsWithBranch(primaryPath, aliasPath, legacyFlatPath))
            {
                if (!File.Exists(path))
                    continue;
                resolvedPath = path;
                resolveBranch = branch;
                break;
            }

            if (logDetail)
                Log.Info($"[CloakMasksDiag]   branch={resolveBranch ?? "(none)"} resolvedPath={(resolvedPath ?? "(null)")}");

            if (resolvedPath != null)
            {
                maskTex = LoadMaskFromDisk(resolvedPath);
                if (maskTex != null &&
                    (maskTex.width != mainTex.width || maskTex.height != mainTex.height))
                {
                    Log.Warn($"[CloakMasks] '{resolvedPath}' size {maskTex.width}x{maskTex.height} does not match atlas " +
                             $"'{mainTex.name}' ({mainTex.width}x{mainTex.height}). Using zero mask (no recolor) for this atlas.");
                    if (logDetail)
                        Log.Info($"[CloakMasksDiag] RESULT: rejected loaded mask — size mismatch after branch={resolveBranch}");
                    UnityEngine.Object.Destroy(maskTex);
                    maskTex = null;
                }
            }

            if (maskTex == null)
            {
                MaybeDumpDiscoveredTextureFiles(mainTex, primaryPath, createEmptyMask: true);
                ByTextureCollectionKey[compositeKey] = null;
                if (logDetail)
                {
                    Log.Info($"[CloakMasksDiag] RESULT: no mask (texStem={texStem})");
                    FinishMaskDiag();
                }

                return false;
            }

            maskTex.wrapMode = TextureWrapMode.Clamp;
            maskTex.filterMode = FilterMode.Bilinear;
            var folderStem = Path.GetFileName(Path.GetDirectoryName(resolvedPath)) ?? stemPrimary;
            maskTex.name = $"CloakMask:{folderStem}/{texStem}";

            ByMaskFilePath[primaryPath] = maskTex;
            if (!string.Equals(primaryPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
                ByMaskFilePath[resolvedPath!] = maskTex;

            ByTextureCollectionKey[compositeKey] = maskTex;

            MaybeDumpDiscoveredTextureFiles(mainTex, primaryPath, createEmptyMask: false);

            mask = maskTex;

            if (logDetail)
            {
                Log.Info($"[CloakMasksDiag] RESULT: OK mask loaded bytes→texture name={maskTex.name}");
                FinishMaskDiag();
            }

            return true;
        }

        private static IEnumerable<string> EnumerateCandidateMaskPaths(string primaryPath, string aliasPath, string legacyFlatPath)
        {
            yield return primaryPath;
            if (!string.Equals(primaryPath, aliasPath, StringComparison.OrdinalIgnoreCase))
                yield return aliasPath;
            yield return legacyFlatPath;
        }

        private static IEnumerable<(string path, string branch)> EnumerateCandidateMaskPathsWithBranch(
            string primaryPath, string aliasPath, string legacyFlatPath)
        {
            yield return (primaryPath, "primary(raw collection folder)");
            if (!string.Equals(primaryPath, aliasPath, StringComparison.OrdinalIgnoreCase))
                yield return (aliasPath, "aliasCompat(e.g. Knight for Player Prefab)");
            yield return (legacyFlatPath, "legacyFlat(CloakMasks root)");
        }

        /// <summary>
        /// When <c>dumpDiscoveredTextures</c> is enabled, writes the source atlas beside the
        /// desired mask path as <c>&lt;atlas&gt;-original.png</c>. For missing masks, also writes
        /// an empty transparent <c>&lt;atlas&gt;.png</c> template so the user can paint it in-place.
        /// </summary>
        private static void MaybeDumpDiscoveredTextureFiles(Texture mainTex, string maskPath, bool createEmptyMask)
        {
            if (!CloakPaletteConfig.DumpDiscoveredTextures)
                return;

            var dir = Path.GetDirectoryName(maskPath);
            var maskStem = Path.GetFileNameWithoutExtension(maskPath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(maskStem))
                return;

            var w = mainTex.width;
            var h = mainTex.height;
            if (w <= 0 || h <= 0)
                return;

            Directory.CreateDirectory(dir);
            MaybeWriteOriginalTexture(mainTex, Path.Combine(dir, $"{maskStem}-original.png"), w, h);
            if (createEmptyMask)
                MaybeWriteEmptyMask(maskPath, w, h);
        }

        private static void MaybeWriteOriginalTexture(Texture mainTex, string outPath, int w, int h)
        {
            if (DumpedOriginalPaths.Contains(outPath) || File.Exists(outPath))
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

        private static void MaybeWriteEmptyMask(string maskPath, int w, int h)
        {
            if (DumpedTemplatePaths.Contains(maskPath) || File.Exists(maskPath))
                return;

            try
            {
                var mask = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                var pixels = new Color32[w * h];
                mask.SetPixels32(pixels);
                mask.Apply(false, false);
                File.WriteAllBytes(maskPath, mask.EncodeToPNG());
                DumpedTemplatePaths.Add(maskPath);
                Log.Info($"[CloakMasks] dumpDiscoveredTextures: wrote empty mask template '{maskPath}'.");
                UnityEngine.Object.Destroy(mask);
            }
            catch (Exception ex)
            {
                Log.Warn($"[CloakMasks] dumpDiscoveredTextures: could not write empty mask '{maskPath}': {ex.Message}");
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
