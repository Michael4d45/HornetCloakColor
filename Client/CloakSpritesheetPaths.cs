using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Formats on-disk mask paths the same way <see cref="CloakMaskManager"/> resolves them.
    /// Used by the optional spritesheet debug overlay.
    /// </summary>
    internal static class CloakSpritesheetPaths
    {
        /// <summary>
        /// Relative path under the plugin folder for a tk2d atlas mask
        /// (<c>CloakMasks/&lt;collection&gt;/&lt;texture&gt;.png</c>).
        /// </summary>
        public static string FormatTk2dMaskPath(string? tk2dCollectionName, Texture? mainTex)
        {
            if (mainTex == null)
                return "CloakMasks/(no texture)";

            var stemPrimary = CloakDiskNames.CollectionFolder(tk2dCollectionName);
            var texStem = CloakDiskNames.SanitizeFileStem(
                string.IsNullOrEmpty(mainTex.name) ? $"tex_{mainTex.GetInstanceID()}" : mainTex.name);

            return $"CloakMasks/{stemPrimary}/{texStem}.png";
        }

        /// <summary>
        /// Relative path for a standalone <see cref="SpriteRenderer"/> mask
        /// (<c>CloakMasks/Texture2D/&lt;stem&gt;.png</c>).
        /// </summary>
        public static string FormatTexture2DMaskPath(string? spriteOrTextureName)
        {
            var stem = CloakDiskNames.SanitizeFileStem(spriteOrTextureName);
            if (stem.Length == 0)
                return "CloakMasks/Texture2D/(no name)";

            return $"CloakMasks/Texture2D/{stem}.png";
        }
    }
}
