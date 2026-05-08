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
                        AccessTools.Method(typeof(CloakHeroDamageHarmonyPatcher), nameof(AfterHeroHealthEvent))));
                Log.Info($"HornetCloakColor: patched HealthManager.{method.Name} for post-damage cloak refresh.");
                any = true;
                break;
            }

            var heroType = typeof(HeroController);
            var doSpecial = AccessTools.Method(heroType, "DoSpecialDamage", new[] { typeof(bool) });
            if (doSpecial != null)
            {
                harmony.Patch(
                    doSpecial,
                    postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(CloakHeroDamageHarmonyPatcher), nameof(AfterHeroHealthEvent))));
                Log.Info("HornetCloakColor: patched HeroController.DoSpecialDamage for post-damage cloak refresh.");
                any = true;
            }

            if (!any)
            {
                Log.Warn(
                    "HornetCloakColor: no damage hooks patched (API mismatch). Cloak may briefly lose tint after hazards.");
            }

            _applied = true;
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
