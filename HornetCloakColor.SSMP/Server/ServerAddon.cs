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
    ///   <item>When a new player enters their first scene, replays every known color to them.</item>
    /// </list>
    ///
    /// <para>Replay timing:</para>
    /// We deliberately wait for <see cref="IServerManager.PlayerEnterSceneEvent"/> rather than
    /// <see cref="IServerManager.PlayerConnectEvent"/>. The connect event fires the moment the
    /// server has queued the SSMP <c>ServerInfo</c> packet for the new client — but the client
    /// hasn't yet received/parsed it, so the addon ID table isn't populated. Any addon packet
    /// we push in that window is silently dropped on the client with
    /// <c>"Addon with ID X has no defined addon packet info"</c>. By the time PlayerEnterScene
    /// fires the client has finished the addon handshake and is safe to send to.
    /// </summary>
    internal class ServerAddon : SSMP.Api.Server.ServerAddon
    {
        protected override string Name => "HornetCloakColor";
        protected override string Version => HornetCloakColorPlugin.ModVersion;
        public override uint ApiVersion => 1;
        public override bool NeedsNetwork => true;

        private readonly Dictionary<ushort, CloakColor> _playerColors = new();

        /// <summary>Players we've already replayed the color list to, so we only do it once per connection.</summary>
        private readonly HashSet<ushort> _seededPlayers = new();

        private IServerApi? _api;
        private IServerAddonNetworkSender<PacketId>? _sender;

        public override void Initialize(IServerApi serverApi)
        {
            _api = serverApi;

            _sender = serverApi.NetServer.GetNetworkSender<PacketId>(this);

            var receiver = serverApi.NetServer.GetNetworkReceiver<PacketId>(this, PacketFactory.Instantiate);
            receiver.RegisterPacketHandler<CloakColorPacket>(PacketId.CloakColorUpdate, OnCloakColorUpdate);

            serverApi.ServerManager.PlayerEnterSceneEvent += OnPlayerEnterScene;
            serverApi.ServerManager.PlayerDisconnectEvent += OnPlayerDisconnect;
            
            Logger.Info("Server addon initialized.");
        }

        private void OnPlayerEnterScene(IServerPlayer player)
        {
            if (_sender == null) return;

            // Only replay once per connection — subsequent scene transitions don't need it.
            if (!_seededPlayers.Add(player.Id)) return;

            var sent = 0;
            foreach (var kvp in _playerColors)
            {
                if (kvp.Key == player.Id) continue;

                _sender.SendSingleData(PacketId.CloakColorUpdate, new CloakColorPacket
                {
                    PlayerId = kvp.Key,
                    Color = kvp.Value,
                }, player.Id);
                sent++;
            }

            Logger.Info($"Seeded {sent} cloak color(s) to newly-arrived player {player.Id}.");
        }

        private void OnPlayerDisconnect(IServerPlayer player)
        {
            _playerColors.Remove(player.Id);
            _seededPlayers.Remove(player.Id);
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
