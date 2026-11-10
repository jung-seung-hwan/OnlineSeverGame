namespace OnlineSeverGame.Shared.Protocol
{
    public static class ProtocolConstants
    {
        /// <summary>Bumped whenever a packet layout changes. Hello/Welcome carry it.</summary>
        public const ushort Version = 12;

        public const int DefaultPort = 7777;

        /// <summary>Frame header: uint16 little-endian payload length.</summary>
        public const int FrameHeaderSize = 2;

        /// <summary>Largest payload (type byte + body) accepted by either side.</summary>
        public const int MaxPayloadSize = 8192;

        /// <summary>Player names are UTF-8, at most this many bytes.</summary>
        public const int MaxNameBytes = 32;

        public const int MaxPlayers = 8;

        /// <summary>Highest EmoteKind value.</summary>
        public const byte MaxEmote = 4;

        /// <summary>Highest CharacterKind value (v11).</summary>
        public const byte MaxCharacter = 3;

        /// <summary>True for 1..MaxCharacter.</summary>
        public static bool IsValidCharacter(byte kind) => kind >= 1 && kind <= MaxCharacter;
    }
}
