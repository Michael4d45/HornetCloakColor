using HornetCloakColor.Shared;

namespace HornetCloakColor.SSMPIntegration
{
    /// <summary>
    /// Public entry loaded via reflection from the main plugin when SSMP is present.
    /// Keeps all SSMP type references in this satellite assembly so the core DLL loads solo.
    /// </summary>
    public static class SatelliteEntry
    {
        public static void Register()
        {
            var client = new Client.ClientAddon();
            var server = new Server.ServerAddon();
            SSMP.Api.Client.ClientAddon.RegisterAddon(client);
            SSMP.Api.Server.ServerAddon.RegisterAddon(server);
        }

        public static void NotifyLocalColorChanged(CloakColor color)
        {
            Client.ClientAddon.Instance?.SetLocalColor(color);
        }

        public static CloakColor GetRemoteMapColorOrDefault(ushort playerId) =>
            Client.ClientAddon.Instance?.GetRemoteMapColorOrDefault(playerId) ?? CloakColor.Default;
    }
}
