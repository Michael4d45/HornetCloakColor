using System;
using System.Reflection;
using HarmonyLib;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Spike damage and hurt animations often rebuild tk2d materials or enable child renderers that
    /// were not in <see cref="CloakRecolor"/>'s cached mesh list until the next periodic rescan
    /// (<see cref="CloakPaletteConfig.HeroMeshRescanIntervalFrames"/>). Refresh immediately when the
    /// hero takes damage so cloak shader + masks rebind on the first frame of the reaction.
    /// </summary>
    internal static class CloakHeroDamageHarmonyPatcher
    {
        private const string HarmonyId = "hornet.cloak.color.hero-damage";
        private static bool _applied;

        internal static void Apply()
        {
            if (_applied) return;

            var harmony = new Harmony(HarmonyId);
            var any = false;

            var hmType = typeof(HealthManager);
            foreach (var method in hmType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != "TakeDamage" || method.IsAbstract)
                    continue;

                harmony.Patch(
                    method,
                    postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(CloakHeroDamageHarmonyPatcher), nameof(AfterHeroTakeDamage))));
                Log.Info($"HornetCloakColor: patched HealthManager.{method.Name} for post-damage cloak refresh.");
                any = true;
                break;
            }

            var heroType = typeof(HeroController);
            var doSpecialPatched = 0;
            foreach (var method in heroType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != "DoSpecialDamage" || method.IsAbstract)
                    continue;

                harmony.Patch(
                    method,
                    postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(CloakHeroDamageHarmonyPatcher), nameof(AfterHeroHealthEvent))));
                Log.Info(
                    $"HornetCloakColor: patched HeroController.{method.Name} for post-damage cloak refresh.");
                doSpecialPatched++;
                any = true;
            }

            if (doSpecialPatched == 0)
            {
                Log.Warn(
                    "HornetCloakColor: HeroController.DoSpecialDamage not found (API mismatch). " +
                    "Cloak may briefly lose tint after special damage.");
            }

            if (!any)
            {
                Log.Warn(
                    "HornetCloakColor: no damage hooks patched (API mismatch). Cloak may briefly lose tint after hazards.");
            }

            _applied = true;
        }

        private static void AfterHeroTakeDamage(HealthManager __instance)
        {
            try
            {
                var hero = HeroController.instance;
                if (hero == null || __instance == null)
                    return;

                if (__instance.gameObject != hero.gameObject
                    && __instance.GetComponentInParent<HeroController>() == null)
                    return;

                CloakRecolor.NotifyHeroPossibleSpriteRebuild(hero);
            }
            catch (Exception ex)
            {
                Log.Warn($"CloakHeroDamageHarmonyPatcher: refresh threw: {ex.Message}");
            }
        }

        private static void AfterHeroHealthEvent()
        {
            try
            {
                var hero = HeroController.instance;
                if (hero == null)
                    return;

                CloakRecolor.NotifyHeroPossibleSpriteRebuild(hero);
            }
            catch (Exception ex)
            {
                Log.Warn($"CloakHeroDamageHarmonyPatcher: refresh threw: {ex.Message}");
            }
        }
    }
}
