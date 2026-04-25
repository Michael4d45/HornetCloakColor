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
    /// <see cref="CloakPaletteConfig.MatchesSceneScanAllowlist"/> (collection / texture name /
    /// transform path substrings in <c>cloak_palette.json</c>).
    ///
    /// Catches renderers spawned <i>outside</i> <see cref="HeroController"/>'s hierarchy
    /// (e.g. steam-vent recoil, item-get pose).
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
            var interval = Math.Max(1, CloakPaletteConfig.SceneScanIntervalFrames);
            if ((_frameCounter++ % interval) != 0) return;

            CloakMaterialApplier.PruneDestroyed(_originalShaderByRenderer);

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
                if (PerfDiagnostics.Enabled)
                    PerfDiagnostics.RecordSceneScan(0, 0, findMs, 0);
                return;
            }

            int applied;
            double loopMs;
            if (PerfDiagnostics.Enabled)
            {
                var swLoop = Stopwatch.StartNew();
                applied = RunScanLoop(sprites);
                swLoop.Stop();
                loopMs = swLoop.Elapsed.TotalMilliseconds;
                PerfDiagnostics.RecordSceneScan(sprites.Length, applied, findMs, loopMs);
            }
            else
            {
                _ = RunScanLoop(sprites);
            }
        }

        private int RunScanLoop(tk2dSprite[] sprites)
        {
            var applied = 0;
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

                if (CloakPaletteConfig.SceneScanAllowlistEmpty)
                    continue;

                var texName = tex.name;
                var path = GetPath(renderer.transform);
                var collectionName = sprite.Collection != null ? (sprite.Collection.name ?? string.Empty) : string.Empty;

                if (!CloakPaletteConfig.MatchesSceneScanAllowlist(collectionName, texName, path))
                {
                    if (CloakPaletteConfig.DebugLogging && _loggedTextureIds.Add(tex.GetInstanceID()))
                    {
                        Log.Info($"[Scanner] Ignored texture (no allowlist match): {texName} " +
                                 $"(id={tex.GetInstanceID()}) on '{path}' " +
                                 $"(collection='{collectionName}')");
                    }
                    continue;
                }

                if (CloakPaletteConfig.DebugLogging && _loggedRendererIds.Add(renderer.GetInstanceID()))
                {
                    Log.Info($"[Scanner] Tinting orphan renderer '{path}' " +
                             $"(tex={texName}, collection='{collectionName}', id={tex.GetInstanceID()})");
                }

                CloakMaterialApplier.Apply(
                    renderer,
                    sprite,
                    _color,
                    useCloakShader: true,
                    _originalShaderByRenderer);
                applied++;
            }

            return applied;
        }

        private static bool IsCompassIcon(Transform t)
        {
            if (t == null) return false;
            return t.name.StartsWith("Compass Icon", StringComparison.Ordinal);
        }

        private static string GetPath(Transform t)
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
