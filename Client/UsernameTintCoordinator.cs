using HornetCloakColor.Interop;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Runs after SSMP applies vanilla team / white username colors, optionally overriding with
    /// synced custom tints per server rules.
    /// </summary>
    internal static class UsernameTintCoordinator
    {
        internal static void OnAfterSsmpChangeNameColor(Component textMesh, object teamEnumBoxed)
        {
            if (textMesh == null) return;
            if (!UsernameColorHarmonyPatcher.IsActive) return;

            var teamOrdinal = System.Convert.ToInt32(teamEnumBoxed);
            var tr = textMesh.transform;

            var teamsEnabled = UsernameNetworkDelegates.GetTeamsEnabled?.Invoke() ?? false;
            var serverOverride = UsernameNetworkDelegates.GetServerCustomUsernameOverridesTeam?.Invoke() ?? false;

            // Local hero: do not require SSMP client-addon delegates (they wire up after our plugin Awake).
            if (HeroController.instance != null && tr.IsChildOf(HeroController.instance.transform))
            {
                ApplyLocal(textMesh, teamOrdinal, teamsEnabled, serverOverride);
                return;
            }

            if (UsernameNetworkDelegates.TryResolveUsernameTransform == null) return;
            var res = UsernameNetworkDelegates.TryResolveUsernameTransform(tr);
            if (!res.Found || res.IsLocal) return;

            ApplyRemote(textMesh, res.PlayerId, teamOrdinal, teamsEnabled, serverOverride);
        }

        /// <summary>
        /// Called when a remote player's synced username RGB changes (SSMP does not re-run name color).
        /// </summary>
        internal static void ApplyRemoteUsernameAfterSync(Component textMesh, ushort playerId, int teamOrdinal)
        {
            if (textMesh == null) return;
            if (!UsernameColorHarmonyPatcher.IsActive) return;

            var teamsEnabled = UsernameNetworkDelegates.GetTeamsEnabled?.Invoke() ?? false;
            var serverOverride = UsernameNetworkDelegates.GetServerCustomUsernameOverridesTeam?.Invoke() ?? false;
            ApplyRemote(textMesh, playerId, teamOrdinal, teamsEnabled, serverOverride);
        }

        /// <summary>
        /// Re-apply local username tint after config changes (SSMP does not re-run name color).
        /// </summary>
        internal static void ForceRefreshLocalHeroUsername()
        {
            if (!UsernameColorHarmonyPatcher.IsActive) return;
            var hero = global::HeroController.instance;
            if (hero == null) return;

            var tmp = UsernameTmpCompat.FindUnderHero(hero.gameObject);
            if (tmp == null) return;

            var teamOrd = UsernameNetworkDelegates.GetLocalPlayerTeamOrdinal?.Invoke() ?? 0;
            UsernameVanillaTintTable.Apply(tmp, teamOrd);
            OnAfterSsmpChangeNameColor(tmp, teamOrd);
        }

        private static void ApplyLocal(
            Component textMesh,
            int teamOrdinal,
            bool teamsEnabled,
            bool serverAllowsCustomOverTeam)
        {
            var plugin = HornetCloakColorPlugin.Instance;
            var cfg = plugin?.UsernameColorConfig;
            if (cfg == null) return;

            if (cfg.IsDisabled) return;

            if (!ShouldUseCustomUsernameTint(teamOrdinal, teamsEnabled, serverAllowsCustomOverTeam)) return;

            var rgb = cfg.ResolveRgb(plugin!.ColorConfig);
            var proposed = rgb.ToUnityColor();
            var ctx = new UsernameTintContext(
                true,
                0,
                teamOrdinal,
                teamsEnabled,
                serverAllowsCustomOverTeam,
                proposed);
            var interop = HornetCloakColorInterop.InvokeLocalUsernameInterop(ctx);
            UsernameTmpCompat.SetColor(textMesh, interop ?? proposed);
        }

        private static void ApplyRemote(
            Component textMesh,
            ushort playerId,
            int teamOrdinal,
            bool teamsEnabled,
            bool serverAllowsCustomOverTeam)
        {
            var remoteOpt = UsernameNetworkDelegates.GetRemoteUsernameTintOrNull?.Invoke(playerId);
            if (remoteOpt == null) return;
            var remote = remoteOpt.Value;

            if (!ShouldUseCustomUsernameTint(teamOrdinal, teamsEnabled, serverAllowsCustomOverTeam)) return;

            var proposed = remote.ToUnityColor();
            var ctx = new UsernameTintContext(
                false,
                playerId,
                teamOrdinal,
                teamsEnabled,
                serverAllowsCustomOverTeam,
                proposed);
            var interop = HornetCloakColorInterop.InvokeRemoteUsernameInterop(ctx);
            UsernameTmpCompat.SetColor(textMesh, interop ?? proposed);
        }

        private static bool ShouldUseCustomUsernameTint(
            int teamOrdinal,
            bool teamsEnabled,
            bool serverAllowsCustomOverTeam)
        {
            if (!teamsEnabled || teamOrdinal == 0) return true;
            return serverAllowsCustomOverTeam;
        }
    }
}
