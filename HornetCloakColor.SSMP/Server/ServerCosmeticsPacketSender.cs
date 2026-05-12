using System.Collections.Generic;
using HornetCloakColor.Shared;
using SSMP.Api.Server;
using SSMP.Api.Server.Networking;

namespace HornetCloakColor.Server
{
    /// <summary>
    /// Central outbound cosmetics traffic (mirrors <c>SSMPEssentials.Server.PacketSender</c>):
    /// <see cref="SendCollectionData"/> for fan-out and join snapshots; <see cref="SendSingleData"/> for rules.
    /// </summary>
    internal static class ServerCosmeticsPacketSender
    {
        private static IServerAddonNetworkSender<PacketId>? _sender;

        internal static void Init(IServerAddonNetworkSender<PacketId> sender) => _sender = sender;

        internal static void SendUsernameRulesTo(ushort recipientId, bool customUsernameColorsOverrideTeamColors)
        {
            if (_sender == null) return;

            _sender.SendSingleData(
                PacketId.ServerUsernameColorRules,
                new ServerUsernameColorRulesPacket
                {
                    CustomUsernameColorsOverrideTeamColors = customUsernameColorsOverrideTeamColors,
                },
                recipientId);
        }

        /// <summary>
        /// Replay every known cloak for players other than <paramref name="recipientId"/> (like
        /// <c>SSMPEssentials.Server.PacketSender.SendAllPlayerHealth</c>).
        /// </summary>
        internal static void SendAllOtherPlayersCloaksTo(ushort recipientId, PlayerCosmeticsTracker tracker)
        {
            if (_sender == null) return;

            foreach (var kvp in tracker.Cloaks)
            {
                if (kvp.Key == recipientId) continue;

                _sender.SendCollectionData(
                    PacketId.CloakColorUpdate,
                    new CloakColorPacket
                    {
                        PlayerId = kvp.Key,
                        Color = kvp.Value.Color,
                        TextureSaturationCenti = kvp.Value.TextureSaturationCenti,
                    },
                    recipientId);
            }
        }

        internal static void SendAllOtherPlayersUsernameTintsTo(ushort recipientId, PlayerCosmeticsTracker tracker)
        {
            if (_sender == null) return;

            foreach (var kvp in tracker.UsernameTints)
            {
                if (kvp.Key == recipientId) continue;

                _sender.SendCollectionData(
                    PacketId.UsernameColorUpdate,
                    new UsernameColorPacket
                    {
                        PlayerId = kvp.Key,
                        Color = kvp.Value,
                        HasCustomUsernameTint = true,
                    },
                    recipientId);
            }
        }

        internal static void BroadcastCloakToOthers(
            ushort senderId,
            CloakNetAppearance appearance,
            IReadOnlyCollection<IServerPlayer> players)
        {
            if (_sender == null) return;

            foreach (var other in players)
            {
                if (other.Id == senderId) continue;

                _sender.SendCollectionData(
                    PacketId.CloakColorUpdate,
                    new CloakColorPacket
                    {
                        PlayerId = senderId,
                        Color = appearance.Color,
                        TextureSaturationCenti = appearance.TextureSaturationCenti,
                    },
                    other.Id);
            }
        }

        internal static void BroadcastUsernameTintToOthers(
            ushort senderId,
            CloakColor color,
            bool hasCustomUsernameTint,
            IReadOnlyCollection<IServerPlayer> players)
        {
            if (_sender == null) return;

            foreach (var other in players)
            {
                if (other.Id == senderId) continue;

                _sender.SendCollectionData(
                    PacketId.UsernameColorUpdate,
                    new UsernameColorPacket
                    {
                        PlayerId = senderId,
                        Color = color,
                        HasCustomUsernameTint = hasCustomUsernameTint,
                    },
                    other.Id);
            }
        }
    }
}
