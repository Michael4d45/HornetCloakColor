using System.Collections.Generic;
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
    /// </list>
    /// </summary>
    internal class ClientAddon : SSMP.Api.Client.ClientAddon
    {
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

        private IClientApi? _api;
        private IClientAddonNetworkSender<PacketId>? _sender;

        /// <summary>The color the local player most recently chose, broadcast to the server.</summary>
        private CloakColor _localColor = CloakColor.Default;

        public override void Initialize(IClientApi clientApi)
        {
            Instance = this;
            _api = clientApi;
            Log.SetLogger(Logger);

            _sender = clientApi.NetClient.GetNetworkSender<PacketId>(this);

            var receiver = clientApi.NetClient.GetNetworkReceiver<PacketId>(this, PacketFactory.Instantiate);
            receiver.RegisterPacketHandler<CloakColorPacket>(PacketId.CloakColorUpdate, OnCloakColorUpdate);

            clientApi.ClientManager.ConnectEvent += OnConnected;
            clientApi.ClientManager.PlayerEnterSceneEvent += OnPlayerEnterScene;
            clientApi.ClientManager.PlayerDisconnectEvent += OnPlayerDisconnect;

            Log.Info("Client addon initialized.");
        }

        /// <summary>
        /// Update the local player's cloak color, apply it locally, and broadcast it to the server.
        /// Safe to call even when not connected — network send is a no-op in that case.
        /// </summary>
        public void SetLocalColor(CloakColor color)
        {
            _localColor = color;

            if (HeroController.SilentInstance != null)
            {
                CloakColorApplier.Apply(HeroController.SilentInstance.gameObject, color);
            }

            SendLocalColor();
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

        private void OnConnected()
        {
            // Resend our color on (re)connect so the server and all other clients learn it.
            SendLocalColor();
        }

        private void OnPlayerEnterScene(IClientPlayer player)
        {
            if (_playerColors.TryGetValue(player.Id, out var color))
            {
                CloakColorApplier.Apply(player.PlayerObject, color);
            }
        }

        private void OnPlayerDisconnect(IClientPlayer player)
        {
            _playerColors.Remove(player.Id);
        }

        private void OnCloakColorUpdate(CloakColorPacket data)
        {
            _playerColors[data.PlayerId] = data.Color;

            var player = _api?.ClientManager.GetPlayer(data.PlayerId);
            if (player?.PlayerObject != null)
            {
                CloakColorApplier.Apply(player.PlayerObject, data.Color);
            }
        }
    }
}
