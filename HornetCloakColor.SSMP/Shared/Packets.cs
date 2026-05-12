using SSMP.Networking.Packet;
using SSMP.Networking.Packet.Data;

namespace HornetCloakColor.Shared
{
    /// <summary>
    /// Packet IDs used for all HornetCloakColor traffic. Namespaced separately from
    /// SSMP core packets via the SSMP addon network channel.
    /// </summary>
    internal enum PacketId
    {
        /// <summary>
        /// Client -> Server: the local player is announcing their chosen cloak color.
        /// Server -> Client (broadcast): a specific player's cloak color has changed.
        /// </summary>
        CloakColorUpdate = 0,

        /// <summary>
        /// Client -> Server: the local player is announcing their chosen username tint (RGB).
        /// Server -> Client (broadcast): a specific player's username tint has changed.
        /// </summary>
        UsernameColorUpdate = 1,

        /// <summary>
        /// Server -> Client (once per connection): whether custom username colors may override team colors.
        /// </summary>
        ServerUsernameColorRules = 2,
    }

    /// <summary>
    /// Packet carrying cloak color and texture saturation for a given player (see mod menu
    /// “texture saturation” / <c>_TargetSat</c> scaling in the cloak shader).
    ///
    /// Client -> Server: the player ID field is ignored; the server infers the sender.
    /// Server -> Client: the player ID is the owner of the appearance.
    /// </summary>
    internal class CloakColorPacket : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;

        public ushort PlayerId;
        public CloakColor Color;

        /// <summary>
        /// Texture saturation boost × 100 (0–200, default 100 = 1.0). Matches <see cref="CloakNetAppearance.TextureSaturationCenti"/>.
        /// </summary>
        public byte TextureSaturationCenti = 100;

        public void WriteData(IPacket packet)
        {
            packet.Write(PlayerId);
            packet.Write(Color.R);
            packet.Write(Color.G);
            packet.Write(Color.B);
            packet.Write(TextureSaturationCenti);
        }

        public void ReadData(IPacket packet)
        {
            PlayerId = packet.ReadUShort();
            var r = packet.ReadByte();
            var g = packet.ReadByte();
            var b = packet.ReadByte();
            Color = new CloakColor(r, g, b);
            TextureSaturationCenti = packet.ReadByte();
        }
    }

    /// <summary>
    /// Username tint sync. Client to server: <see cref="PlayerId"/> is ignored (sender is inferred).
    /// <see cref="HasCustomUsernameTint"/> false = stop syncing (Mod Menu Disabled); true = sync
    /// <see cref="Color"/> including literal white (255,255,255), which is distinct from Disabled
    /// because that RGB equals <see cref="CloakColor.Default"/>.
    /// </summary>
    internal class UsernameColorPacket : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;

        public ushort PlayerId;
        public CloakColor Color;

        /// <summary>False: remove this player from the username-tint relay. True: apply <see cref="Color"/>.</summary>
        public bool HasCustomUsernameTint;

        public void WriteData(IPacket packet)
        {
            packet.Write(PlayerId);
            packet.Write(Color.R);
            packet.Write(Color.G);
            packet.Write(Color.B);
            packet.Write(HasCustomUsernameTint);
        }

        public void ReadData(IPacket packet)
        {
            PlayerId = packet.ReadUShort();
            var r = packet.ReadByte();
            var g = packet.ReadByte();
            var b = packet.ReadByte();
            Color = new CloakColor(r, g, b);
            HasCustomUsernameTint = packet.ReadBool();
        }
    }

    internal class ServerUsernameColorRulesPacket : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;

        /// <summary>
        /// When true, HornetCloakColor username tints override SSMP team name colors.
        /// When false (default), team colors win whenever the player is on a team and teams are enabled.
        /// </summary>
        public bool CustomUsernameColorsOverrideTeamColors;

        public void WriteData(IPacket packet) =>
            packet.Write(CustomUsernameColorsOverrideTeamColors);

        public void ReadData(IPacket packet) =>
            CustomUsernameColorsOverrideTeamColors = packet.ReadBool();
    }

    /// <summary>
    /// Instantiator for <b>server</b> addon receivers. Clients send cosmetics with
    /// <see cref="SSMP.Api.Client.Networking.IClientAddonNetworkSender{T}.SendSingleData"/>, so inbound payloads are
    /// single <see cref="CloakColorPacket"/> / <see cref="UsernameColorPacket"/> (no collection length prefix).
    /// </summary>
    internal static class ServerAddonReceivePacketFactory
    {
        public static IPacketData Instantiate(PacketId id) =>
            id switch
            {
                PacketId.CloakColorUpdate => new CloakColorPacket(),
                PacketId.UsernameColorUpdate => new UsernameColorPacket(),
                PacketId.ServerUsernameColorRules => new ServerUsernameColorRulesPacket(),
                _ => new CloakColorPacket(),
            };
    }

    /// <summary>
    /// Instantiator for <b>client</b> addon receivers. The server mirrors
    /// <c>SSMPEssentials.Server.PacketSender.Broadcast</c> using
    /// <see cref="SSMP.Api.Server.Networking.IServerAddonNetworkSender{T}.SendCollectionData{T}"/>, which SSMP
    /// serializes as <see cref="PacketDataCollection{T}"/> (count + instances). Same pattern as
    /// <c>SSMPEssentials.Server.Packets.Packets.Instantiate</c> for <c>PlayerHealth</c> / <c>Color</c>.
    /// </summary>
    internal static class ClientAddonReceivePacketFactory
    {
        public static IPacketData Instantiate(PacketId id) =>
            id switch
            {
                PacketId.CloakColorUpdate => new PacketDataCollection<CloakColorPacket>(),
                PacketId.UsernameColorUpdate => new PacketDataCollection<UsernameColorPacket>(),
                PacketId.ServerUsernameColorRules => new ServerUsernameColorRulesPacket(),
                _ => new PacketDataCollection<CloakColorPacket>(),
            };
    }
}
