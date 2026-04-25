using System.Collections.Generic;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Shared per-renderer "apply cloak shader / properties / vertex tint" logic. Used by
    /// both <see cref="CloakRecolor"/> (per-player hierarchy) and <see cref="CloakSceneScanner"/>
    /// (scene-wide scan). Each caller owns its own dictionary mapping renderer to original
    /// shader so we can restore correctly when the cloak shader is unavailable.
    /// </summary>
    internal static class CloakMaterialApplier
    {
        public static void Apply(
            MeshRenderer renderer,
            tk2dSprite? sprite,
            CloakColor color,
            bool useCloakShader,
            Dictionary<MeshRenderer, Shader> originalShaderByRenderer)
        {
            if (renderer == null) return;

            // .material clones once and returns the per-renderer instance on subsequent calls,
            // so this is cheap once the renderer has been touched.
            var mat = renderer.material;
            if (mat == null) return;

            sprite ??= renderer.GetComponent<tk2dSprite>();

            if (useCloakShader && CloakShaderManager.Shader != null)
            {
                EnsureCloakShader(renderer, mat, originalShaderByRenderer);
                ApplyShaderProperties(mat, sprite, color);
            }
            else
            {
                RestoreOriginalShader(renderer, mat, originalShaderByRenderer);
                ApplyVertexTint(mat, sprite, color);
            }
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

        private static void ApplyShaderProperties(Material mat, tk2dSprite? sprite, CloakColor color)
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
                mat.SetTexture(CloakShaderManager.CloakMaskTexId, CloakMaskManager.BlackWeightMask);
                return;
            }

            color.ToHSV(out var h, out var s, out var v);
            mat.SetFloat(CloakShaderManager.TargetHueId, h);
            mat.SetFloat(CloakShaderManager.TargetSatId, s <= 0.001f ? 0f : 1.0f);
            mat.SetFloat(CloakShaderManager.TargetValId, Mathf.Lerp(0.6f, 1.4f, v));
            mat.SetFloat(CloakShaderManager.StrengthId, 1f);

            var collectionName = sprite?.Collection != null ? sprite.Collection.name : null;
            var mask = CloakMaskManager.GetMaskForMainTexture(mat.mainTexture, collectionName);
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
