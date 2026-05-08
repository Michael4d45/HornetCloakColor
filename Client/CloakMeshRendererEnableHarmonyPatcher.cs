using System;
using System.Reflection;
using HarmonyLib;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// When dash/attack layers activate after <see cref="CloakRecolor.LateUpdate"/>, tk2d can assign vanilla
    /// materials for a frame. Unity 6000 does not expose <c>Renderer.OnEnable()</c> for Harmony (no managed target),
    /// so we postfix <see cref="Renderer.enabled"/> and <see cref="GameObject.SetActive"/> instead — still cloak-mask
    /// tint only via <see cref="CloakMaterialApplier"/>.
    /// </summary>
    internal static class CloakMeshRendererEnableHarmonyPatcher
    {
        private const string HarmonyId = "hornet.cloak.color.renderer-visibility";
        private static bool _applied;

        /// <summary>
        /// <see cref="CloakMaterialApplier.Apply"/> can trigger tk2d / Unity internals that toggle
        /// <see cref="GameObject.SetActive"/> or <see cref="Renderer.enabled"/> while we are still inside
        /// a visibility postfix. Re-entering causes nested Apply stacks and has crashed the player during
        /// Witch dash; skip nested hooks — <see cref="CloakRecolor.LateUpdate"/> and pre-draw tint recover.
        /// </summary>
        private static int _visibilityHookDepth;

        private static readonly System.Collections.Generic.List<MeshRenderer> SetActiveMeshScratch = new();

        internal static void Apply()
        {
            if (_applied) return;
            _applied = true;

            var harmony = new Harmony(HarmonyId);
            var patched = false;

            var enabledSetter = typeof(Renderer).GetProperty("enabled", BindingFlags.Public | BindingFlags.Instance)?.GetSetMethod(true);
            if (enabledSetter != null)
            {
                try
                {
                    harmony.Patch(enabledSetter, postfix: new HarmonyMethod(typeof(CloakMeshRendererEnableHarmonyPatcher), nameof(Renderer_Enabled_Setter_Postfix)));
                    Log.Info("HornetCloakColor: hooked Renderer.enabled setter for same-frame hero cloak tint.");
                    patched = true;
                }
                catch (Exception ex)
                {
                    Log.Warn($"CloakMeshRendererEnableHarmonyPatcher: Renderer.enabled setter patch failed ({ex.GetType().Name}: {ex.Message})");
                }
            }
            else
            {
                Log.Warn("CloakMeshRendererEnableHarmonyPatcher: Renderer.enabled setter not found.");
            }

            var setActive = AccessTools.Method(typeof(GameObject), nameof(GameObject.SetActive), new[] { typeof(bool) });
            if (setActive != null)
            {
                try
                {
                    harmony.Patch(setActive, postfix: new HarmonyMethod(typeof(CloakMeshRendererEnableHarmonyPatcher), nameof(GameObject_SetActive_Postfix)));
                    Log.Info("HornetCloakColor: hooked GameObject.SetActive for hero cloak tint when subtrees activate.");
                    patched = true;
                }
                catch (Exception ex)
                {
                    Log.Warn($"CloakMeshRendererEnableHarmonyPatcher: GameObject.SetActive patch failed ({ex.GetType().Name}: {ex.Message})");
                }
            }

            if (!patched)
                Log.Warn("CloakMeshRendererEnableHarmonyPatcher: no visibility hooks applied — Witch/dash cloak tint may lag until LateUpdate/pre-render.");
        }

        private static void Renderer_Enabled_Setter_Postfix(Renderer __instance, bool value)
        {
            if (!value || __instance is not MeshRenderer meshRenderer)
                return;

            if (_visibilityHookDepth > 0)
                return;

            _visibilityHookDepth++;
            try
            {
                TryRefreshUnderRecolor(meshRenderer);
            }
            finally
            {
                _visibilityHookDepth--;
            }
        }

        private static void GameObject_SetActive_Postfix(GameObject __instance, bool value)
        {
            if (!value || __instance == null)
                return;

            if (_visibilityHookDepth > 0)
                return;

            _visibilityHookDepth++;
            try
            {
                var recolor = __instance.GetComponentInParent<CloakRecolor>();
                if (recolor == null)
                    return;

                SetActiveMeshScratch.Clear();
                __instance.GetComponentsInChildren(true, SetActiveMeshScratch);
                foreach (var mr in SetActiveMeshScratch)
                {
                    if (mr != null)
                        recolor.RefreshMeshRendererNow(mr);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"CloakMeshRendererEnableHarmonyPatcher: SetActive postfix threw on '{__instance.name}': {ex.Message}");
            }
            finally
            {
                _visibilityHookDepth--;
            }
        }

        private static void TryRefreshUnderRecolor(MeshRenderer meshRenderer)
        {
            try
            {
                var recolor = meshRenderer.GetComponentInParent<CloakRecolor>();
                if (recolor == null)
                    return;

                recolor.RefreshMeshRendererNow(meshRenderer);
            }
            catch (Exception ex)
            {
                Log.Warn($"CloakMeshRendererEnableHarmonyPatcher: enabled postfix threw on '{meshRenderer.name}': {ex.Message}");
            }
        }
    }
}
