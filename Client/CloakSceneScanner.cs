using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Scene-wide fallback: tints orphan <see cref="tk2dSprite"/>s that match
    /// <see cref="CloakPaletteConfig.MatchesSceneScanAllowlist"/> (tk2d collection name substrings
    /// in <c>cloak_palette.json</c>).
    ///
    /// Catches renderers spawned <i>outside</i> <see cref="HeroController"/>'s hierarchy
    /// (e.g. steam-vent recoil, item-get pose).
    ///
    /// <see cref="CloakPaletteConfig.SceneScanIntervalFrames"/> controls how often we run
    /// <c>FindObjectsByType</c> to refresh the orphan set; <see cref="CloakMaterialApplier.Apply"/>
    /// runs <b>every</b> <c>LateUpdate</c> on that cache so tk2d material resets (FX, HUD) do not
    /// flicker between scans.
    ///
    /// Runs late (high <see cref="DefaultExecutionOrderAttribute"/>) so it executes after
    /// tk2d's LateUpdate.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    internal class CloakSceneScanner : MonoBehaviour
    {
        public static CloakSceneScanner? Instance { get; private set; }

        private CloakColor _color = CloakColor.Default;
        private readonly Dictionary<MeshRenderer, Shader> _originalShaderByRenderer = new();

        private readonly HashSet<int> _loggedTextureIds = new();
        private readonly HashSet<int> _loggedRendererIds = new();

        private readonly List<MeshRenderer> _orphanCache = new();
        private readonly HashSet<MeshRenderer> _rebuildDedup = new();

        private int _frameCounter;

        public static void EnsureCreated()
        {
            if (Instance != null) return;
            var go = new GameObject("HornetCloakColorSceneScanner");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CloakSceneScanner>();
        }

        public static void SetColor(CloakColor color)
        {
            EnsureCreated();
            Instance!._color = color;
        }

        private void LateUpdate()
        {
            CloakMaterialApplier.PruneDestroyed(_originalShaderByRenderer);

            var interval = Math.Max(1, CloakPaletteConfig.SceneScanIntervalFrames);
            if ((_frameCounter++ % interval) == 0)
                RunFullSceneScan();

            ApplyOrphanCache();
        }

        /// <summary>Walks every <see cref="tk2dSprite"/> and rebuilds <see cref="_orphanCache"/>.</summary>
        private void RunFullSceneScan()
        {
            double findMs;
            tk2dSprite[] sprites;
            if (PerfDiagnostics.Enabled)
            {
                var swFind = Stopwatch.StartNew();
                sprites = FindObjectsByType<tk2dSprite>(FindObjectsSortMode.None);
                swFind.Stop();
                findMs = swFind.Elapsed.TotalMilliseconds;
            }
            else
            {
                sprites = FindObjectsByType<tk2dSprite>(FindObjectsSortMode.None);
                findMs = 0;
            }

            if (sprites == null || sprites.Length == 0)
            {
                _orphanCache.Clear();
                _rebuildDedup.Clear();
                if (PerfDiagnostics.Enabled)
                    PerfDiagnostics.RecordSceneScan(0, 0, findMs, 0);
                return;
            }

            int cacheSize;
            double loopMs;
            if (PerfDiagnostics.Enabled)
            {
                var swLoop = Stopwatch.StartNew();
                cacheSize = RebuildOrphanCache(sprites);
                swLoop.Stop();
                loopMs = swLoop.Elapsed.TotalMilliseconds;
                PerfDiagnostics.RecordSceneScan(sprites.Length, cacheSize, findMs, loopMs);
            }
            else
            {
                _ = RebuildOrphanCache(sprites);
            }
        }

        /// <summary>Returns number of distinct <see cref="MeshRenderer"/>s added to the cache.</summary>
        private int RebuildOrphanCache(tk2dSprite[] sprites)
        {
            _orphanCache.Clear();
            _rebuildDedup.Clear();

            if (CloakPaletteConfig.SceneScanAllowlistEmpty)
                return 0;

            foreach (var sprite in sprites)
            {
                if (sprite == null) continue;
                var renderer = sprite.GetComponent<MeshRenderer>();
                if (renderer == null) continue;

                var shared = renderer.sharedMaterial;
                if (shared == null) continue;

                var tex = shared.mainTexture;
                if (tex == null) continue;

                if (renderer.GetComponentInParent<CloakRecolor>() != null)
                    continue;

                if (IsCompassIcon(renderer.transform))
                    continue;

                var texName = tex.name;
                var path = FormatTransformPath(renderer.transform);
                var collectionName = sprite.Collection != null ? (sprite.Collection.name ?? string.Empty) : string.Empty;

                if (!CloakPaletteConfig.MatchesSceneScanAllowlist(collectionName))
                {
                    if (CloakPaletteConfig.DebugLogging && _loggedTextureIds.Add(tex.GetInstanceID()))
                    {
                        Log.Info($"[Scanner] Ignored texture (no allowlist match): {texName} " +
                                 $"(id={tex.GetInstanceID()}) on '{path}' " +
                                 $"(collection='{collectionName}')");
                    }
                    continue;
                }

                if (!_rebuildDedup.Add(renderer))
                    continue;

                if (CloakPaletteConfig.DebugLogging && _loggedRendererIds.Add(renderer.GetInstanceID()))
                {
                    Log.Info($"[Scanner] Tinting orphan renderer '{path}' " +
                             $"(tex={texName}, collection='{collectionName}', id={tex.GetInstanceID()})");
                }

                _orphanCache.Add(renderer);
            }

            _rebuildDedup.Clear();
            return _orphanCache.Count;
        }

        private void ApplyOrphanCache()
        {
            if (_orphanCache.Count == 0) return;

            for (var i = _orphanCache.Count - 1; i >= 0; i--)
            {
                var renderer = _orphanCache[i];
                if (renderer == null)
                {
                    _orphanCache.RemoveAt(i);
                    continue;
                }

                if (renderer.GetComponentInParent<CloakRecolor>() != null)
                {
                    _orphanCache.RemoveAt(i);
                    continue;
                }

                if (IsCompassIcon(renderer.transform))
                {
                    _orphanCache.RemoveAt(i);
                    continue;
                }

                var sprite = renderer.GetComponent<tk2dSprite>();
                var shared = renderer.sharedMaterial;
                if (shared == null)
                {
                    _orphanCache.RemoveAt(i);
                    continue;
                }

                if (shared.mainTexture == null)
                {
                    _orphanCache.RemoveAt(i);
                    continue;
                }

                var collectionName = sprite != null && sprite.Collection != null
                    ? (sprite.Collection.name ?? string.Empty)
                    : string.Empty;

                if (CloakPaletteConfig.SceneScanAllowlistEmpty
                    || !CloakPaletteConfig.MatchesSceneScanAllowlist(collectionName))
                {
                    _orphanCache.RemoveAt(i);
                    continue;
                }

                CloakMaterialApplier.Apply(
                    renderer,
                    sprite,
                    _color,
                    useCloakShader: true,
                    _originalShaderByRenderer);
            }
        }

        private static bool IsCompassIcon(Transform t)
        {
            if (t == null) return false;
            return t.name.StartsWith("Compass Icon", StringComparison.Ordinal);
        }

        /// <summary>Hierarchy path like <c>Root/Child/Leaf</c> for logging.</summary>
        internal static string FormatTransformPath(Transform? t)
        {
            if (t == null) return "(null)";
            var sb = new StringBuilder();
            while (t != null)
            {
                if (sb.Length > 0) sb.Insert(0, '/');
                sb.Insert(0, t.name);
                t = t.parent;
            }
            return sb.ToString();
        }
    }
}
