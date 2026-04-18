using BepInEx;
using HornetCloakColor.Client;
using HornetCloakColor.Server;
using HornetCloakColor.Shared;

namespace HornetCloakColor
{
    /// <summary>
    /// BepInEx entry point. Registers the SSMP client + server addons and wires the
    /// BepInEx configuration so user-driven color changes propagate over the network.
    /// </summary>
    [BepInAutoPlugin(id: "hornet.cloak.color", version: ModVersion)]
    [BepInDependency("ssmp", BepInDependency.DependencyFlags.HardDependency)]
    public partial class HornetCloakColorPlugin : BaseUnityPlugin
    {
        /// <summary>
        /// Keep this in sync with &lt;Version&gt; in HornetCloakColor.csproj. The BepInAutoPlugin
        /// attribute requires a compile-time constant, so we can't read from the csproj directly.
        /// </summary>
        public const string ModVersion = "0.1.0";

        internal static HornetCloakColorPlugin? Instance { get; private set; }
        internal CloakColorConfig ColorConfig { get; private set; } = null!;

        private readonly ClientAddon _clientAddon = new();
        private readonly ServerAddon _serverAddon = new();

        private void Awake()
        {
            Instance = this;

            ColorConfig = new CloakColorConfig(Config);

            SSMP.Api.Client.ClientAddon.RegisterAddon(_clientAddon);
            SSMP.Api.Server.ServerAddon.RegisterAddon(_serverAddon);

            // Whenever the user changes the config, push the new color to local Hornet + network.
            ColorConfig.ColorChanged += OnConfigColorChanged;

            // Also apply the initial color as soon as the hero exists in the scene (menu -> in-game).
            HeroController.OnHeroInstanceSet += OnHeroInstanceSet;

            Logger.LogInfo($"{Name} v{ModVersion} loaded.");
        }

        private void OnHeroInstanceSet(HeroController hero)
        {
            var color = ColorConfig.CurrentColor;
            CloakColorApplier.Apply(hero.gameObject, color);

            // Make sure the server and other clients know about our color from the start.
            ClientAddon.Instance?.SetLocalColor(color);
        }

        private void OnConfigColorChanged(CloakColor color)
        {
            if (ColorConfig.DebugLogging.Value)
            {
                Logger.LogInfo($"Cloak color changed to {color}");
            }

            ClientAddon.Instance?.SetLocalColor(color);
        }
    }
}
