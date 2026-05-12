using System.Collections;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Remote SSMP bodies run through <see cref="SSMP.Game.Client.PlayerManager.SpawnPlayer"/> skin setup and
    /// tk2d initialization after our tint runs; materials can be reset on subsequent frames. Reapply over several
    /// frames so cloak shader + masks stick (matches how local hero benefits from continuous LateUpdate).
    /// </summary>
    internal static class MpRemoteCloakReapply
    {
        internal static void Schedule(GameObject? playerObject, CloakNetAppearance appearance)
        {
            if (playerObject == null) return;
            var plugin = HornetCloakColorPlugin.Instance;
            if (plugin == null) return;

            plugin.StartCoroutine(ReapplyRoutine(playerObject, appearance));
        }

        private static IEnumerator ReapplyRoutine(GameObject playerObject, CloakNetAppearance appearance)
        {
            // Let SpawnPlayer / SkinManager / animator finish mutating materials first.
            yield return null;

            const int passes = 5;
            for (var i = 0; i < passes; i++)
            {
                if (!playerObject)
                    yield break;

                CloakColorApplier.Apply(
                    playerObject,
                    appearance.Color,
                    appearance.TextureSaturationMultiplier);
                playerObject.GetComponent<CloakRecolor>()?.ForceHierarchyRefresh();
                yield return null;
            }
        }
    }
}
