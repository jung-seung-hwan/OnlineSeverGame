using System;

namespace OnlineSeverGame.Shared.Protocol
{
    /// <summary>Malformed frame or packet. The receiver should drop the connection.</summary>
    public sealed class PacketFormatException : Exception
    {
        public PacketFormatException(string message) : base(message) { }
    }
}
