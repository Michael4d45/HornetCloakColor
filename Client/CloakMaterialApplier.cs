using System;
using System.Collections.Generic;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Shared per-renderer "apply cloak shader / properties / vertex tint" logic. Used by
    /// both <see cref="CloakRecolor"/> (per-player hierarchy) and <see cref="CloakSceneScanner"/>
    /// (scene-wide mask pass). Each caller owns its own dictionary mapping renderer to original
    /// shader so we can restore correctly when the cloak shader is unavailable or no mask exists.
    ///
    /// <para>
    /// Apply memoizes per-Material state (shader, mask texture, color, mode) so steady-state
    /// frames skip the SetTexture / SetFloat block entirely. tk2d may replace a renderer's
    /// <c>sharedMaterial</c> with a new vanilla material mid-animation; that shows up as a
    /// new instance ID and trips the slow path so the cloak shader gets reapplied.
    /// </para>
    /// </summary>
    internal static class CloakMaterialApplier
    {
        private enum AppliedMode : byte
        {
            None = 0,
            CloakApplied,        // cloak shader bound + mask bound + tint pushed
            NoMaskRestored,      // useCloakShader=true but no mask on disk → original shader kept
            VertexTinted,        // useCloakShader=false → original shader + vertex tint
        }

        private readonly struct AppliedState
        {
            public readonly int SharedMatInstanceId;
            public readonly CloakColor Color;
            public readonly AppliedMode Mode;

            public AppliedState(int sharedMatInstanceId, CloakColor color, AppliedMode mode)
            {
                SharedMatInstanceId = sharedMatInstanceId;
                Color = color;
                Mode = mode;
            }
        }

        /// <summary>
        /// Per-renderer memoization: renderer.GetInstanceID() → last applied state. The state's
        /// <c>SharedMatInstanceId</c> is the instance ID of <c>renderer.sharedMaterial</c> after we
        /// finished work; when tk2d swaps the renderer's material mid-animation the IDs diverge
        /// and we rerun the slow path.
        /// </summary>
        private static readonly Dictionary<int, AppliedState> AppliedByRenderer = new();

        /// <summary>
        /// Drop all memoized per-renderer state. Forces the next <see cref="Apply"/> to take the
        /// slow path. Call after mask reloads, scene transitions, or anything that may rebind
        /// textures or shaders out from under us.
        /// </summary>
        public static void InvalidateAll() => AppliedByRenderer.Clear();

        public static void Apply(
            MeshRenderer renderer,
            tk2dSprite? sprite,
            CloakColor color,
            bool useCloakShader,
            Dictionary<MeshRenderer, Shader> originalShaderByRenderer)
        {
            if (renderer == null) return;

            var sharedCheck = renderer.sharedMaterial;
            if (sharedCheck == null) return;

            // Skip materials whose shader does not expose _MainTex (e.g. heat-effect FX,
            // custom shaders attached to in-scene effects). Touching mat.mainTexture below
            // would otherwise spam Unity errors.
            if (!sharedCheck.HasProperty(CloakShaderManager.MainTexId)) return;

            var rendererId = renderer.GetInstanceID();
            var sharedMatId = sharedCheck.GetInstanceID();
            var hasCloakShader = useCloakShader && CloakShaderManager.Shader != null;

            // Fast path: same Material instance + same intent + same color → nothing changed.
            // Mask binding can only change after InvalidateAll() (palette reload / scene change),
            // so it's implicitly covered by the SharedMatInstanceId equality.
            if (AppliedByRenderer.TryGetValue(rendererId, out var prev)
                && prev.SharedMatInstanceId == sharedMatId)
            {
                if (hasCloakShader)
                {
                    if (prev.Mode == AppliedMode.CloakApplied && prev.Color.Equals(color)) return;
                    if (prev.Mode == AppliedMode.NoMaskRestored) return; // confirmed no mask; keep restored
                }
                else if (prev.Mode == AppliedMode.VertexTinted && prev.Color.Equals(color))
                {
                    return;
                }
            }

            // .material clones once and returns the per-renderer instance on subsequent calls,
            // so this is cheap once the renderer has been touched.
            var mat = renderer.material;
            if (mat == null) return;

            sprite ??= renderer.GetComponent<tk2dSprite>();

            AppliedMode finalMode;
            CloakColor finalColor = color;

            if (hasCloakShader)
            {
                var collectionName = sprite?.Collection != null ? sprite.Collection.name : null;
                if (!CloakMaskManager.TryGetMaskForMainTexture(mat.mainTexture, collectionName, out var mask))
                {
                    RestoreOriginalShader(renderer, mat, originalShaderByRenderer);
                    finalMode = AppliedMode.NoMaskRestored;
                    finalColor = default;
                }
                else
                {
                    EnsureCloakShader(renderer, mat, originalShaderByRenderer);
                    ApplyShaderProperties(mat, sprite, color, mask);
                    finalMode = AppliedMode.CloakApplied;
                }
            }
            else
            {
                RestoreOriginalShader(renderer, mat, originalShaderByRenderer);
                ApplyVertexTint(mat, sprite, color);
                finalMode = AppliedMode.VertexTinted;
            }

            // After Apply, renderer.sharedMaterial == the cloned per-renderer Material; cache its
            // instance ID so subsequent frames hit the fast path until tk2d (or anything else)
            // swaps a different material onto the renderer.
            var finalSharedMat = renderer.sharedMaterial;
            if (finalSharedMat != null)
                AppliedByRenderer[rendererId] = new AppliedState(finalSharedMat.GetInstanceID(), finalColor, finalMode);
        }

        public static void Restore(MeshRenderer renderer, Dictionary<MeshRenderer, Shader> originalShaderByRenderer)
        {
            if (renderer == null) return;
            var shared = renderer.sharedMaterial;
            if (shared == null) return;
            if (!shared.HasProperty(CloakShaderManager.MainTexId)) return;

            var rendererId = renderer.GetInstanceID();
            var sharedMatId = shared.GetInstanceID();

            // Fast path: already restored against this exact Material instance.
            if (AppliedByRenderer.TryGetValue(rendererId, out var prev)
                && prev.SharedMatInstanceId == sharedMatId
                && prev.Mode == AppliedMode.NoMaskRestored)
            {
                return;
            }

            var mat = renderer.material;
            if (mat == null) return;
            RestoreOriginalShader(renderer, mat, originalShaderByRenderer);

            var finalSharedMat = renderer.sharedMaterial;
            if (finalSharedMat != null)
                AppliedByRenderer[rendererId] = new AppliedState(finalSharedMat.GetInstanceID(), default, AppliedMode.NoMaskRestored);
        }

        private static void EnsureCloakShader(
            MeshRenderer renderer,
            Material mat,
            Dictionary<MeshRenderer, Shader> map)
        {
            var cloakShader = CloakShaderManager.Shader!;
            if (mat.shader == cloakShader) return;

            if (!map.ContainsKey(renderer))
                map[renderer] = mat.shader;

            // Swapping the shader on a Sprite material can null the texture binding on
            // some Unity versions; preserve and restore it.
            var tex = mat.mainTexture;
            mat.shader = cloakShader;
            if (tex != null) mat.mainTexture = tex;
        }

        private static void RestoreOriginalShader(
            MeshRenderer renderer,
            Material mat,
            Dictionary<MeshRenderer, Shader> map)
        {
            if (!map.TryGetValue(renderer, out var orig) || orig == null) return;
            if (mat.shader == orig) return;

            var tex = mat.mainTexture;
            mat.shader = orig;
            if (tex != null) mat.mainTexture = tex;
        }

        private static void ApplyShaderProperties(Material mat, tk2dSprite? sprite, CloakColor color, Texture2D mask)
        {
            // The cloak shader reads vertex/tk2d color as a multiplier; force white so the
            // user-chosen tint isn't darkened by a leftover sprite color.
            if (sprite != null && sprite.color != Color.white)
                sprite.color = Color.white;

            if (color.Equals(CloakColor.Default))
            {
                // White preset = "no tint": disable the recolor pass and let the vanilla
                // texture show through (prevents the cloak going off-white/grey).
                mat.SetFloat(CloakShaderManager.StrengthId, 0f);
                mat.SetTexture(CloakShaderManager.CloakMaskTexId, mask);
                return;
            }

            color.ToHSV(out var h, out var s, out var v);
            mat.SetFloat(CloakShaderManager.TargetHueId, h);
            mat.SetFloat(CloakShaderManager.TargetSatId, s <= 0.001f ? 0f : 1.0f);
            mat.SetFloat(CloakShaderManager.TargetValId, Mathf.Lerp(0.6f, 1.4f, v));
            mat.SetFloat(CloakShaderManager.StrengthId, 1f);
            mat.SetTexture(CloakShaderManager.CloakMaskTexId, mask);
        }

        private static void ApplyVertexTint(Material mat, tk2dSprite? sprite, CloakColor color)
        {
            var unityColor = color.ToUnityColor();
            if (sprite != null) sprite.color = unityColor;
            mat.color = unityColor;
        }

        public static void PruneDestroyed(Dictionary<MeshRenderer, Shader> map)
        {
            if (map.Count == 0) return;
            List<MeshRenderer>? dead = null;
            foreach (var key in map.Keys)
            {
                if (!key) (dead ??= new List<MeshRenderer>()).Add(key!);
            }
            if (dead == null) return;
            foreach (var d in dead) map.Remove(d);
        }
    }
}
