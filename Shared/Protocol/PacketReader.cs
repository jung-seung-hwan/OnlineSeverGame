using System.Buffers.Binary;
using System.Text;

namespace OnlineSeverGame.Shared.Protocol
{
    /// <summary>Little-endian reader over one payload. Throws PacketFormatException on underflow.</summary>
    public sealed class PacketReader
    {
        private readonly byte[] _data;
        private readonly int _end;
        private int _pos;

        public PacketReader(byte[] data) : this(data, 0, data.Length) { }

        public PacketReader(byte[] data, int offset, int count)
        {
            _data = data;
            _pos = offset;
            _end = offset + count;
        }

        public int Remaining => _end - _pos;

        public byte ReadByte()
        {
            Need(1);
            return _data[_pos++];
        }

        public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

        public bool ReadBool() => ReadByte() != 0;

        public ushort ReadUInt16()
        {
            Need(2);
            ushort v = BinaryPrimitives.ReadUInt16LittleEndian(new System.ReadOnlySpan<byte>(_data, _pos, 2));
            _pos += 2;
            return v;
        }

        public int ReadInt32()
        {
            Need(4);
            int v = BinaryPrimitives.ReadInt32LittleEndian(new System.ReadOnlySpan<byte>(_data, _pos, 4));
            _pos += 4;
            return v;
        }

        public uint ReadUInt32()
        {
            Need(4);
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(new System.ReadOnlySpan<byte>(_data, _pos, 4));
            _pos += 4;
            return v;
        }

        public string ReadString(int maxBytes = ProtocolConstants.MaxNameBytes)
        {
            int count = ReadByte();
            if (count > maxBytes) throw new PacketFormatException($"String of {count} bytes exceeds {maxBytes}");
            Need(count);
            string s = Encoding.UTF8.GetString(_data, _pos, count);
            _pos += count;
            return s;
        }

        /// <summary>Called after a packet is fully read: trailing bytes mean the layouts disagree.</summary>
        public void ExpectEnd()
        {
            if (_pos != _end) throw new PacketFormatException($"{_end - _pos} unexpected trailing bytes");
        }

        private void Need(int count)
        {
            if (_end - _pos < count) throw new PacketFormatException("Packet too short");
        }
    }
}
