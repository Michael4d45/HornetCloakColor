using System.Collections.Generic;
using System.Diagnostics;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Per-player behaviour that keeps cloak tint applied across animation / material swaps.
    /// Walks <b>all</b> <see cref="MeshRenderer"/>s under the player (including children and
    /// inactive objects). Some Hornet animations use separate renderers or layers; only
    /// touching the root missed those frames.
    ///
    /// This component owns the renderers in its own hierarchy. <see cref="CloakSceneScanner"/>
    /// then handles "orphan" Hornet renderers that the engine spawns elsewhere in the scene
    /// (steam-vent recoil pose, item-get pose, etc.).
    ///
    /// Mesh renderers are cached and the hierarchy is re-scanned every
    /// <see cref="CloakPaletteConfig.HeroMeshRescanIntervalFrames"/> (default 30) instead of every frame,
    /// while <see cref="CloakMaterialApplier.Apply"/> still runs every <c>LateUpdate</c> so tk2d material swaps stay tinted.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    internal class CloakRecolor : MonoBehaviour
    {
        public CloakColor Color { get; private set; } = CloakColor.Default;
        public bool UseCloakShader { get; private set; } = true;

        /// <summary>Original sprite shader per renderer before we swapped in the cloak shader.</summary>
        private readonly Dictionary<MeshRenderer, Shader> _originalShaderByRenderer = new();

        private readonly List<MeshRenderer> _meshCache = new();
        private int _meshRescanCountdown;
        private bool _meshCacheInvalid = true;

        private void OnEnable() => _meshCacheInvalid = true;

        private void LateUpdate()
        {
            CloakMaterialApplier.PruneDestroyed(_originalShaderByRenderer);
            MaybeRefreshMeshCache();

            if (PerfDiagnostics.Enabled)
            {
                var sw = Stopwatch.StartNew();
                ApplyToCachedMeshRenderersCore();
                sw.Stop();
                PerfDiagnostics.RecordRecolorLateUpdate(gameObject.name, _meshCache.Count, sw.Elapsed.TotalMilliseconds);
            }
            else
            {
                ApplyToCachedMeshRenderersCore();
            }
        }

        public void Configure(CloakColor color, bool useCloakShader)
        {
            Color          = color;
            UseCloakShader = useCloakShader;
            _meshCacheInvalid = true;
            RebuildMeshCache();
            ApplyToCachedMeshRenderersCore();
        }

        public void SetColor(CloakColor color)
        {
            Color = color;
            ApplyToCachedMeshRenderersCore();
        }

        private void MaybeRefreshMeshCache()
        {
            if (_meshCacheInvalid)
            {
                RebuildMeshCache();
                return;
            }

            if (--_meshRescanCountdown <= 0)
                RebuildMeshCache();
        }

        private void RebuildMeshCache()
        {
            _meshCacheInvalid = false;
            _meshRescanCountdown = Mathf.Max(1, CloakPaletteConfig.HeroMeshRescanIntervalFrames);
            _meshCache.Clear();

            var renderers = GetComponentsInChildren<MeshRenderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            foreach (var meshRenderer in renderers)
            {
                if (meshRenderer == null) continue;

                if (IsUnderSsmpUsernameObject(meshRenderer.transform))
                    continue;

                _meshCache.Add(meshRenderer);
            }
        }

        private void ApplyToCachedMeshRenderersCore()
        {
            foreach (var meshRenderer in _meshCache)
            {
                if (meshRenderer == null)
                {
                    _meshCacheInvalid = true;
                    continue;
                }

                var shared = meshRenderer.sharedMaterial;
                if (shared != null)
                {
                    var heroTex = shared.mainTexture;
                    if (HornetTextureRegistry.Register(heroTex))
                        TextureDumper.TryDump(heroTex, "hero");
                }

                CloakMaterialApplier.Apply(
                    meshRenderer,
                    sprite: null,
                    Color,
                    UseCloakShader,
                    _originalShaderByRenderer);
            }
        }

        /// <summary>Matches <c>SSMP.Game.Client.PlayerManager.UsernameObjectName</c> ("Username").</summary>
        private static bool IsUnderSsmpUsernameObject(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
            {
                if (p.name == "Username") return true;
            }
            return false;
        }

        public static CloakRecolor? AttachOrUpdate(GameObject? playerObject, CloakColor color, bool useCloakShader)
        {
            if (playerObject == null) return null;
            var comp = playerObject.GetComponent<CloakRecolor>();
            if (comp == null) comp = playerObject.AddComponent<CloakRecolor>();
            comp.Configure(color, useCloakShader);
            return comp;
        }
    }
}
