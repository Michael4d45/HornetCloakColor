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
    /// (e.g. steam-vent recoil, item-get pose).
    ///
    /// <para>
    /// Discovery (full <see cref="FindObjectsByType{T}(FindObjectsSortMode)"/> walk) only runs on
    /// scene change and on a slow trickle (<see cref="RescanIntervalSec"/>) — not every frame.
    /// The walk itself is <b>budget-sliced</b> across frames via a coroutine
    /// (<see cref="RebuildBudgetMs"/> per frame) so a heavy scene transition or periodic rescan
    /// never absorbs the whole discovery cost in a single frame.
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
        /// Budget-sliced full scene walk: build the list of orphan masked renderers we want to
        /// keep tinted. Yields whenever the per-frame budget (<see cref="RebuildBudgetMs"/>) is
        /// exhausted so a heavy scene transition doesn't compound into a single big spike.
        ///
        /// While the rebuild is in-flight, <see cref="ApplyEligibleCache"/> continues to walk the
        /// previous cache, so the visible state is still tinted (just slightly stale until the
        /// new walk completes — a few frames at most).
        /// </summary>
        private IEnumerator RebuildEligibleCacheCo()
        {
            // Defer the start by one frame so we never share the scene-load frame with the
            // discovery walk; the game's own scene-init work already pegs that frame.
            yield return null;

            var sprites = FindObjectsByType<tk2dSprite>(FindObjectsSortMode.None);

            // Track the previous eligible set so we can RESTORE renderers that no longer qualify
            // (e.g. moved under the player hierarchy, or their atlas changed).
            var stillEligible = new HashSet<MeshRenderer>();
            var pendingSprite = new Dictionary<MeshRenderer, tk2dSprite>();

            var sw = Stopwatch.StartNew();

            if (sprites != null)
            {
                foreach (var sprite in sprites)
                {
                    if (sprite == null) goto Yield;
                    var renderer = sprite.GetComponent<MeshRenderer>();
                    if (renderer == null) goto Yield;

                    var shared = renderer.sharedMaterial;
                    if (shared == null) goto Yield;
                    if (!shared.HasProperty(CloakShaderManager.MainTexId)) goto Yield;

                    var tex = shared.mainTexture;
                    if (tex == null) goto Yield;

                    if (renderer.GetComponentInParent<CloakRecolor>() != null) goto Yield;
                    if (IsCompassIcon(renderer.transform)) goto Yield;

                    var collectionName = sprite.Collection != null ? (sprite.Collection.name ?? string.Empty) : string.Empty;
                    if (!CloakMaskManager.TryGetMaskForMainTexture(tex, collectionName, out _))
                    {
                        if (CloakPaletteConfig.DebugLogging && _loggedTextureIds.Add(tex.GetInstanceID()))
                        {
                            Log.Info($"[Scanner] Ignored texture (no CloakMasks PNG): {tex.name} " +
                                     $"(id={tex.GetInstanceID()}) on '{FormatTransformPath(renderer.transform)}' " +
                                     $"(collection='{collectionName}')");
                        }
                        goto Yield;
                    }

                    if (stillEligible.Add(renderer))
                    {
                        if (CloakPaletteConfig.DebugLogging && _loggedRendererIds.Add(renderer.GetInstanceID()))
                        {
                            Log.Info($"[Scanner] Tinting masked orphan renderer '{FormatTransformPath(renderer.transform)}' " +
                                     $"(tex={tex.name}, collection='{collectionName}', id={tex.GetInstanceID()})");
                        }
                        pendingSprite[renderer] = sprite;
                    }

                Yield:
                    if (sw.Elapsed.TotalMilliseconds > RebuildBudgetMs)
                    {
                        yield return null;
                        sw.Restart();
                    }
                }
            }

            // Restore renderers that were previously eligible but now aren't.
            foreach (var prev in _eligibleSet)
            {
                if (prev == null) continue;
                if (stillEligible.Contains(prev)) continue;
                CloakMaterialApplier.Restore(prev, _originalShaderByRenderer);
            }

            // Atomic-ish swap: replace the live cache with the freshly-built one.
            _eligibleSprite.Clear();
            foreach (var kv in pendingSprite) _eligibleSprite[kv.Key] = kv.Value;

            _eligibleSet.Clear();
            _eligibleCache.Clear();
            foreach (var r in stillEligible)
            {
                _eligibleSet.Add(r);
                _eligibleCache.Add(r);
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
                    // Destroyed mid-frame; remove and let the next rescan (or scene change)
                    // rebuild authoritatively.
                    _eligibleCache.RemoveAt(i);
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
