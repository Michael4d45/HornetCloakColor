using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Attaches <see cref="CloakRecolor"/> and applies the chosen <see cref="CloakColor"/>.
    /// Uses the cloak shader when the embedded bundle is available; otherwise vertex tints
    /// the whole sprite.
    /// </summary>
    internal static class CloakColorApplier
    {
        public static void Apply(GameObject? playerObject, CloakColor color)
        {
            if (playerObject == null) return;

            var useCloakShader = CloakShaderManager.Shader != null;
            CloakRecolor.AttachOrUpdate(playerObject, color, useCloakShader);
        }
    }
}
