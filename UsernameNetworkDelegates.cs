using System;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor
{
    internal readonly struct UsernameResolveResult
    {
        internal UsernameResolveResult(bool found, bool isLocal, ushort playerId)
        {
            Found = found;
            IsLocal = isLocal;
            PlayerId = playerId;
        }

        internal bool Found { get; }
        internal bool IsLocal { get; }
        internal ushort PlayerId { get; }
    }

    /// <summary>
    /// Callbacks from <c>HornetCloakColor.SSMP</c> into the core plugin for username tinting.
    /// </summary>
    internal static class UsernameNetworkDelegates
    {
        internal static Func<Transform, UsernameResolveResult>? TryResolveUsernameTransform;
        internal static Func<ushort, CloakColor>? GetRemoteUsernameColorOrDefault;
        internal static Func<bool>? GetTeamsEnabled;
        internal static Func<bool>? GetServerCustomUsernameOverridesTeam;
        internal static Func<int>? GetLocalPlayerTeamOrdinal;

        internal static void Clear()
        {
            TryResolveUsernameTransform = null;
            GetRemoteUsernameColorOrDefault = null;
            GetTeamsEnabled = null;
            GetServerCustomUsernameOverridesTeam = null;
            GetLocalPlayerTeamOrdinal = null;
        }
    }
}
