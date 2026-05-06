using System.Reflection;
using HarmonyLib;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Postfix on SSMP's name color application so custom username tints can layer on top.
    /// </summary>
    internal static class UsernameColorHarmonyPatcher
    {
        private const string HarmonyId = "hornet.cloak.color.usernametint";

        private static bool _applied;
        private static bool _aborted;

        /// <summary>True after SSMP <c>ChangeNameColor</c> was patched successfully.</summary>
        internal static bool IsActive => _applied;

        internal static void Apply()
        {
            if (_applied || _aborted) return;
            if (!SSMPBridge.IsAvailable) return;

            var pm = AccessTools.TypeByName("SSMP.Game.Client.PlayerManager");
            if (pm == null) return;

            MethodInfo? method = null;
            foreach (var mi in AccessTools.GetDeclaredMethods(pm))
            {
                if (mi.Name != "ChangeNameColor") continue;
                if (mi.GetParameters().Length != 2) continue;
                method = mi;
                break;
            }

            if (method == null)
            {
                _aborted = true;
                Log.Warn(
                    "UsernameColorHarmonyPatcher: PlayerManager.ChangeNameColor not found (SSMP API may have changed). Username tint disabled.");
                return;
            }

            try
            {
                var harmony = new Harmony(HarmonyId);
                harmony.Patch(
                    method,
                    postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(UsernameColorHarmonyPatcher), nameof(ChangeNameColor_Postfix))));
                _applied = true;
            }
            catch (System.Exception ex)
            {
                _aborted = true;
                Log.Warn($"UsernameColorHarmonyPatcher: failed to patch ChangeNameColor: {ex.Message}");
            }
        }

        /// <summary>
        /// Use <c>object</c> parameters so the postfix matches SSMP's TextMeshPro CLR type even when it
        /// differs from the TMPro assembly referenced by this plugin.
        /// </summary>
        private static void ChangeNameColor_Postfix(object textMeshObject, object? team)
        {
            try
            {
                if (team == null) return;
                if (textMeshObject is not Component comp) return;
                UsernameTintCoordinator.OnAfterSsmpChangeNameColor(comp, team);
            }
            catch (System.Exception ex)
            {
                Log.Warn($"UsernameColorHarmonyPatcher postfix: {ex.Message}");
            }
        }
    }
}
