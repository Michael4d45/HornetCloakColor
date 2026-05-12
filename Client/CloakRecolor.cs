using System.Collections.Generic;
using HornetCloakColor.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Per-player behaviour that keeps cloak tint applied across animation / material swaps.
    /// Walks <b>all</b> <see cref="MeshRenderer"/>s under the player (including children and
    /// inactive objects). Some Hornet animations use separate renderers or layers; only
    /// touching the root missed those frames.
    ///
    /// This component owns the renderers in its hierarchy. <see cref="CloakSceneScanner"/>
    /// applies the same cloak treatment to orphan <c>tk2dSprite</c>s whose atlases have
    /// mask PNGs under <c>CloakMasks/</c>.
    ///
    /// Mesh renderers are cached and the hierarchy is re-scanned every
    /// <see cref="CloakPaletteConfig.HeroMeshRescanIntervalFrames"/> (default 4) instead of every frame,
    /// while <see cref="CloakMaterialApplier.Apply"/> still runs every <c>LateUpdate</c> so tk2d material swaps stay tinted.
    /// Pre-draw: <see cref="Camera.onPreRender"/> on the built-in render pipeline; on SRP,
    /// <see cref="RenderPipelineManager.beginCameraRendering"/> (registered only when a scriptable pipeline asset is set).
    /// Both run after LateUpdate so tk2d cannot restore vanilla materials later in the same frame (common on sprint / Witch dash).
    /// Sprites touched via <see cref="ApplyFromTk2dPipeline"/> register their <see cref="MeshRenderer"/> immediately so
    /// crest-specific attack subtrees tint even before the next periodic rescan.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    [DisallowMultipleComponent]
    internal class CloakRecolor : MonoBehaviour
    {
        private static readonly List<CloakRecolor> ActiveInstances = new();
        /// <summary>Bit 1 = built-in <see cref="Camera.onPreRender"/>; bit 2 = SRP <see cref="RenderPipelineManager.beginCameraRendering"/>.</summary>
        private static byte _preDrawHookRegistration;
        private static int _lastPreDrawTintFrame = -1;

        public CloakColor Color { get; private set; } = CloakColor.Default;
        public bool UseCloakShader { get; private set; } = true;

        /// <summary>
        /// When set, passed to <see cref="CloakMaterialApplier.Apply"/> instead of the local config slider
        /// (SSMP remote players). Null = use <see cref="CloakMaterialApplier.GetTextureSaturationBoost"/> each frame.
        /// </summary>
        private float? _textureSaturationBoostOverride;

        /// <summary>Original sprite shader per renderer before we swapped in the cloak shader.</summary>
        private readonly Dictionary<MeshRenderer, Shader> _originalShaderByRenderer = new();

        private readonly List<MeshRenderer> _meshCache = new();
        private readonly HashSet<MeshRenderer> _meshCacheSet = new();
        private int _meshRescanCountdown;
        private bool _meshCacheInvalid = true;

        /// <summary>
        /// Called from <see cref="CloakTk2dHarmonyPatcher"/> immediately after tk2d finishes updating
        /// this sprite (LateUpdate / Start / OnEnable).
        /// </summary>
        internal void ApplyFromTk2dPipeline(tk2dBaseSprite sprite)
        {
            var meshRenderer = sprite.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                return;

            EnsureMeshRendererTracked(meshRenderer);

            CloakMaterialApplier.InvalidateRenderer(meshRenderer);
            CloakMaterialApplier.Apply(
                meshRenderer,
                sprite,
                Color,
                UseCloakShader,
                _originalShaderByRenderer,
                _textureSaturationBoostOverride);
        }

        /// <summary>
        /// Call after tk2d rebuilds child sprites/materials (spike damage, hurt states, etc.) so we
        /// rediscover <see cref="MeshRenderer"/>s and re-run mask resolution instead of waiting for
        /// the periodic mesh rescan interval.
        /// </summary>
        internal static void NotifyHeroPossibleSpriteRebuild(HeroController? hero)
        {
            if (hero == null) return;
            var recolor = hero.GetComponent<CloakRecolor>();
            recolor?.ForceHierarchyRefresh();
        }

        private void OnEnable()
        {
            _meshCacheInvalid = true;
            if (!ActiveInstances.Contains(this))
                ActiveInstances.Add(this);
            EnsurePreDrawTintHooks();
        }

        private void OnDisable()
        {
            ActiveInstances.Remove(this);
            TeardownPreDrawTintHooksIfIdle();
        }

        private static void EnsurePreDrawTintHooks()
        {
            if (_preDrawHookRegistration != 0)
                return;

            Camera.onPreRender += OnBuiltInCameraPreRender;
            _preDrawHookRegistration |= 1;

            if (GraphicsSettings.defaultRenderPipeline != null)
            {
                RenderPipelineManager.beginCameraRendering += OnSrpBeginCameraRendering;
                _preDrawHookRegistration |= 2;
            }
        }

        private static void TeardownPreDrawTintHooksIfIdle()
        {
            if (ActiveInstances.Count > 0 || _preDrawHookRegistration == 0)
                return;

            if ((_preDrawHookRegistration & 1) != 0)
                Camera.onPreRender -= OnBuiltInCameraPreRender;
            if ((_preDrawHookRegistration & 2) != 0)
                RenderPipelineManager.beginCameraRendering -= OnSrpBeginCameraRendering;
            _preDrawHookRegistration = 0;
        }

        private static void OnBuiltInCameraPreRender(Camera cam)
        {
            if (!IsGameplayCameraForPreDrawTint(cam))
                return;

            RunPreDrawTintPass();
        }

        private static void OnSrpBeginCameraRendering(ScriptableRenderContext _, Camera cam)
        {
            if (!IsGameplayCameraForPreDrawTint(cam))
                return;

            RunPreDrawTintPass();
        }

        /// <summary>
        /// Which cameras may drive the post–LateUpdate tint pass.
        /// When <see cref="Camera.main"/> is set (MainCamera tag), we use only that camera so UI or secondary cams
        /// do not each trigger redundant callbacks (work is still deduped per frame, but this avoids extra noise).
        /// When <see cref="Camera.main"/> is <c>null</c> — common in Silksong for long stretches — <b>every enabled
        /// camera</b> is eligible; that is normal for this game, not a degraded mode. Per-frame dedup in
        /// <see cref="RunPreDrawTintPass"/> runs the actual mesh work once per frame.
        /// </summary>
        private static bool IsGameplayCameraForPreDrawTint(Camera? cam)
        {
            if (cam == null || !cam.enabled)
                return false;

            var main = Camera.main;
            if (main != null)
                return ReferenceEquals(cam, main);

            return true;
        }

        private static void RunPreDrawTintPass()
        {
            var frame = Time.frameCount;
            if (frame == _lastPreDrawTintFrame)
                return;
            _lastPreDrawTintFrame = frame;

            for (var i = ActiveInstances.Count - 1; i >= 0; i--)
            {
                var recolor = ActiveInstances[i];
                if (!recolor)
                {
                    ActiveInstances.RemoveAt(i);
                    continue;
                }

                CloakMaterialApplier.PruneDestroyed(recolor._originalShaderByRenderer);
                recolor.ApplyToCachedMeshRenderersCore();
            }
        }

        /// <summary>
        /// Full mesh cache rebuild + invalidate per-renderer memoization + immediate apply.
        /// </summary>
        internal void ForceHierarchyRefresh()
        {
            _meshCacheInvalid = true;
            RebuildMeshCache();
            CloakMaterialApplier.InvalidateSubtree(transform);
            ApplyToCachedMeshRenderersCore();
        }

        private void LateUpdate()
        {
            CloakMaterialApplier.PruneDestroyed(_originalShaderByRenderer);
            MaybeRefreshMeshCache();
            ApplyToCachedMeshRenderersCore();
        }

        public void Configure(CloakColor color, bool useCloakShader, float? textureSaturationBoostOverride = null)
        {
            Color = color;
            UseCloakShader = useCloakShader;
            _textureSaturationBoostOverride = textureSaturationBoostOverride;
            CloakSceneScanner.EnsureCreated();
            CloakSceneScanner.ReleaseEligibleUnderTransform(transform);
            _meshCacheInvalid = true;
            RebuildMeshCache();
            CloakMaterialApplier.InvalidateSubtree(transform);
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
            _meshCacheSet.Clear();

            var renderers = GetComponentsInChildren<MeshRenderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            foreach (var meshRenderer in renderers)
            {
                if (meshRenderer == null) continue;

                if (IsUnderSsmpUsernameObject(meshRenderer.transform))
                    continue;

                _meshCache.Add(meshRenderer);
                _meshCacheSet.Add(meshRenderer);
            }
        }

        /// <summary>
        /// Attack subtrees can appear or activate after the last full scan; tk2d hooks still drive those sprites every frame.
        /// </summary>
        private void EnsureMeshRendererTracked(MeshRenderer meshRenderer)
        {
            if (meshRenderer == null || IsUnderSsmpUsernameObject(meshRenderer.transform))
                return;

            if (_meshCacheSet.Add(meshRenderer))
                _meshCache.Add(meshRenderer);
        }

        /// <summary>
        /// Same-frame tint when a hero <see cref="MeshRenderer"/> is spawned or enabled after <see cref="LateUpdate"/>
        /// (e.g. Witch dash stab meshes). Uses <see cref="CloakMaterialApplier.ResolveTk2dSprite"/> for mask collection.
        /// </summary>
        internal void RefreshMeshRendererNow(MeshRenderer mr)
        {
            if (mr == null || IsUnderSsmpUsernameObject(mr.transform))
                return;

            EnsureMeshRendererTracked(mr);
            CloakMaterialApplier.InvalidateRenderer(mr);
            CloakMaterialApplier.Apply(
                mr,
                CloakMaterialApplier.ResolveTk2dSprite(mr),
                Color,
                UseCloakShader,
                _originalShaderByRenderer,
                _textureSaturationBoostOverride);
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

                if (IsUnderSsmpUsernameObject(meshRenderer.transform))
                    continue;

                CloakMaterialApplier.Apply(
                    meshRenderer,
                    sprite: null,
                    Color,
                    UseCloakShader,
                    _originalShaderByRenderer,
                    _textureSaturationBoostOverride);
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

        public static CloakRecolor? AttachOrUpdate(
            GameObject? playerObject,
            CloakColor color,
            bool useCloakShader,
            float? textureSaturationBoostOverride = null)
        {
            if (playerObject == null) return null;
            var comp = playerObject.GetComponent<CloakRecolor>();
            if (comp == null) comp = playerObject.AddComponent<CloakRecolor>();
            comp.Configure(color, useCloakShader, textureSaturationBoostOverride);
            return comp;
        }
    }
}
