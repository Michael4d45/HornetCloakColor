using System;
using HarmonyLib;
using HornetCloakColor.Shared;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Harmony postfix on <c>tk2dSprite.Awake</c> so newly-spawned masked sprites are evaluated
    /// for cloak tint on the same frame they appear, instead of waiting up to
    /// <c>CloakSceneScanner.RescanIntervalSec</c> seconds for the next backstop scan.
    ///
    /// <para>
    /// This is the fix for the visible "sometimes the spike-death pose isn't tinted" bug:
    /// transient effects (e.g. <c>Knight Spike Death(Clone)</c>) often spawn-and-despawn entirely
    /// within a single rescan interval, so polling alone misses them.
    /// </para>
    ///
    /// <para>
    /// Per-call cost is small (a few µs on warm caches — the per-texture ID cache in
    /// <see cref="CloakMaskManager"/> avoids repeated string allocations). Sprites whose atlas
    /// has no mask short-circuit immediately.
    /// </para>
    /// </summary>
    internal static class CloakSpawnHookHarmonyPatcher
    {
        private const string HarmonyId = "hornet.cloak.color.spawn-hook";
        private static bool _applied;

        internal static void Apply()
        {
            if (_applied) return;
            _applied = true;

            var harmony = new Harmony(HarmonyId);

            // tk2dSprite.Awake is the canonical spawn point. AccessTools resolves it on the
            // declaring type even if it lives on a base (tk2dBaseSprite in standard 2DToolkit
            // builds; the game's variant may differ).
            var tk2dType = typeof(tk2dSprite);
            var awake = AccessTools.Method(tk2dType, "Awake");
            if (awake == null)
            {
                Log.Warn("CloakSpawnHookHarmonyPatcher: tk2dSprite.Awake not found; spawn-hook disabled. " +
                         "The 2-second backstop scan still picks up new sprites, just with up to ~2s latency.");
                return;
            }

            harmony.Patch(
                awake,
                postfix: new HarmonyMethod(AccessTools.Method(typeof(CloakSpawnHookHarmonyPatcher), nameof(Tk2dSprite_Awake_Postfix))));

            Log.Info($"Hooked {tk2dType.Name}.Awake (declared on {awake.DeclaringType?.Name ?? "?"}) for spawn-time cloak tint.");
        }

        /// <summary>
        /// Postfix runs after the sprite's own Awake / Build, so <c>renderer.sharedMaterial</c>
        /// and <c>Collection</c> are populated. We catch and swallow exceptions so a misbehaving
        /// scanner can never break sprite construction.
        /// </summary>
        private static void Tk2dSprite_Awake_Postfix(tk2dSprite __instance)
        {
            try
            {
                CloakSceneScanner.OnSpriteSpawned(__instance);
            }
            catch (Exception ex)
            {
                Log.Warn($"CloakSpawnHookHarmonyPatcher: postfix threw on '{__instance?.name ?? "(null)"}': {ex.Message}");
            }
        }
    }
}
