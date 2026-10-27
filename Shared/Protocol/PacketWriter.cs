using System;
using System.Buffers.Binary;
using System.Text;

namespace OnlineSeverGame.Shared.Protocol
{
    /// <summary>Little-endian writer into a growable buffer.</summary>
    public sealed class PacketWriter
    {
        private byte[] _buffer;
        private int _length;

        public PacketWriter(int capacity = 256)
        {
            _buffer = new byte[Math.Max(16, capacity)];
        }

        public int Length => _length;

        public void Clear() => _length = 0;

        public void WriteByte(byte value)
        {
            Ensure(1);
            _buffer[_length++] = value;
        }

        public void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteUInt16(ushort value)
        {
            Ensure(2);
            BinaryPrimitives.WriteUInt16LittleEndian(new Span<byte>(_buffer, _length, 2), value);
            _length += 2;
        }

        public void WriteInt32(int value)
        {
            Ensure(4);
            BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(_buffer, _length, 4), value);
            _length += 4;
        }

        public void WriteUInt32(uint value)
        {
            Ensure(4);
            BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(_buffer, _length, 4), value);
            _length += 4;
        }

        /// <summary>uint8 byte length + UTF-8 bytes. Truncated (on a char boundary) to maxBytes.</summary>
        public void WriteString(string value, int maxBytes = ProtocolConstants.MaxNameBytes)
        {
            if (maxBytes > 255) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            value ??= string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            int count = bytes.Length;
            if (count > maxBytes)
            {
                count = maxBytes;
                // Do not cut a multi-byte character in half: back up to a lead byte.
                while (count > 0 && (bytes[count] & 0xC0) == 0x80) count--;
            }
            WriteByte((byte)count);
            Ensure(count);
            Buffer.BlockCopy(bytes, 0, _buffer, _length, count);
            _length += count;
        }

        public byte[] ToArray()
        {
            var result = new byte[_length];
            Buffer.BlockCopy(_buffer, 0, result, 0, _length);
            return result;
        }

        private void Ensure(int extra)
        {
            int needed = _length + extra;
            if (needed <= _buffer.Length) return;
            int size = _buffer.Length * 2;
            while (size < needed) size *= 2;
            Array.Resize(ref _buffer, size);
        }
    }
}
