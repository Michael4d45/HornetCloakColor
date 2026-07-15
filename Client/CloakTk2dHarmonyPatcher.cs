using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Runs cloak apply immediately after tk2d finishes pipeline work on a sprite (geometry/material).
    /// Hooks declared <c>UpdateColors</c>, <c>SetColors</c>, and <c>UpdateVertices</c> on
    /// <see cref="tk2dBaseSprite"/> and concrete <c>tk2d*</c> subclasses, plus <c>OnEnable</c>/<c>Start</c> on
    /// <see cref="tk2dAnimatedSprite"/> and <c>tk2dSpriteAnimator</c> (verified present in silksong1.0.30000).
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

        /// <summary>Methods declared on tk2d types that drive color/vertex updates (order irrelevant).</summary>
        /// <remarks>
        /// Verified absent in silksong1.0.30000: LateUpdate, Update, FixedUpdate, BuildMesh, UpdateMesh,
        /// SwitchClip on tk2d pipeline types; OnEnable/Start on tk2dBaseSprite/tk2dSprite (not declared there).
        /// </remarks>
        private static readonly string[] Tk2dDeclaredPipelineNames =
        {
            "UpdateColors",
            "SetColors",
            "UpdateVertices",
        };

        /// <summary>Lifecycle hooks declared only on animated tk2d types in this build.</summary>
        private static readonly string[] Tk2dAnimatedLifecycleNames =
        {
            "OnEnable",
            "Start",
        };

        internal static void Apply()
        {
            if (_applied) return;

            var harmony = new Harmony(HarmonyId);
            var postfix = new HarmonyMethod(
                AccessTools.Method(typeof(CloakTk2dHarmonyPatcher), nameof(Tk2dSprite_Postfix)));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var patched = 0;
            var patchedLabels = new List<string>();

            foreach (var type in EnumerateTk2dSpritePipelineTypes())
            {
                foreach (var methodName in Tk2dDeclaredPipelineNames)
                {
                    foreach (var method in EnumerateDeclaredPipelineMethods(type, methodName))
                        TryPatchPipelineMethod(harmony, postfix, method, seen, patchedLabels, ref patched);
                }
            }

            foreach (var type in new[] { typeof(tk2dAnimatedSprite), typeof(tk2dSpriteAnimator) })
            {
                foreach (var methodName in Tk2dAnimatedLifecycleNames)
                {
                    foreach (var method in EnumerateDeclaredPipelineMethods(type, methodName))
                        TryPatchPipelineMethod(harmony, postfix, method, seen, patchedLabels, ref patched);
                }
            }

            foreach (var method in EnumerateAnimatedSpriteDeclaredMeshCandidates())
                TryPatchPipelineMethod(harmony, postfix, method, seen, patchedLabels, ref patched);

            _applied = true;

            if (patched > 0)
            {
                Log.Info(
                    $"HornetCloakColor: hooked {patched} tk2d pipeline method(s) for post-tk2d cloak apply: " +
                    string.Join(", ", patchedLabels));
            }

            if (patched == 0)
            {
                Log.Warn(
                    "HornetCloakColor: no tk2d pipeline methods were patched — cloak tint may lag " +
                    "until CloakRecolor mesh rescans or hero damage refresh. Report Silksong tk2d API changes to the mod author.");
            }
        }

        /// <summary>
        /// Includes subclasses (animated/clipped/etc.) where <c>BuildMesh</c> overrides actually live in this build.
        /// </summary>
        private static IEnumerable<Type> EnumerateTk2dSpritePipelineTypes()
        {
            var root = typeof(tk2dBaseSprite);
            yield return root;
            yield return typeof(tk2dSprite);

            Assembly asm;
            try
            {
                asm = root.Assembly;
            }
            catch
            {
                yield break;
            }

            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).Cast<Type>().ToArray();
            }

            foreach (var t in types)
            {
                if (t.IsAbstract || !t.IsClass || !root.IsAssignableFrom(t))
                    continue;

                if (!t.Name.StartsWith("tk2d", StringComparison.Ordinal))
                    continue;

                if (t == root || t == typeof(tk2dSprite))
                    continue;

                yield return t;
            }
        }

        private static IEnumerable<MethodInfo> EnumerateDeclaredPipelineMethods(Type type, string name)
        {
            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.Name == name)
                    yield return m;
            }
        }

        /// <summary>
        /// Silksong-specific helpers on <see cref="tk2dAnimatedSprite"/> that do not match our fixed name list.
        /// </summary>
        private static IEnumerable<MethodInfo> EnumerateAnimatedSpriteDeclaredMeshCandidates()
        {
            var animated = typeof(tk2dAnimatedSprite);
            foreach (var m in animated.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName)
                    continue;

                var n = m.Name;
                if (n.Contains("Mesh", StringComparison.Ordinal))
                    yield return m;

                if (n.Contains("Build", StringComparison.Ordinal) && !n.Contains("Rebuild", StringComparison.Ordinal))
                    yield return m;
            }
        }

        private static void TryPatchPipelineMethod(
            Harmony harmony,
            HarmonyMethod postfix,
            MethodInfo method,
            HashSet<string> seen,
            List<string> patchedLabels,
            ref int patched)
        {
            if (!CanHarmonyDetour(method))
                return;

            var key = $"{method.MetadataToken:X8}:{method.DeclaringType!.AssemblyQualifiedName}";
            if (!seen.Add(key))
                return;

            var label = $"{method.DeclaringType!.Name}.{FormatMethodSignature(method)}";

            try
            {
                harmony.Patch(method, postfix: postfix);
                patched++;
                patchedLabels.Add(label);
            }
            catch (Exception ex)
            {
                Log.Warn(
                    $"HornetCloakColor: skipped {label} — Harmony cannot detour it ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        private static string FormatMethodSignature(MethodInfo m)
        {
            var ps = m.GetParameters();
            if (ps.Length == 0)
                return m.Name;

            return $"{m.Name}({string.Join(",", ps.Select(p => p.ParameterType.Name))})";
        }

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
            // Patches are attached to methods declared on tk2dSprite and tk2dBaseSprite; the runtime
            // instance may be a subtype that does not inherit tk2dSprite (e.g. some clipped/tiled sprites).
            if (__instance is not tk2dBaseSprite sprite)
                return;

            HarmonySafe.Run(
                () => PostTk2dBaseSprite(sprite),
                $"CloakTk2dHarmonyPatcher: postfix threw on '{sprite?.name ?? "(null)"}'");
        }

        private static void PostTk2dBaseSprite(tk2dBaseSprite sprite)
        {
            var recolor = sprite.GetComponentInParent<CloakRecolor>();
            if (recolor != null)
            {
                recolor.ApplyFromTk2dPipeline(sprite);
                return;
            }

            CloakSceneScanner.OnTk2dPipelineComplete(sprite);
        }
    }
}
