using System.Collections;
using BepInEx;
using Silksong.ModMenu;
using UnityEngine;
using BepInEx.Logging;
using HornetCloakColor.Client;
using HornetCloakColor.Shared;

namespace HornetCloakColor
{
    /// <summary>
    /// BepInEx entry point. Applies the local cloak color to Hornet and, if SSMP is
    /// installed, registers SSMP addons so the color is synchronized to other players.
    /// SSMP is a <b>soft</b> dependency — without it the mod still recolors your own cloak.
    /// </summary>
    [BepInAutoPlugin(id: "hornet.cloak.color", version: ModVersion)]
    [BepInDependency(ModMenuPlugin.Id)]
    [BepInDependency(SSMPBridge.SSMPGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public partial class HornetCloakColorPlugin : BaseUnityPlugin
    {
        /// <summary>
        /// Keep this in sync with &lt;Version&gt; in HornetCloakColor.csproj. The BepInAutoPlugin
        /// attribute requires a compile-time constant, so we can't read from the csproj directly.
        /// </summary>
        public const string ModVersion = "1.14.3";

        internal static HornetCloakColorPlugin? Instance { get; private set; }
        internal static ManualLogSource? LogSource { get; private set; }
        internal CloakColorConfig ColorConfig { get; private set; } = null!;
        internal UsernameColorConfig UsernameColorConfig { get; private set; } = null!;

        private void Awake()
        {
            Instance = this;
            LogSource = Logger;

            CloakPaletteConfig.Load();

            var sweepDelays = CloakPaletteConfig.MissingMaskDumpAllowlistSweepDelaysSec;
            if (sweepDelays != null && sweepDelays.Length > 0)
                StartCoroutine(AllowlistMissingMaskSweepRoutine(sweepDelays));

            // Stand up the scene-wide scanner before the hero loads so orphan Hornet
            // renderers (steam-vent recoil, item-get pose, etc.) get tinted as soon as
            // they appear, even if the hero's hierarchy never owns them.
            CloakSceneScanner.EnsureCreated();

            ColorConfig = new CloakColorConfig(Config);
            UsernameColorConfig = new UsernameColorConfig(Config);
            CloakColorApplier.SetLocalSceneColor(ColorConfig.EffectiveColor);

            MapMaskHarmonyPatcher.Apply();

            // Same-frame tint for newly-spawned tk2dSprites (e.g. Knight Spike Death(Clone)).
            // Without this, transient sprites can spawn-and-despawn between backstop rescans
            // and never get tinted — the visible "sometimes she's tinted, sometimes not" bug.
            CloakSpawnHookHarmonyPatcher.Apply();
            CloakTk2dHarmonyPatcher.Apply();
            CloakMeshRendererEnableHarmonyPatcher.Apply();
            CloakHeroDamageHarmonyPatcher.Apply();

            // SSMP may load after this plugin; username tint needs its types + satellite registration.
            UsernameColorHarmonyPatcher.Apply();
            TryRegisterSsmpSatelliteAndLog();

            StartCoroutine(SsmpIntegrationRetryRoutine());

            ColorConfig.ColorChanged += OnConfigColorChanged;
            UsernameColorConfig.UsernameTintChanged += OnUsernameTintConfigChanged;

            HeroController.OnHeroInstanceSet += OnHeroInstanceSet;

            Logger.LogInfo($"{Name} v{ModVersion} loaded.");
        }

        /// <summary>
        /// Runs <see cref="CloakMaskManager.SweepAllowlistedLoadedTexturesForMissingMaskDumps"/> after each delay so atlases
        /// that load later (menus, scene loads) still get a chance to dump without binding every sprite first.
        /// </summary>
        private IEnumerator AllowlistMissingMaskSweepRoutine(float[] delaysSec)
        {
            foreach (var sec in delaysSec)
            {
                if (sec > 0f)
                    yield return new WaitForSecondsRealtime(sec);

                CloakMaskManager.SweepAllowlistedLoadedTexturesForMissingMaskDumps();
            }
        }

        /// <summary>
        /// Retries satellite registration until username Harmony delegates exist; then pushes username + cloak RGB once.
        /// Cloak push mirrors <see cref="PushLocalUsernameToNetwork"/> timing so both run after SSMP client wiring is ready.
        /// </summary>
        private IEnumerator SsmpIntegrationRetryRoutine()
        {
            for (var i = 0; i < 180; i++)
            {
                if (SSMPBridge.IsAvailable)
                    UsernameColorHarmonyPatcher.Apply();

                if (SSMPBridge.IsAvailable && !SSMPBridge.IsRegistered)
                    TryRegisterSsmpSatelliteAndLog();

                RefreshLocalUsernameVisual();
                if (SSMPBridge.IsRegistered && UsernameNetworkDelegates.TryResolveUsernameTransform != null)
                {
                    PushLocalUsernameToNetwork();
                    PushLocalCloakColorToNetwork();
                    yield break;
                }

                yield return null;
            }
        }

        private void TryRegisterSsmpSatelliteAndLog()
        {
            if (!SSMPBridge.IsAvailable) return;

            if (SSMPBridge.TryRegister())
            {
                Logger.LogInfo("SSMP detected — multiplayer cloak + username sync enabled.");
                PushLocalCloakColorToNetwork();
            }
        }

        /// <summary>
        /// Calls <see cref="SSMPBridge.NotifyLocalColorChanged"/> with <see cref="CloakColorConfig.EffectiveColor"/>.
        /// Safe to call repeatedly. After registration, call at least once: while <see cref="SSMPBridge.IsRegistered"/>
        /// was false, <see cref="SSMPBridge.NotifyLocalColorChanged"/> returned immediately and did not update the satellite.
        /// </summary>
        private void PushLocalCloakColorToNetwork()
        {
            if (!SSMPBridge.IsRegistered) return;
            SSMPBridge.NotifyLocalColorChanged(ColorConfig.EffectiveColor);
        }

        private void OnHeroInstanceSet(HeroController hero)
        {
            var color = ColorConfig.EffectiveColor;
            CloakColorApplier.Apply(hero.gameObject, color);
            CloakColorApplier.SetLocalSceneColor(color);

            SSMPBridge.NotifyLocalColorChanged(color);
            PushLocalUsernameToNetworkAndVisual();
        }

        private void OnConfigColorChanged(CloakColor color)
        {
            if (CloakPaletteConfig.DebugLogging)
            {
                Logger.LogInfo($"Cloak color changed to {color}");
            }

            if (HeroController.SilentInstance != null)
            {
                CloakColorApplier.Apply(HeroController.SilentInstance.gameObject, color);
            }
            CloakColorApplier.SetLocalSceneColor(color);

            SSMPBridge.NotifyLocalColorChanged(color);

            LocalMapMaskTint.Refresh(global::GameManager.instance?.gameMap, color);
            // Also push to the wide / overall map (and any other already-attached local icons).
            MapMaskTint.BroadcastLocalColor(color);

            PushLocalUsernameToNetworkAndVisual();
        }

        private void OnUsernameTintConfigChanged()
        {
            PushLocalUsernameToNetworkAndVisual();
        }

        /// <summary>Network sync only (requires satellite + client addon).</summary>
        private void PushLocalUsernameToNetwork()
        {
            if (!SSMPBridge.IsRegistered) return;

            var rgb = UsernameColorConfig.IsDisabled
                ? CloakColor.Default
                : UsernameColorConfig.ResolveRgb(ColorConfig);

            SSMPBridge.NotifyLocalUsernameColorChanged(rgb);
        }

        /// <summary>Re-tint local name text (works as soon as SSMP username Harmony is active).</summary>
        private void RefreshLocalUsernameVisual()
        {
            UsernameTintCoordinator.ForceRefreshLocalHeroUsername();
        }

        private void PushLocalUsernameToNetworkAndVisual()
        {
            PushLocalUsernameToNetwork();
            RefreshLocalUsernameVisual();
        }
    }
}
