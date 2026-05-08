using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Runs cloak apply immediately after tk2d finishes pipeline work on a sprite (geometry/material).
    /// Silksong's tk2d types inherit Unity lifecycle methods (<see cref="MonoBehaviour.LateUpdate"/>, etc.)
    /// without redeclaring them, so declaring-type-only lookup on
    /// <c>tk2dSprite</c> alone finds nothing. We patch every relevant method **declared** on
    /// <see cref="tk2dSprite"/> and <see cref="tk2dBaseSprite"/> (e.g. mesh build), which is tk2d-specific and
    /// avoids patching <see cref="MonoBehaviour.LateUpdate"/> for the entire game.
    ///
    /// <para>
    /// <c>Awake</c> is omitted here — <see cref="CloakSpawnHookHarmonyPatcher"/> already postfixes <c>Awake</c> for
    /// spawn-time scanner enrollment.
    /// </para>
    /// </summary>
    internal static class CloakTk2dHarmonyPatcher
    {
        private const string HarmonyId = "hornet.cloak.color.tk2d-post";
        private static bool _applied;

        /// <summary>Methods declared on tk2d types that drive mesh/material updates (order irrelevant).</summary>
        private static readonly string[] Tk2dDeclaredPipelineNames =
        {
            "LateUpdate",
            "FixedUpdate",
            "Update",
            "OnEnable",
            "Start",
            "BuildMesh",
            "UpdateMesh",
            "UpdateColors",
            "SetColors",
        };

        internal static void Apply()
        {
            if (_applied) return;

            var harmony = new Harmony(HarmonyId);
            var postfix = new HarmonyMethod(
                AccessTools.Method(typeof(CloakTk2dHarmonyPatcher), nameof(Tk2dSprite_Postfix)));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var patched = 0;

            foreach (var type in new[] { typeof(tk2dSprite), typeof(tk2dBaseSprite) })
            {
                foreach (var methodName in Tk2dDeclaredPipelineNames)
                {
                    // Use raw reflection — Harmony's AccessTools.DeclaredMethod logs a Warning on every miss, which
                    // spams the console when most names exist only on MonoBehaviour or use different signatures.
                    var method = GetDeclaredInstanceMethodNoParams(type, methodName);
                    if (method == null || !CanHarmonyDetour(method))
                        continue;

                    var key = $"{method.MetadataToken:X8}:{method.DeclaringType!.AssemblyQualifiedName}";
                    if (!seen.Add(key))
                        continue;

                    try
                    {
                        harmony.Patch(method, postfix: postfix);
                        patched++;
                        Log.Info($"HornetCloakColor: hooked {method.DeclaringType!.Name}.{methodName} for post-tk2d cloak apply.");
                    }
                    catch (Exception ex)
                    {
                        // Common when Unity/tk2d exposes extern or runtime-internal stubs (no IL body).
                        Log.Warn(
                            $"HornetCloakColor: skipped {method.DeclaringType?.Name}.{methodName} — Harmony cannot detour it ({ex.GetType().Name}: {ex.Message}).");
                    }
                }
            }

            _applied = true;

            if (patched == 0)
            {
                Log.Warn(
                    "HornetCloakColor: no tk2dSprite/tk2dBaseSprite pipeline methods were patched — cloak tint may lag " +
                    "until CloakRecolor mesh rescans or hero damage refresh. Report Silksong tk2d API changes to the mod author.");
            }
        }

        private static MethodInfo? GetDeclaredInstanceMethodNoParams(Type type, string name) =>
            type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

        /// <summary>
        /// Harmony/MonoMod requires a real IL body; some declared tk2d methods are extern or otherwise unstoppable.
        /// </summary>
        private static bool CanHarmonyDetour(MethodBase method)
        {
            if (method.IsAbstract)
                return false;

            // P/Invoke and certain runtime forwards report no body to the runtime.
            if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                return false;

            if (method is not MethodInfo mi)
                return false;

            try
            {
                return mi.GetMethodBody() != null;
            }
            catch
            {
                return false;
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

            // Per-frame Unity callbacks: do not walk orphan scanner path (would hit every pooled tk2d sprite).
            // Geometry/color hooks (BuildMesh, etc.) still run so transient sprites pick up tint when relevant.
            if (IsHighFrequencyUnityLifecycle(patchedMethodName))
                return;

            CloakSceneScanner.OnTk2dPipelineComplete(sprite);
        }

        private static bool IsHighFrequencyUnityLifecycle(string? patchedMethodName) =>
            string.Equals(patchedMethodName, "LateUpdate", StringComparison.Ordinal)
            || string.Equals(patchedMethodName, "Update", StringComparison.Ordinal)
            || string.Equals(patchedMethodName, "FixedUpdate", StringComparison.Ordinal);
    }
}
