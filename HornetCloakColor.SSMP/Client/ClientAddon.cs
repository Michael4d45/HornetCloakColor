using System;
using System.Collections.Generic;
using HornetCloakColor;
using HornetCloakColor.Shared;
using SSMP.Api.Client;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// SSMP client-side addon. Inbound handlers + presentation; outbound cosmetics use
    /// <see cref="ClientCosmeticsPacketSender"/> (SSMP.Essentials-style single <c>SendSingleData</c> path).
    /// </summary>
    internal class ClientAddon : SSMP.Api.Client.ClientAddon
    {
        private const string UsernameObjectName = "Username";

        protected override string Name => "HornetCloakColor";
        protected override string Version => HornetCloakColorPlugin.ModVersion;
        public override uint ApiVersion => 1;
        public override bool NeedsNetwork => true;

        /// <summary>Singleton exposed so the plugin / config can trigger a resend on color change.</summary>
        internal static ClientAddon? Instance { get; private set; }

        /// <summary>
        /// Cache of the latest known cloak appearance (RGB + texture saturation) per remote player for
        /// reapply on scene enter / material rebuilds.
        /// </summary>
        private readonly Dictionary<ushort, CloakNetAppearance> _playerCloakAppearances = new();

        private readonly Dictionary<ushort, CloakColor> _playerUsernameColors = new();

        private IClientApi? _api;

        /// <summary>The local player's last announced cloak appearance (RGB + texture saturation).</summary>
        private CloakNetAppearance _localCloakAppearance = CloakNetAppearance.Default;

        private CloakColor _localUsernameColor = CloakColor.Default;

        /// <summary>When false, the next send tells the server to drop our username tint (Mod Menu Disabled).</summary>
        private bool _localUsernameHasCustomTint;

        private bool _serverCustomUsernameOverridesTeam;

        public override void Initialize(IClientApi clientApi)
        {
            Instance = this;
            _api = clientApi;

            var sender = clientApi.NetClient.GetNetworkSender<PacketId>(this);
            ClientCosmeticsPacketSender.Init(clientApi, sender);

            var receiver = clientApi.NetClient.GetNetworkReceiver<PacketId>(this, ClientAddonReceivePacketFactory.Instantiate);
            receiver.RegisterPacketHandler<CloakColorPacket>(PacketId.CloakColorUpdate, OnCloakColorUpdate);
            receiver.RegisterPacketHandler<UsernameColorPacket>(PacketId.UsernameColorUpdate, OnUsernameColorUpdate);
            receiver.RegisterPacketHandler<ServerUsernameColorRulesPacket>(
                PacketId.ServerUsernameColorRules,
                OnServerUsernameColorRules);

            clientApi.ClientManager.ConnectEvent += OnConnected;
            clientApi.ClientManager.DisconnectEvent += OnDisconnected;
            clientApi.ClientManager.PlayerEnterSceneEvent += OnPlayerEnterScene;
            clientApi.ClientManager.PlayerDisconnectEvent += OnPlayerDisconnect;

            PushUsernameDelegates();

            Log.Info("Client addon initialized.");
        }

        private void PushUsernameDelegates()
        {
            UsernameNetworkDelegates.TryResolveUsernameTransform = TryResolveUsernameTransform;
            UsernameNetworkDelegates.GetRemoteUsernameTintOrNull = GetRemoteUsernameTintOrNull;
            UsernameNetworkDelegates.GetTeamsEnabled = () => _api!.ClientManager.ServerSettings.TeamsEnabled;
            UsernameNetworkDelegates.GetServerCustomUsernameOverridesTeam = () => _serverCustomUsernameOverridesTeam;
            UsernameNetworkDelegates.GetLocalPlayerTeamOrdinal = () =>
                (int)_api!.ClientManager.PlayerManager.LocalPlayerTeam;
        }

        /// <summary>
        /// Remember the local player's cloak appearance and broadcast it to the server.
        /// The local hero's visual is applied by the plugin directly so this path also runs
        /// when SSMP isn't loaded. Safe to call before connecting — the send is a no-op then.
        /// </summary>
        public void SetLocalCloakAppearance(CloakNetAppearance appearance)
        {
            _localCloakAppearance = appearance;
            ClientCosmeticsPacketSender.TrySendCloakUpdate(_localCloakAppearance);
        }

        /// <summary>Broadcast username tint while connected (including literal white / match-cloak default).</summary>
        public void SetLocalUsernameTint(bool hasCustomUsernameTint, CloakColor color)
        {
            _localUsernameHasCustomTint = hasCustomUsernameTint;
            _localUsernameColor = color;
            ClientCosmeticsPacketSender.TrySendUsernameUpdate(_localUsernameHasCustomTint, _localUsernameColor);
        }

        /// <summary>Latest color for another player's cloak (from server). Used for map mask tint.</summary>
        internal CloakColor GetRemoteMapColorOrDefault(ushort playerId) =>
            _playerCloakAppearances.TryGetValue(playerId, out var a) ? a.Color : CloakColor.Default;

        internal CloakColor? GetRemoteUsernameTintOrNull(ushort playerId) =>
            _playerUsernameColors.TryGetValue(playerId, out var c) ? c : null;

        private UsernameResolveResult TryResolveUsernameTransform(Transform t)
        {
            if (t == null) return default;

            if (HeroController.instance != null && t.IsChildOf(HeroController.instance.transform))
            {
                if (TryGetLocalNetworkPlayerId(out var id)) return new UsernameResolveResult(true, true, id);
                return new UsernameResolveResult(true, true, 0);
            }

            // SSMP.PlayerManager.SpawnPlayer calls AddNameToPlayer *before* assigning
            // ClientPlayerData.PlayerContainer. The Harmony postfix on ChangeNameColor therefore runs when
            // IsChildOf(p.PlayerContainer) cannot match yet — resolve by container object name instead.
            if (TryResolveRemoteByPlayerContainerAncestor(t, out var idFromAncestor))
                return new UsernameResolveResult(true, false, idFromAncestor);

            if (_api == null) return default;

            foreach (var p in _api.ClientManager.Players)
            {
                if (p.PlayerContainer == null) continue;
                if (t.IsChildOf(p.PlayerContainer.transform)) return new UsernameResolveResult(true, false, p.Id);
            }

            return default;
        }

        /// <summary>
        /// Mirrors <c>SSMP.Game.Client.PlayerManager</c> naming: <c>Player Container {ushort id}</c>.
        /// </summary>
        private static bool TryResolveRemoteByPlayerContainerAncestor(Transform t, out ushort playerId)
        {
            playerId = 0;
            const string prefix = "Player Container ";
            for (var p = t; p != null; p = p.parent)
            {
                var name = p.name;
                if (!name.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                var suffix = name.Substring(prefix.Length);
                return ushort.TryParse(suffix, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out playerId);
            }

            return false;
        }

        private bool TryGetLocalNetworkPlayerId(out ushort id)
        {
            id = 0;
            if (_api == null || HeroController.instance == null) return false;

            foreach (var p in _api.ClientManager.Players)
            {
                if (p.PlayerObject != null
                    && ReferenceEquals(p.PlayerObject, HeroController.instance.gameObject))
                {
                    id = p.Id;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Used by <see cref="SSMPBridge.ResendStoredLocalColorsToServer"/> (post-connect delayed flush only).
        /// </summary>
        internal void ForceResendLocalColorsToServer() =>
            ClientCosmeticsPacketSender.TrySendStoredLocal(
                _localCloakAppearance,
                _localUsernameHasCustomTint,
                _localUsernameColor);

        private void OnConnected()
        {
            PushUsernameDelegates();
            ClientCosmeticsPacketSender.TrySendStoredLocal(
                _localCloakAppearance,
                _localUsernameHasCustomTint,
                _localUsernameColor);
            SSMPBridge.SchedulePostConnectColorResend();
        }

        private void OnDisconnected()
        {
            UsernameNetworkDelegates.Clear();
            _playerUsernameColors.Clear();
            _serverCustomUsernameOverridesTeam = false;
        }

        private void OnPlayerEnterScene(IClientPlayer player)
        {
            if (_playerCloakAppearances.TryGetValue(player.Id, out var appearance) && player.PlayerObject != null)
            {
                CloakColorApplier.Apply(
                    player.PlayerObject,
                    appearance.Color,
                    appearance.TextureSaturationMultiplier);
                // Skin/tk2d can stomp materials after this callback — reinforce tint across frames.
                if (!IsLocalHeroPlayer(player))
                    MpRemoteCloakReapply.Schedule(player.PlayerObject, appearance);
            }

            // Spawn finishes assigning PlayerContainer after the initial ChangeNameColor call; reapply any
            // cached custom username RGB now that the Username TMP exists and delegates can resolve.
            // Skip the local hero — their tint is driven by config + Harmony on ChangeNameColor only.
            if (!IsLocalHeroPlayer(player))
                RefreshRemoteUsernameVisual(player.Id);
        }

        private static bool IsLocalHeroPlayer(IClientPlayer player)
        {
            var hero = HeroController.instance;
            return hero != null
                   && player.PlayerObject != null
                   && ReferenceEquals(player.PlayerObject, hero.gameObject);
        }

        private void OnPlayerDisconnect(IClientPlayer player)
        {
            _playerCloakAppearances.Remove(player.Id);
            _playerUsernameColors.Remove(player.Id);
        }

        private void OnCloakColorUpdate(CloakColorPacket data)
        {
            var appearance = new CloakNetAppearance(data.Color, data.TextureSaturationCenti);
            _playerCloakAppearances[data.PlayerId] = appearance;

            PlayerMapMaskTintRegistry.SetColor(data.PlayerId, appearance.Color);

            var player = _api?.ClientManager.GetPlayer(data.PlayerId);
            if (player?.PlayerObject != null)
            {
                CloakColorApplier.Apply(
                    player.PlayerObject,
                    appearance.Color,
                    appearance.TextureSaturationMultiplier);
                MpRemoteCloakReapply.Schedule(player.PlayerObject, appearance);
            }
        }

        private void OnServerUsernameColorRules(ServerUsernameColorRulesPacket data)
        {
            _serverCustomUsernameOverridesTeam = data.CustomUsernameColorsOverrideTeamColors;
        }

        private void OnUsernameColorUpdate(UsernameColorPacket data)
        {
            if (!data.HasCustomUsernameTint)
                _playerUsernameColors.Remove(data.PlayerId);
            else
                _playerUsernameColors[data.PlayerId] = data.Color;

            RefreshRemoteUsernameVisual(data.PlayerId);
        }

        private void RefreshRemoteUsernameVisual(ushort playerId)
        {
            if (_api == null) return;
            if (!_api.ClientManager.TryGetPlayer(playerId, out var player) || player == null) return;

            var container = player.PlayerContainer;
            if (container == null) return;

            var nameGo = FindDeepChildByName(container, UsernameObjectName);
            if (nameGo == null) return;

            var tmp = UsernameTmpCompat.FindOnGameObject(nameGo);
            if (tmp == null) return;

            SsmpUsernameVanillaColors.ApplyTeamColor(tmp, player.Team);

            if (_playerUsernameColors.ContainsKey(playerId))
                UsernameTintCoordinator.ApplyRemoteUsernameAfterSync(tmp, playerId, (int)player.Team);
        }

        private static GameObject? FindDeepChildByName(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t.gameObject;
            }

            return null;
        }
    }
}
