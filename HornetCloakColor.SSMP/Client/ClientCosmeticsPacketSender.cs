using System;
using HornetCloakColor.Shared;
using SSMP.Api.Client;
using SSMP.Api.Client.Networking;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Outbound cosmetics packets (mirrors <c>SSMPEssentials.Client.PacketSender</c>: guarded
    /// <see cref="IClientAddonNetworkSender{T}.SendSingleData"/> only).
    /// </summary>
    internal static class ClientCosmeticsPacketSender
    {
        private static IClientApi? _api;
        private static IClientAddonNetworkSender<PacketId>? _sender;

        internal static void Init(IClientApi api, IClientAddonNetworkSender<PacketId> sender)
        {
            _api = api;
            _sender = sender;
        }

        internal static void TrySendCloakUpdate(CloakNetAppearance appearance)
        {
            if (_api == null || _sender == null || !_api.NetClient.IsConnected) return;

            try
            {
                _sender.SendSingleData(
                    PacketId.CloakColorUpdate,
                    new CloakColorPacket
                    {
                        PlayerId = 0,
                        Color = appearance.Color,
                        TextureSaturationCenti = appearance.TextureSaturationCenti,
                    });
            }
            catch (Exception ex)
            {
                Log.Warn($"HornetCloakColor: failed to send cloak color to server ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        internal static void TrySendUsernameUpdate(bool hasCustomUsernameTint, CloakColor color)
        {
            if (_api == null || _sender == null || !_api.NetClient.IsConnected) return;

            try
            {
                _sender.SendSingleData(
                    PacketId.UsernameColorUpdate,
                    new UsernameColorPacket
                    {
                        PlayerId = 0,
                        Color = color,
                        HasCustomUsernameTint = hasCustomUsernameTint,
                    });
            }
            catch (Exception ex)
            {
                Log.Warn($"HornetCloakColor: failed to send username tint to server ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        /// <summary>
        /// Single flush point for stored local cloak + username (post-connect delay; see
        /// <see cref="HornetCloakColorPlugin.PostConnectSsmpColorResendRoutine"/>).
        /// </summary>
        internal static void TrySendStoredLocal(
            CloakNetAppearance cloak,
            bool hasCustomUsernameTint,
            CloakColor usernameColor)
        {
            TrySendCloakUpdate(cloak);
            TrySendUsernameUpdate(hasCustomUsernameTint, usernameColor);
        }
    }
}
