using System;
using System.Reflection;
using HarmonyLib;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Runs cloak apply immediately after tk2d finishes its own <see cref="MonoBehaviour"/> hooks
    /// (<see cref="LateUpdate"/> etc.). Script execution order alone is ambiguous among behaviours at
    /// the same priority; here Harmony guarantees our logic runs after tk2d updates materials on that
    /// sprite for the current frame.
    /// </summary>
    internal static class CloakTk2dHarmonyPatcher
    {
        private const string HarmonyId = "hornet.cloak.color.tk2d-post";
        private static bool _applied;

        internal static void Apply()
        {
            if (_applied) return;
            _applied = true;

            var harmony = new Harmony(HarmonyId);
            var spriteType = typeof(tk2dSprite);

            foreach (var methodName in new[] { "LateUpdate", "Start", "OnEnable" })
            {
                MethodBase? method = null;
                for (var cur = spriteType; cur != null && cur != typeof(MonoBehaviour); cur = cur.BaseType)
                {
                    method = AccessTools.DeclaredMethod(cur, methodName, Type.EmptyTypes);
                    if (method != null)
                        break;
                }

                if (method == null)
                    continue;

                harmony.Patch(
                    method,
                    postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(CloakTk2dHarmonyPatcher), nameof(Tk2dSprite_Postfix))
                    )
                );

                Log.Info($"HornetCloakColor: hooked {method.DeclaringType?.Name}.{methodName} for post-tk2d cloak apply.");
            }
        }

        private static void Tk2dSprite_Postfix(MonoBehaviour __instance, MethodBase __originalMethod)
        {
            if (__instance is not tk2dSprite sprite)
                return;

            try
            {
                PostTk2dSprite(sprite, __originalMethod?.Name);
            }
            catch (Exception ex)
            {
                Log.Warn($"CloakTk2dHarmonyPatcher: postfix threw on '{sprite?.name ?? "(null)"}': {ex.Message}");
            }
        }

        private static void PostTk2dSprite(tk2dSprite sprite, string? patchedMethodName)
        {
            var recolor = sprite.GetComponentInParent<CloakRecolor>();
            if (recolor != null)
            {
                recolor.ApplyFromTk2dPipeline(sprite);
                return;
            }

            // LateUpdate runs every frame for every tk2d sprite — skip orphans here (scene scanner +
            // spawn hooks). Start/OnEnable still catch late material assignment on orphan sprites.
            if (string.Equals(patchedMethodName, "LateUpdate", StringComparison.Ordinal))
                return;

            CloakSceneScanner.OnTk2dPipelineComplete(sprite);
        }
    }
}
