using System;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Interop
{
    /// <summary>
    /// Hooks for other mods (e.g. SSMP Essentials). Subscribe from your plugin after HornetCloakColor loads.
    /// </summary>
    public static class HornetCloakColorInterop
    {
        /// <summary>
        /// Fired for the local player's cloak color after config presets are resolved and before apply + network send.
        /// Chain multiple subscribers in registration order.
        /// </summary>
        public static event Func<CloakColor, CloakColor>? ModifyLocalCloakColorBeforeApply;

        /// <summary>
        /// Fired when computing the local player's username tint (multiplayer). Return a color to force it, or null to keep the mod's proposal.
        /// </summary>
        public static event Func<UsernameTintContext, Color?>? ResolveLocalUsernameTint;

        /// <summary>
        /// Fired when applying a remote player's synced username tint. Return a color to force it, or null to keep the mod's proposal.
        /// </summary>
        public static event Func<UsernameTintContext, Color?>? ResolveRemoteUsernameTint;

        public static CloakColor ApplyCloakColorModifiers(CloakColor color)
        {
            if (ModifyLocalCloakColorBeforeApply == null) return color;
            var c = color;
            foreach (var handler in ModifyLocalCloakColorBeforeApply.GetInvocationList())
            {
                c = ((Func<CloakColor, CloakColor>)handler)(c);
            }

            return c;
        }

        internal static Color? InvokeLocalUsernameInterop(in UsernameTintContext ctx) =>
            InvokeUsernameChain(ResolveLocalUsernameTint, ctx);

        internal static Color? InvokeRemoteUsernameInterop(in UsernameTintContext ctx) =>
            InvokeUsernameChain(ResolveRemoteUsernameTint, ctx);

        private static Color? InvokeUsernameChain(
            Func<UsernameTintContext, Color?>? chain,
            in UsernameTintContext ctx)
        {
            if (chain == null) return null;
            foreach (var handler in chain.GetInvocationList())
            {
                var r = ((Func<UsernameTintContext, Color?>)handler)(ctx);
                if (r.HasValue) return r;
            }

            return null;
        }

        /// <summary>Local cloak RGB after config + <see cref="ModifyLocalCloakColorBeforeApply"/>.</summary>
        public static CloakColor GetLocalCloakColorAfterModifiers() =>
            HornetCloakColorPlugin.Instance?.ColorConfig.EffectiveColor ?? CloakColor.Default;

        /// <summary>Last synced username tint for a remote player (white if none).</summary>
        public static CloakColor GetSyncedRemoteUsernameRgbOrDefault(ushort networkPlayerId) =>
            UsernameNetworkDelegates.GetRemoteUsernameTintOrNull?.Invoke(networkPlayerId) ?? CloakColor.Default;
    }

    /// <summary>
    /// Context for <see cref="HornetCloakColorInterop.ResolveLocalUsernameTint"/> / <c>ResolveRemoteUsernameTint</c>.
    /// </summary>
    public readonly struct UsernameTintContext
    {
        public UsernameTintContext(
            bool isLocalPlayer,
            ushort networkPlayerId,
            int teamOrdinal,
            bool teamsEnabledOnServer,
            bool serverAllowsCustomUsernameOverTeam,
            Color proposedColor)
        {
            IsLocalPlayer = isLocalPlayer;
            NetworkPlayerId = networkPlayerId;
            TeamOrdinal = teamOrdinal;
            TeamsEnabledOnServer = teamsEnabledOnServer;
            ServerAllowsCustomUsernameOverTeam = serverAllowsCustomUsernameOverTeam;
            ProposedColor = proposedColor;
        }

        public bool IsLocalPlayer { get; }
        public ushort NetworkPlayerId { get; }
        public int TeamOrdinal { get; }
        public bool TeamsEnabledOnServer { get; }
        public bool ServerAllowsCustomUsernameOverTeam { get; }
        public Color ProposedColor { get; }
    }
}
