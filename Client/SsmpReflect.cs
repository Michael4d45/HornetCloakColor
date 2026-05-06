using System;
using HarmonyLib;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Lazy-cached <c>AccessTools.TypeByName</c> lookups for SSMP types. All properties are
    /// <c>null</c> when SSMP is not loaded. Use only from code paths already gated by
    /// <see cref="HornetCloakColor.SSMPBridge.IsAvailable"/> or after checking for null.
    /// </summary>
    internal static class SsmpReflect
    {
        private static Type? _mapManager;
        private static Type? _mathVector2;
        private static Type? _clientManager;
        private static Type? _serverManager;
        private static Type? _serverPlayerData;
        private static Type? _clientPlayerAlreadyInScene;

        internal static Type? MapManager =>
            _mapManager ??= AccessTools.TypeByName("SSMP.Game.Client.MapManager");

        internal static Type? MathVector2 =>
            _mathVector2 ??= AccessTools.TypeByName("SSMP.Math.Vector2");

        internal static Type? ClientManager =>
            _clientManager ??= AccessTools.TypeByName("SSMP.Game.Client.ClientManager");

        internal static Type? ServerManager =>
            _serverManager ??= AccessTools.TypeByName("SSMP.Game.Server.ServerManager");

        internal static Type? ServerPlayerData =>
            _serverPlayerData ??= AccessTools.TypeByName("SSMP.Game.Server.ServerPlayerData");

        internal static Type? ClientPlayerAlreadyInScene =>
            _clientPlayerAlreadyInScene ??=
                AccessTools.TypeByName("SSMP.Networking.Packet.Data.ClientPlayerAlreadyInScene");
    }
}
