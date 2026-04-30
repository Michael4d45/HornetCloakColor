using BepInEx;
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
    [BepInDependency(SSMPBridge.SSMPGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public partial class HornetCloakColorPlugin : BaseUnityPlugin
    {
        /// <summary>
        /// Keep this in sync with &lt;Version&gt; in HornetCloakColor.csproj. The BepInAutoPlugin
        /// attribute requires a compile-time constant, so we can't read from the csproj directly.
        /// </summary>
        public const string ModVersion = "1.9.0";

        internal static HornetCloakColorPlugin? Instance { get; private set; }
        internal static ManualLogSource? LogSource { get; private set; }
        internal CloakColorConfig ColorConfig { get; private set; } = null!;

        private void Awake()
        {
            Instance = this;
            LogSource = Logger;

            CloakPaletteConfig.Load();

            // Stand up the scene-wide scanner before the hero loads so orphan Hornet
            // renderers (steam-vent recoil, item-get pose, etc.) get tinted as soon as
            // they appear, even if the hero's hierarchy never owns them.
            CloakSceneScanner.EnsureCreated();

            ColorConfig = new CloakColorConfig(Config);
            CloakColorApplier.SetLocalSceneColor(ColorConfig.CurrentColor);

            MapMaskHarmonyPatcher.Apply();

            if (SSMPBridge.TryRegister())
            {
                Logger.LogInfo("SSMP detected — multiplayer cloak sync enabled.");
            }
            else
            {
                Logger.LogInfo("SSMP not detected — running solo (your cloak only).");
            }

            ColorConfig.ColorChanged += OnConfigColorChanged;

            HeroController.OnHeroInstanceSet += OnHeroInstanceSet;

            Logger.LogInfo($"{Name} v{ModVersion} loaded.");
        }

        private void OnHeroInstanceSet(HeroController hero)
        {
            var color = ColorConfig.CurrentColor;
            CloakColorApplier.Apply(hero.gameObject, color);
            CloakColorApplier.SetLocalSceneColor(color);

            SSMPBridge.NotifyLocalColorChanged(color);
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
        }
    }
}
