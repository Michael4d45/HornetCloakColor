using System;
using System.Collections.Generic;
using HornetCloakColor;
using HornetCloakColor.Shared;
using SSMP.Api.Client;
using SSMP.Api.Client.Networking;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// SSMP client-side addon. Responsible for:
    /// <list type="bullet">
    ///   <item>Sending the local player's cloak color to the server.</item>
    ///   <item>Receiving other players' cloak colors from the server.</item>
    ///   <item>Applying colors to the correct player objects as they enter scenes.</item>
    ///   <item>Username tint sync + server rule for team override.</item>
    /// </list>
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
        /// Cache of the latest known color for each remote player. We keep this so that when a
        /// player enters the local scene we can reapply the tint (the SSMP renderer may be
        /// reparented/rebuilt between scenes, which resets material color).
        /// </summary>
        private readonly Dictionary<ushort, CloakColor> _playerColors = new();

        private readonly Dictionary<ushort, CloakColor> _playerUsernameColors = new();

        private IClientApi? _api;
        private IClientAddonNetworkSender<PacketId>? _sender;

        /// <summary>The color the local player most recently chose, broadcast to the server.</summary>
        private CloakColor _localColor = CloakColor.Default;

        private CloakColor _localUsernameColor = CloakColor.Default;

        private bool _serverCustomUsernameOverridesTeam;

        public override void Initialize(IClientApi clientApi)
        {
            Instance = this;
            _api = clientApi;

            _sender = clientApi.NetClient.GetNetworkSender<PacketId>(this);

            var receiver = clientApi.NetClient.GetNetworkReceiver<PacketId>(this, PacketFactory.Instantiate);
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
            UsernameNetworkDelegates.GetRemoteUsernameColorOrDefault = GetRemoteUsernameColorOrDefault;
            UsernameNetworkDelegates.GetTeamsEnabled = () => _api!.ClientManager.ServerSettings.TeamsEnabled;
            UsernameNetworkDelegates.GetServerCustomUsernameOverridesTeam = () => _serverCustomUsernameOverridesTeam;
            UsernameNetworkDelegates.GetLocalPlayerTeamOrdinal = () =>
                (int)_api!.ClientManager.PlayerManager.LocalPlayerTeam;
        }

        /// <summary>
        /// Remember the local player's color and broadcast it to the server.
        /// The local hero's visual is applied by the plugin directly so this path also runs
        /// when SSMP isn't loaded. Safe to call before connecting — the send is a no-op then.
        /// </summary>
        public void SetLocalColor(CloakColor color)
        {
            _localColor = color;
            SendLocalColor();
        }

        /// <summary>Broadcast username tint (or white to clear) while connected.</summary>
        public void SetLocalUsernameColor(CloakColor color)
        {
            _localUsernameColor = color;
            SendLocalUsernameColor();
        }

        /// <summary>Latest color for another player's cloak (from server). Used for map mask tint.</summary>
        internal CloakColor GetRemoteMapColorOrDefault(ushort playerId) =>
            _playerColors.TryGetValue(playerId, out var c) ? c : CloakColor.Default;

        internal CloakColor GetRemoteUsernameColorOrDefault(ushort playerId) =>
            _playerUsernameColors.TryGetValue(playerId, out var c) ? c : CloakColor.Default;

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

        private void SendLocalColor()
        {
            if (_api == null || _sender == null) return;
            if (!_api.NetClient.IsConnected) return;

            _sender.SendSingleData(PacketId.CloakColorUpdate, new CloakColorPacket
            {
                PlayerId = 0, // ignored by server; it infers sender
                Color = _localColor,
            });
        }

        private void SendLocalUsernameColor()
        {
            if (_api == null || _sender == null) return;
            if (!_api.NetClient.IsConnected) return;

            _sender.SendSingleData(PacketId.UsernameColorUpdate, new UsernameColorPacket
            {
                PlayerId = 0,
                Color = _localUsernameColor,
            });
        }

        private void OnConnected()
        {
            PushUsernameDelegates();
            SendLocalColor();
            SendLocalUsernameColor();
        }

        private void OnDisconnected()
        {
            UsernameNetworkDelegates.Clear();
            _playerUsernameColors.Clear();
            _serverCustomUsernameOverridesTeam = false;
        }

        private void OnPlayerEnterScene(IClientPlayer player)
        {
            if (_playerColors.TryGetValue(player.Id, out var color) && player.PlayerObject != null)
            {
                CloakColorApplier.Apply(player.PlayerObject, color);
                // Skin/tk2d can stomp materials after this callback — reinforce tint across frames.
                if (!IsLocalHeroPlayer(player))
                    MpRemoteCloakReapply.Schedule(player.PlayerObject, color);
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
            _playerColors.Remove(player.Id);
            _playerUsernameColors.Remove(player.Id);
        }

        private void OnCloakColorUpdate(CloakColorPacket data)
        {
            _playerColors[data.PlayerId] = data.Color;

            PlayerMapMaskTintRegistry.SetColor(data.PlayerId, data.Color);

            var player = _api?.ClientManager.GetPlayer(data.PlayerId);
            if (player?.PlayerObject != null)
            {
                CloakColorApplier.Apply(player.PlayerObject, data.Color);
                MpRemoteCloakReapply.Schedule(player.PlayerObject, data.Color);
            }
        }

        private void OnServerUsernameColorRules(ServerUsernameColorRulesPacket data)
        {
            _serverCustomUsernameOverridesTeam = data.CustomUsernameColorsOverrideTeamColors;
        }

        private void OnUsernameColorUpdate(UsernameColorPacket data)
        {
            if (data.Color == CloakColor.Default)
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

            if (_playerUsernameColors.TryGetValue(playerId, out var c) && c != CloakColor.Default)
            {
                UsernameTintCoordinator.ApplyRemoteUsernameAfterSync(tmp, playerId, (int)player.Team);
            }
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
