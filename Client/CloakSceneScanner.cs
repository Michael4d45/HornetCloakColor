using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using HornetCloakColor.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Scene-wide fallback: tints orphan <see cref="tk2dSprite"/>s whose atlas has a matching
    /// mask PNG under <c>CloakMasks/</c>.
    ///
    /// Catches renderers spawned <i>outside</i> <see cref="HeroController"/>'s hierarchy
    /// (e.g. steam-vent recoil, item-get pose, <c>Knight Spike Death(Clone)</c>).
    ///
    /// <para>
    /// Discovery has two paths:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="OnSpriteSpawned"/> is called from a Harmony postfix on
    ///     <c>tk2dSprite.Awake</c> (see <see cref="CloakSpawnHookHarmonyPatcher"/>) so newly-
    ///     spawned masked sprites are tinted on the same frame they appear. This is the
    ///     latency-sensitive path that catches transient effects (death animations, etc.).
    ///   </item>
    ///   <item>
    ///     A budget-sliced full <see cref="FindObjectsByType{T}(FindObjectsSortMode)"/> walk
    ///     runs on scene change and on a slow backstop (<see cref="RescanIntervalSec"/>),
    ///     for any sprite the spawn hook missed (e.g. material assigned after Awake).
    ///   </item>
    /// </list>
    /// <para>
    /// Per-frame work walks the cached eligible-renderer list and calls the memoized
    /// <see cref="CloakMaterialApplier.Apply"/>, which is essentially free when nothing changed.
    /// </para>
    ///
    /// Runs late (high <see cref="DefaultExecutionOrderAttribute"/>) so it executes after
    /// tk2d's LateUpdate, avoiding a frame where newly spawned masked sprites are left untinted.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    internal class CloakSceneScanner : MonoBehaviour
    {
        public static CloakSceneScanner? Instance { get; private set; }

        /// <summary>Slow rescan interval so newly spawned (post-scene-load) masked sprites get picked up.</summary>
        private const float RescanIntervalSec = 2.0f;

        /// <summary>
        /// Per-frame budget for the discovery walk. The coroutine yields (defers to the next frame)
        /// once it's spent this much wall time on filtering sprites. Keeps post-scene-change and
        /// periodic-rescan spikes bounded — at the cost of a few frames of latency before
        /// newly-loaded masked sprites get tinted.
        /// </summary>
        private const double RebuildBudgetMs = 1.5;

        private CloakColor _color = CloakColor.Default;
        private readonly Dictionary<MeshRenderer, Shader> _originalShaderByRenderer = new();

        private readonly HashSet<int> _loggedTextureIds = new();
        private readonly HashSet<int> _loggedRendererIds = new();

        /// <summary>Renderers we've matched as scanner-tinted; walked every frame (cheap, memoized).</summary>
        private readonly List<MeshRenderer> _eligibleCache = new();
        private readonly Dictionary<MeshRenderer, tk2dSprite> _eligibleSprite = new();
        private readonly HashSet<MeshRenderer> _eligibleSet = new();

        private bool _eligibleCacheDirty = true;
        private float _nextRescanTime;
        private Coroutine? _rebuildCo;

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
            // Per-renderer memo keys on Color; clearing isn't required (entries naturally
            // mismatch and re-apply), but a forced invalidation guarantees the next frame's
            // walk pushes the new tint immediately even if some renderer happens to land on
            // an instance ID we'd never seen.
            CloakMaterialApplier.InvalidateAll();
        }

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        private void OnActiveSceneChanged(Scene from, Scene to)
        {
            // Material/renderer instances from the previous scene are gone; everything keyed by
            // them is stale. Mark the cache dirty (will trigger a fresh budgeted rebuild) and
            // drop applier memo so freshly-loaded scene renderers take the slow path on first sight.
            _eligibleCacheDirty = true;
            CloakMaterialApplier.InvalidateAll();

            // Cancel any in-flight rebuild from the previous scene; its results would be stale.
            // The next LateUpdate restarts the coroutine against the new scene.
            if (_rebuildCo != null)
            {
                StopCoroutine(_rebuildCo);
                _rebuildCo = null;
            }

            // Drop the (now-invalid) eligible cache so we don't keep applying to dead renderers.
            _eligibleCache.Clear();
            _eligibleSet.Clear();
            _eligibleSprite.Clear();
        }

        private void LateUpdate()
        {
            CloakMaterialApplier.PruneDestroyed(_originalShaderByRenderer);

            var now = Time.realtimeSinceStartup;
            if (_rebuildCo == null && (_eligibleCacheDirty || now >= _nextRescanTime))
            {
                _eligibleCacheDirty = false;
                _nextRescanTime = now + RescanIntervalSec;
                _rebuildCo = StartCoroutine(RebuildEligibleCacheCo());
            }

            ApplyEligibleCache();
        }

        /// <summary>
        /// Called from <see cref="CloakSpawnHookHarmonyPatcher"/> on every <c>tk2dSprite.Awake</c>.
        /// Same-frame eligibility check + immediate apply, so a transient spawn (e.g.
        /// <c>Knight Spike Death(Clone)</c>) is tinted on its first rendered frame instead of
        /// waiting up to <see cref="RescanIntervalSec"/> seconds for the next backstop scan.
        /// </summary>
        internal static void OnSpriteSpawned(tk2dSprite sprite)
        {
            if (Instance == null || sprite == null) return;
            Instance.TryEnrollEligible(sprite, source: "Spawn");
        }

        /// <summary>
        /// Eligibility check shared by the spawn hook and the periodic scan. Returns the matched
        /// mask (via <see cref="CloakMaskManager"/>'s caches) when the sprite qualifies, or
        /// <c>false</c> otherwise. Cheap on warm caches: a previously-seen atlas resolves in O(1)
        /// without string allocation.
        /// </summary>
        private bool PassesEligibility(MeshRenderer renderer, tk2dSprite sprite)
        {
            if (renderer == null || sprite == null) return false;

            var shared = renderer.sharedMaterial;
            if (shared == null) return false;
            // _MainTex must exist; touching mat.mainTexture on a custom shader without it
            // (e.g. Heat Effect) would log a Unity error.
            if (!shared.HasProperty(CloakShaderManager.MainTexId)) return false;

            var tex = shared.mainTexture;
            if (tex == null) return false;

            // Renderers under a CloakRecolor are owned by the per-player path. Compass icons are
            // tinted by the map-mask code; both paths would otherwise stomp on each other.
            if (renderer.GetComponentInParent<CloakRecolor>() != null) return false;
            if (IsCompassIcon(renderer.transform)) return false;

            var collectionName = sprite.Collection != null ? (sprite.Collection.name ?? string.Empty) : string.Empty;
            return CloakMaskManager.TryGetMaskForMainTexture(tex, collectionName, out _);
        }

        /// <summary>
        /// Add the renderer to the eligible cache and apply tint immediately. No-op if the
        /// renderer is already tracked or doesn't qualify. Diagnostic logging is gated by
        /// <c>debugLogging</c> and deduplicated per-texture / per-renderer.
        /// </summary>
        private void TryEnrollEligible(tk2dSprite sprite, string source)
        {
            var renderer = sprite.GetComponent<MeshRenderer>();
            if (renderer == null) return;
            if (_eligibleSet.Contains(renderer)) return;

            if (!PassesEligibility(renderer, sprite))
            {
                MaybeLogIgnoredTexture(renderer, sprite);
                return;
            }

            _eligibleSet.Add(renderer);
            _eligibleCache.Add(renderer);
            _eligibleSprite[renderer] = sprite;

            if (CloakPaletteConfig.DebugLogging && _loggedRendererIds.Add(renderer.GetInstanceID()))
            {
                var tex = renderer.sharedMaterial != null ? renderer.sharedMaterial.mainTexture : null;
                var texName = tex != null ? tex.name : "(null)";
                var texId = tex != null ? tex.GetInstanceID() : 0;
                var collectionName = sprite.Collection != null ? (sprite.Collection.name ?? string.Empty) : string.Empty;
                Log.Info($"[Scanner/{source}] Tinting masked orphan renderer '{FormatTransformPath(renderer.transform)}' " +
                         $"(tex={texName}, collection='{collectionName}', id={texId})");
            }

            // Apply now so the sprite is tinted on its first rendered frame (or, for the
            // periodic scan, on the same frame we discovered it).
            CloakMaterialApplier.Apply(
                renderer,
                sprite,
                _color,
                useCloakShader: true,
                _originalShaderByRenderer);
        }

        /// <summary>
        /// Logs <c>[Scanner] Ignored texture (no CloakMasks PNG): ...</c> at most once per
        /// unique atlas, gated by <c>debugLogging</c>. Lets users identify which textures need
        /// a mask PNG without spamming the log for every sprite spawn.
        /// </summary>
        private void MaybeLogIgnoredTexture(MeshRenderer renderer, tk2dSprite sprite)
        {
            if (!CloakPaletteConfig.DebugLogging) return;
            var shared = renderer.sharedMaterial;
            if (shared == null || !shared.HasProperty(CloakShaderManager.MainTexId)) return;
            var tex = shared.mainTexture;
            if (tex == null) return;
            if (!_loggedTextureIds.Add(tex.GetInstanceID())) return;
            var collectionName = sprite.Collection != null ? (sprite.Collection.name ?? string.Empty) : string.Empty;
            Log.Info($"[Scanner] Ignored texture (no CloakMasks PNG): {tex.name} " +
                     $"(id={tex.GetInstanceID()}) on '{FormatTransformPath(renderer.transform)}' " +
                     $"(collection='{collectionName}')");
        }

        /// <summary>
        /// Budget-sliced backstop scan. Catches sprites the spawn hook missed (e.g. material
        /// assigned after Awake) and any sprites that existed before the patcher was applied.
        ///
        /// Purely additive: the cache only grows here. The memoized <see cref="CloakMaterialApplier.Apply"/>
        /// already handles "renderer is no longer eligible" gracefully by storing
        /// <c>NoMaskRestored</c> state when its mask lookup fails on the slow path. Removal
        /// happens via destroyed-null detection in <see cref="ApplyEligibleCache"/> and via
        /// <see cref="OnActiveSceneChanged"/>'s scene-wide cache reset.
        /// </summary>
        private IEnumerator RebuildEligibleCacheCo()
        {
            var sw = Stopwatch.StartNew();

            // SpriteRenderer masks are rare and often appear as pre-existing scene objects
            // (e.g. diving-bell bench-grab). Scan them first so Texture2D-masked sprites don't
            // wait behind the larger tk2d discovery pass.
            var spriteRenderers = FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (spriteRenderers != null)
            {
                foreach (var spriteRenderer in spriteRenderers)
                {
                    if (spriteRenderer != null)
                        CloakSpriteRendererTint.Watch(spriteRenderer);

                    if (sw.Elapsed.TotalMilliseconds > RebuildBudgetMs)
                    {
                        yield return null;
                        sw.Restart();
                    }
                }
            }

            // Defer the heavier tk2d pass by one frame if the SpriteRenderer pass did not
            // already yield, so we avoid piling all discovery work onto scene-load frames.
            yield return null;

            var sprites = FindObjectsByType<tk2dSprite>(FindObjectsSortMode.None);
            if (sprites != null)
            {
                foreach (var sprite in sprites)
                {
                    // TryEnrollEligible is a no-op for already-tracked / ineligible renderers,
                    // so calling it unconditionally keeps this loop short and self-contained.
                    if (sprite != null) TryEnrollEligible(sprite, source: "Rescan");

                    if (sw.Elapsed.TotalMilliseconds > RebuildBudgetMs)
                    {
                        yield return null;
                        sw.Restart();
                    }
                }
            }

            _rebuildCo = null;
        }

        /// <summary>Per-frame: walk the cached eligible list and call the memoized applier.</summary>
        private void ApplyEligibleCache()
        {
            for (int i = _eligibleCache.Count - 1; i >= 0; i--)
            {
                var renderer = _eligibleCache[i];
                if (renderer == null)
                {
                    // Destroyed mid-frame; drop it from every cache so transient effects
                    // (spike-death, item-get pose, etc.) don't accumulate stale entries.
                    // Unity-destroyed objects compare equal to null but the C# reference is
                    // still the original wrapper, so HashSet/Dictionary will find and remove it.
                    _eligibleCache.RemoveAt(i);
                    _eligibleSet.Remove(renderer!);
                    _eligibleSprite.Remove(renderer!);
                    continue;
                }

                _eligibleSprite.TryGetValue(renderer, out var sprite);
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
