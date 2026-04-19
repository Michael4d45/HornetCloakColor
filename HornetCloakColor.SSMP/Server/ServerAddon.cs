using System.Collections.Generic;
using HornetCloakColor.Shared;
using SSMP.Api.Server;
using SSMP.Api.Server.Networking;

namespace HornetCloakColor.Server
{
    /// <summary>
    /// SSMP server-side addon. Acts as a simple relay + memory store:
    /// <list type="bullet">
    ///   <item>Stores each connected player's cloak color.</item>
    ///   <item>Broadcasts color updates to all other players.</item>
    ///   <item>When a new player joins, replays every known color to them.</item>
    /// </list>
    /// </summary>
    internal class ServerAddon : SSMP.Api.Server.ServerAddon
    {
        protected override string Name => "HornetCloakColor";
        protected override string Version => HornetCloakColorPlugin.ModVersion;
        public override uint ApiVersion => 1;
        public override bool NeedsNetwork => true;

        private readonly Dictionary<ushort, CloakColor> _playerColors = new();

        private IServerApi? _api;
        private IServerAddonNetworkSender<PacketId>? _sender;

        public override void Initialize(IServerApi serverApi)
        {
            _api = serverApi;

            _sender = serverApi.NetServer.GetNetworkSender<PacketId>(this);

            var receiver = serverApi.NetServer.GetNetworkReceiver<PacketId>(this, PacketFactory.Instantiate);
            receiver.RegisterPacketHandler<CloakColorPacket>(PacketId.CloakColorUpdate, OnCloakColorUpdate);

            serverApi.ServerManager.PlayerConnectEvent += OnPlayerConnect;
            serverApi.ServerManager.PlayerDisconnectEvent += OnPlayerDisconnect;

            Log.Info("Server addon initialized.");
        }

        private void OnPlayerConnect(IServerPlayer player)
        {
            // Replay every known color to the newly connected player so they see existing cloaks.
            if (_sender == null) return;

            foreach (var kvp in _playerColors)
            {
                if (kvp.Key == player.Id) continue;

                _sender.SendSingleData(PacketId.CloakColorUpdate, new CloakColorPacket
                {
                    PlayerId = kvp.Key,
                    Color = kvp.Value,
                }, player.Id);
            }
        }

        private void OnPlayerDisconnect(IServerPlayer player)
        {
            _playerColors.Remove(player.Id);
        }

        private void OnCloakColorUpdate(ushort senderId, CloakColorPacket data)
        {
            _playerColors[senderId] = data.Color;

            if (_api == null || _sender == null) return;

            // Broadcast to every other player. We always stamp the real sender ID so clients
            // can't spoof colors for other users.
            foreach (var other in _api.ServerManager.Players)
            {
                if (other.Id == senderId) continue;

                _sender.SendSingleData(PacketId.CloakColorUpdate, new CloakColorPacket
                {
                    PlayerId = senderId,
                    Color = data.Color,
                }, other.Id);
            }
        }
    }
}
