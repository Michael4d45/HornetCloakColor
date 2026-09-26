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

        internal static Type? MapManager =>
            _mapManager ??= AccessTools.TypeByName("SSMP.Game.Client.MapManager");

        internal static Type? MathVector2 =>
            _mathVector2 ??= AccessTools.TypeByName("SSMP.Math.Vector2");
    }
}
