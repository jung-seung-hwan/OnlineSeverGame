using System;
using System.Buffers.Binary;

namespace OnlineSeverGame.Shared.Protocol
{
    /// <summary>
    /// Reassembles frames from a TCP byte stream. TCP may split one frame across reads or deliver several
    /// in one read; push whatever arrived, then call TryReadFrame until it returns false.
    /// Not thread-safe: use one decoder per connection, from its receive thread.
    /// </summary>
    public sealed class FrameDecoder
    {
        private byte[] _buffer = new byte[4096];
        private int _start; // first unread byte
        private int _end;   // one past the last buffered byte

        public int BufferedBytes => _end - _start;

        public void Push(byte[] data, int offset, int count)
        {
            if (count <= 0) return;
            if (_end + count > _buffer.Length)
            {
                // Compact first, grow only if still short.
                int buffered = _end - _start;
                if (buffered + count > _buffer.Length)
                {
                    int size = _buffer.Length;
                    while (size < buffered + count) size *= 2;
                    var bigger = new byte[size];
                    Buffer.BlockCopy(_buffer, _start, bigger, 0, buffered);
                    _buffer = bigger;
                }
                else
                {
                    Buffer.BlockCopy(_buffer, _start, _buffer, 0, buffered);
                }
                _start = 0;
                _end = buffered;
            }
            Buffer.BlockCopy(data, offset, _buffer, _end, count);
            _end += count;
        }

        /// <summary>
        /// Returns the next complete payload ([type][body]) or false if more bytes are needed.
        /// Throws PacketFormatException on a zero or oversized length; the stream is then unusable.
        /// </summary>
        public bool TryReadFrame(out byte[] payload)
        {
            payload = null;
            int buffered = _end - _start;
            if (buffered < ProtocolConstants.FrameHeaderSize) return false;
            int length = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(_buffer, _start, 2));
            if (length == 0 || length > ProtocolConstants.MaxPayloadSize)
                throw new PacketFormatException($"Bad frame length {length}");
            if (buffered < ProtocolConstants.FrameHeaderSize + length) return false;
            payload = new byte[length];
            Buffer.BlockCopy(_buffer, _start + ProtocolConstants.FrameHeaderSize, payload, 0, length);
            _start += ProtocolConstants.FrameHeaderSize + length;
            if (_start == _end) _start = _end = 0;
            return true;
        }
    }
}
