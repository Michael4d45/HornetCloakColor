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
    /// Silksong's tk2d types inherit Unity lifecycle methods (<see cref="MonoBehaviour.LateUpdate"/>, etc.)
    /// without redeclaring them, so declaring-type-only lookup on
    /// <c>tk2dSprite</c> alone finds nothing. We patch every relevant method **declared** on
    /// <see cref="tk2dSprite"/>, <see cref="tk2dBaseSprite"/>, and other concrete <c>tk2d*</c> subclasses
    /// (e.g. <c>tk2dAnimatedSprite.BuildMesh</c>) — Silksong attack/dash frames often rebuild there without
    /// calling <c>tk2dSprite.UpdateColors</c>, which left only that hook (see BepInEx log).
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

            foreach (var type in EnumerateTk2dSpritePipelineTypes())
            {
                foreach (var methodName in Tk2dDeclaredPipelineNames)
                {
                    // Patch every declared overload (e.g. BuildMesh() vs BuildMesh(bool)), not only the first match.
                    foreach (var method in EnumerateDeclaredPipelineMethods(type, methodName))
                        TryPatchPipelineMethod(harmony, postfix, method, seen, ref patched);
                }
            }

            // console-16: tk2dAnimatedSprite had only OnEnable/Start — frame advances often use inherited
            // BuildMesh/UpdateMesh on tk2dSprite without redeclaring UpdateColors on the animated type.
            foreach (var extraName in new[] { "BuildMesh", "UpdateMesh", "UpdateVertices", "SwitchClip" })
            {
                foreach (var method in EnumerateInheritedChainMethods(typeof(tk2dAnimatedSprite), extraName))
                    TryPatchPipelineMethod(harmony, postfix, method, seen, ref patched);
            }

            foreach (var method in EnumerateAnimatedSpriteDeclaredMeshCandidates())
                TryPatchPipelineMethod(harmony, postfix, method, seen, ref patched);

            _applied = true;

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
        /// Walk <paramref name="leaf"/> → bases up to <see cref="tk2dBaseSprite"/> and yield every overload of
        /// <paramref name="name"/> declared on each level (virtual overrides show up on the declaring type).
        /// </summary>
        private static IEnumerable<MethodInfo> EnumerateInheritedChainMethods(Type leaf, string name)
        {
            var root = typeof(tk2dBaseSprite);
            for (var t = leaf; t != null && root.IsAssignableFrom(t); t = t.BaseType)
            {
                foreach (var m in EnumerateDeclaredPipelineMethods(t, name))
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
                Log.Info($"HornetCloakColor: hooked {label} for post-tk2d cloak apply.");
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

            try
            {
                PostTk2dBaseSprite(sprite, __originalMethod?.Name);
            }
            catch (Exception ex)
            {
                Log.Warn($"CloakTk2dHarmonyPatcher: postfix threw on '{sprite?.name ?? "(null)"}': {ex.Message}");
            }
        }

        private static void PostTk2dBaseSprite(tk2dBaseSprite sprite, string? patchedMethodName)
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
