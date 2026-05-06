using SSMP.Networking.Packet;

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
    /// Packet carrying a cloak color for a given player.
    ///
    /// Client -> Server: the player ID field is ignored; the server infers the sender.
    /// Server -> Client: the player ID is the owner of the color.
    /// </summary>
    internal class CloakColorPacket : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;

        public ushort PlayerId;
        public CloakColor Color;

        public void WriteData(IPacket packet)
        {
            packet.Write(PlayerId);
            packet.Write(Color.R);
            packet.Write(Color.G);
            packet.Write(Color.B);
        }

        public void ReadData(IPacket packet)
        {
            PlayerId = packet.ReadUShort();
            var r = packet.ReadByte();
            var g = packet.ReadByte();
            var b = packet.ReadByte();
            Color = new CloakColor(r, g, b);
        }
    }

    /// <summary>
    /// Same wire layout as <see cref="CloakColorPacket"/> — separate type for clarity.
    /// </summary>
    internal class UsernameColorPacket : IPacketData
    {
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;

        public ushort PlayerId;
        public CloakColor Color;

        public void WriteData(IPacket packet)
        {
            packet.Write(PlayerId);
            packet.Write(Color.R);
            packet.Write(Color.G);
            packet.Write(Color.B);
        }

        public void ReadData(IPacket packet)
        {
            PlayerId = packet.ReadUShort();
            var r = packet.ReadByte();
            var g = packet.ReadByte();
            var b = packet.ReadByte();
            Color = new CloakColor(r, g, b);
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

    internal static class PacketFactory
    {
        /// <summary>
        /// Shared instantiator used by both client and server receivers.
        /// </summary>
        public static IPacketData Instantiate(PacketId id)
        {
            return id switch
            {
                PacketId.CloakColorUpdate => new CloakColorPacket(),
                PacketId.UsernameColorUpdate => new UsernameColorPacket(),
                PacketId.ServerUsernameColorRules => new ServerUsernameColorRulesPacket(),
                _ => new CloakColorPacket(),
            };
        }
    }
}
