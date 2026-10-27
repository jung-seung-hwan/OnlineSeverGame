using System.Buffers.Binary;

namespace OnlineSeverGame.Shared.Protocol
{
    /// <summary>Packet to/from bytes. A frame is [uint16 LE payload length][payload]; payload is [type][body].</summary>
    public static class PacketCodec
    {
        /// <summary>Builds a complete frame ready to write to the socket. Thread-safe.</summary>
        public static byte[] EncodeFrame(IPacket packet)
        {
            var w = new PacketWriter();
            w.WriteUInt16(0); // length placeholder
            w.WriteByte((byte)packet.Type);
            packet.Write(w);
            int payload = w.Length - ProtocolConstants.FrameHeaderSize;
            if (payload > ProtocolConstants.MaxPayloadSize)
                throw new PacketFormatException($"{packet.Type} payload {payload} exceeds {ProtocolConstants.MaxPayloadSize}");
            byte[] frame = w.ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)payload);
            return frame;
        }

        /// <summary>Parses one payload (as returned by FrameDecoder). Throws PacketFormatException if malformed.</summary>
        public static IPacket DecodePayload(byte[] payload) => DecodePayload(payload, 0, payload.Length);

        public static IPacket DecodePayload(byte[] payload, int offset, int count)
        {
            var r = new PacketReader(payload, offset, count);
            var type = (PacketType)r.ReadByte();
            IPacket packet;
            switch (type)
            {
                case PacketType.Hello: packet = HelloPacket.Read(r); break;
                case PacketType.Welcome: packet = WelcomePacket.Read(r); break;
                case PacketType.Reject: packet = RejectPacket.Read(r); break;
                case PacketType.PlayerJoined: packet = PlayerJoinedPacket.Read(r); break;
                case PacketType.PlayerLeft: packet = PlayerLeftPacket.Read(r); break;
                case PacketType.Input: packet = InputPacket.Read(r); break;
                case PacketType.Snapshot: packet = SnapshotPacket.Read(r); break;
                case PacketType.RoundStart: packet = RoundStartPacket.Read(r); break;
                case PacketType.RoundEnd: packet = RoundEndPacket.Read(r); break;
                case PacketType.Ping: packet = PingPacket.Read(r); break;
                case PacketType.Pong: packet = PongPacket.Read(r); break;
                case PacketType.RoomInfo: packet = RoomInfoPacket.Read(r); break;
                case PacketType.MapRequest: packet = MapRequestPacket.Read(r); break;
                default: throw new PacketFormatException($"Unknown packet type {(byte)type}");
            }
            r.ExpectEnd();
            return packet;
        }
    }
}
