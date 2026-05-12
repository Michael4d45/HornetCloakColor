using System.Collections.Generic;
using HornetCloakColor.Shared;
using SSMP.Api.Server;
using SSMP.Api.Server.Networking;

namespace HornetCloakColor.Server
{
    /// <summary>
    /// SSMP server addon: authoritative cosmetics relay aligned with <c>SSMPEssentials.Server.Server</c>
    /// (<see cref="IServerManager.PlayerConnectEvent"/> join snapshot + <c>PacketSender</c>-style broadcast).
    /// </summary>
    internal class ServerAddon : SSMP.Api.Server.ServerAddon
    {
        protected override string Name => "HornetCloakColor";
        protected override string Version => HornetCloakColorPlugin.ModVersion;
        public override uint ApiVersion => 1;
        public override bool NeedsNetwork => true;

        private readonly PlayerCosmeticsTracker _tracker = new();
        private readonly bool _customUsernameColorsOverrideTeamColors =
            ServerUsernameRulesStore.LoadCustomUsernameColorsOverrideTeamColors();

        /// <summary>
        /// One idempotent replay on first <see cref="IServerManager.PlayerEnterSceneEvent"/> per connection
        /// (covers rare client addon-handshake timing; duplicates are harmless on receivers).
        /// </summary>
        private readonly HashSet<ushort> _sceneCatchUpSent = new();

        private IServerApi? _api;

        public override void Initialize(IServerApi serverApi)
        {
            _api = serverApi;

            var sender = serverApi.NetServer.GetNetworkSender<PacketId>(this);
            ServerCosmeticsPacketSender.Init(sender);

            var receiver = serverApi.NetServer.GetNetworkReceiver<PacketId>(this, ServerAddonReceivePacketFactory.Instantiate);
            receiver.RegisterPacketHandler<CloakColorPacket>(PacketId.CloakColorUpdate, OnCloakColorUpdate);
            receiver.RegisterPacketHandler<UsernameColorPacket>(PacketId.UsernameColorUpdate, OnUsernameColorUpdate);

            serverApi.ServerManager.PlayerConnectEvent += OnPlayerConnect;
            serverApi.ServerManager.PlayerEnterSceneEvent += OnPlayerEnterSceneFirstCatchUp;
            serverApi.ServerManager.PlayerDisconnectEvent += OnPlayerDisconnect;

            Log.Info(
                _customUsernameColorsOverrideTeamColors
                    ? "Server addon initialized (custom username colors may override team colors)."
                    : "Server addon initialized (team colors take precedence over custom username colors when teams are used).");
        }

        /// <summary>Primary join path — same role as <c>SSMPEssentials.Server.Server.SendJoinInfo</c>.</summary>
        private void OnPlayerConnect(IServerPlayer player) =>
            PushJoinSnapshotTo(player, "PlayerConnect");

        private void OnPlayerEnterSceneFirstCatchUp(IServerPlayer player)
        {
            if (!_sceneCatchUpSent.Add(player.Id))
                return;

            PushJoinSnapshotTo(player, "first scene catch-up");
        }

        private void PushJoinSnapshotTo(IServerPlayer player, string reason)
        {
            ServerCosmeticsPacketSender.SendUsernameRulesTo(player.Id, _customUsernameColorsOverrideTeamColors);
            ServerCosmeticsPacketSender.SendAllOtherPlayersCloaksTo(player.Id, _tracker);
            ServerCosmeticsPacketSender.SendAllOtherPlayersUsernameTintsTo(player.Id, _tracker);

            Log.Info($"Cosmetics snapshot ({reason}) → player {player.Id} (rules + others' cloak/username).");
        }

        private void OnPlayerDisconnect(IServerPlayer player)
        {
            _tracker.RemovePlayer(player.Id);
            _sceneCatchUpSent.Remove(player.Id);
        }

        private void OnCloakColorUpdate(ushort senderId, CloakColorPacket data)
        {
            var appearance = new CloakNetAppearance(data.Color, data.TextureSaturationCenti);
            _tracker.SetCloak(senderId, appearance);

            if (_api == null) return;

            ServerCosmeticsPacketSender.BroadcastCloakToOthers(senderId, appearance, _api.ServerManager.Players);
        }

        private void OnUsernameColorUpdate(ushort senderId, UsernameColorPacket data)
        {
            _tracker.SetUsernameTint(senderId, data.HasCustomUsernameTint, data.Color);

            if (_api == null) return;

            ServerCosmeticsPacketSender.BroadcastUsernameTintToOthers(
                senderId,
                data.Color,
                data.HasCustomUsernameTint,
                _api.ServerManager.Players);
        }
    }
}
