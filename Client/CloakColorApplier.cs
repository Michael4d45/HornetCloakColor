using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Applies a <see cref="CloakColor"/> tint to a player's character renderer.
    ///
    /// Silksong's hero (and the SSMP remote-player prefab) renders Hornet through a single
    /// MeshRenderer with a tk2dSprite driving the vertex colors. The cheapest way to recolor
    /// the cloak without swapping textures is to multiply the sprite's tint.
    ///
    /// We prefer the tk2dSprite color path (vertex-color multiplier) because it is non-destructive
    /// and survives animation frame swaps. If tk2dSprite is unavailable we fall back to cloning
    /// the MeshRenderer material and setting <c>_Color</c>.
    /// </summary>
    internal static class CloakColorApplier
    {
        /// <summary>
        /// Apply the given color to the player GameObject. Safe to call with a null target.
        /// </summary>
        public static void Apply(GameObject? playerObject, CloakColor color)
        {
            if (playerObject == null) return;

            var unityColor = color.ToUnityColor();
            var applied = false;

            // Primary path: tk2dSprite's vertex tint (works on local hero + remote player prefab).
            var sprite = playerObject.GetComponent<tk2dSprite>();
            if (sprite != null)
            {
                sprite.color = unityColor;
                applied = true;
            }

            // Fallback / reinforcement: tint the MeshRenderer's material directly. Accessing
            // `.material` here clones the shared material so we don't pollute other renderers.
            var meshRenderer = playerObject.GetComponent<MeshRenderer>();
            if (meshRenderer != null && meshRenderer.material != null)
            {
                meshRenderer.material.color = unityColor;
                applied = true;
            }

            if (!applied)
            {
                Log.Warn("No tk2dSprite or MeshRenderer found on player object; cloak tint skipped.");
            }
        }
    }
}
