using System;
using System.Collections.Generic;
using System.Text;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Scene-wide fallback that finds any <see cref="tk2dSprite"/> whose main texture name
    /// matches one of <see cref="CloakPaletteConfig.SceneScanTextureContains"/> and applies
    /// the local player's cloak color to it.
    ///
    /// This catches Hornet renderers spawned <i>outside</i> <see cref="HeroController"/>'s
    /// hierarchy — e.g. the steam-vent recoil pose and the item-get pose, which are
    /// instantiated as separate scene objects and so are invisible to
    /// <see cref="CloakRecolor"/>'s <c>GetComponentsInChildren</c> walk.
    ///
    /// Created once by the plugin (<see cref="EnsureCreated"/>); survives scene loads.
    ///
    /// Runs late (high <see cref="DefaultExecutionOrderAttribute"/>) so it executes after
    /// tk2d's own LateUpdate that advances the sprite animator and assigns materials.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    internal class CloakSceneScanner : MonoBehaviour
    {
        public static CloakSceneScanner? Instance { get; private set; }

        private CloakColor _color = CloakColor.Default;
        private readonly Dictionary<MeshRenderer, Shader> _originalShaderByRenderer = new();

        // Diagnostic dedup so debug logs don't spam each frame.
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

            var nameFilters = CloakPaletteConfig.SceneScanTextureContains;
            var pathFilters = CloakPaletteConfig.SceneScanPathContains;

            // Scene-wide scan; gated by ScanIntervalFrames. FindObjectsSortMode.None is the
            // cheap variant (we don't need a stable instance-ID order).
            var sprites = FindObjectsByType<tk2dSprite>(FindObjectsSortMode.None);
            if (sprites == null || sprites.Length == 0) return;

            foreach (var sprite in sprites)
            {
                if (sprite == null) continue;
                var renderer = sprite.GetComponent<MeshRenderer>();
                if (renderer == null) continue;

                // sharedMaterial avoids cloning until we actually decide to tint.
                var shared = renderer.sharedMaterial;
                if (shared == null) continue;

                var tex = shared.mainTexture;
                if (tex == null) continue;

                // Renderers owned by a CloakRecolor are already handled with that owner's
                // color (important for SSMP remote players whose color != local color).
                if (renderer.GetComponentInParent<CloakRecolor>() != null)
                    continue;

                // Map compass icons are owned by MapMaskTint (see MapMaskHarmonyPatcher).
                // We must NOT swap their shader to the cloak hue-shift one (its avoid-color
                // list contains white/black/grey, which is exactly the mask palette → no
                // visible tint), and we must NOT rewrite tk2dSprite.color every frame
                // (NestedFadeGroup uses sprite alpha to fade the icon out when the player
                // switches between the wide/overall map and the zoomed-in map; resetting
                // that to white pinned the wide-map icon visible on top of the zoomed view).
                if (IsCompassIcon(renderer.transform))
                    continue;

                var texName = tex.name;

                // Primary match: this exact atlas instance has been seen on a known-Hornet
                // renderer. Reliable because instance IDs are unique per asset, even when
                // many unrelated atlases share the name "atlas0".
                var instanceMatch = HornetTextureRegistry.Contains(tex);

                // Fallback 1: texture name substring (rarely useful — Silksong's Hornet
                // atlases are named atlas0/atlas1/etc., shared with many unrelated atlases).
                var nameMatch = !instanceMatch
                                && nameFilters != null && nameFilters.Length > 0
                                && MatchesAnyFilter(texName, nameFilters);

                // Fallback 2: GameObject path substring. The bed/sit poses live under
                // scene-specific GameObjects whose name contains "Hornet" but whose atlas
                // hasn't been seen by the active hero yet, so registry/name both miss.
                string? path = null;
                var pathMatch = false;
                if (!instanceMatch && !nameMatch
                    && pathFilters != null && pathFilters.Length > 0)
                {
                    path = GetPath(renderer.transform);
                    pathMatch = MatchesAnyFilter(path, pathFilters);
                }

                if (!instanceMatch && !nameMatch && !pathMatch)
                {
                    if (CloakPaletteConfig.DebugLogging && _loggedTextureIds.Add(tex.GetInstanceID()))
                    {
                        path ??= GetPath(renderer.transform);
                        Log.Info($"[Scanner] Ignored texture (no match): {texName} " +
                                 $"(id={tex.GetInstanceID()}) on '{path}'");
                    }
                    continue;
                }

                if (CloakPaletteConfig.DebugLogging && _loggedRendererIds.Add(renderer.GetInstanceID()))
                {
                    path ??= GetPath(renderer.transform);
                    var via = instanceMatch ? "registry"
                            : nameMatch ? "name-filter"
                            : "path-filter";
                    Log.Info($"[Scanner] Tinting orphan renderer '{path}' " +
                             $"(tex={texName}, id={tex.GetInstanceID()}, via={via})");
                }

                // Promote name/path matches into the registry so subsequent scans hit the
                // fast path AND other orphan renderers sharing the same atlas get tinted.
                if (nameMatch || pathMatch)
                {
                    if (HornetTextureRegistry.Register(tex))
                        TextureDumper.TryDump(tex, nameMatch ? "scanner-name" : "scanner-path");
                }

                CloakMaterialApplier.Apply(
                    renderer,
                    sprite,
                    _color,
                    useCloakShader: true,
                    _originalShaderByRenderer);
            }
        }

        // The GameMap (zoomed-in) and InventoryWideMap (overall) compass mask GameObjects
        // are literally named "Compass Icon". SSMP's MapManager.CreatePlayerIcon clones
        // gameMap.compassIcon for every remote player, producing "Compass Icon(Clone)" (and
        // "Compass Icon(Clone)(Clone)" if the same player's icon is recreated). We match
        // the prefix so the scanner stays out of all of them.
        private static bool IsCompassIcon(Transform t)
        {
            if (t == null) return false;
            var n = t.name;
            return n.StartsWith("Compass Icon", StringComparison.Ordinal);
        }

        private static bool MatchesAnyFilter(string name, string[] filters)
        {
            foreach (var f in filters)
            {
                if (string.IsNullOrEmpty(f)) continue;
                if (name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
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
