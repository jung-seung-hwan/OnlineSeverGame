namespace OnlineSeverGame.Shared.Protocol
{
    /// <summary>First byte of every payload. C = client to server, S = server to client.</summary>
    public enum PacketType : byte
    {
        Hello = 1,        // C
        Welcome = 2,      // S
        Reject = 3,       // S
        PlayerJoined = 4, // S
        PlayerLeft = 5,   // S
        Input = 10,       // C
        Snapshot = 20,    // S
        RoundStart = 30,  // S
        RoundEnd = 31,    // S
        Ping = 40,        // C
        Pong = 41,        // S
    }

    public enum RejectReason : byte
    {
        RoomFull = 1,
        VersionMismatch = 2,
        BadHello = 3,
    }

    public enum LeaveReason : byte
    {
        Disconnected = 1,
        Timeout = 2,
        ProtocolError = 3,
    }

    public enum RoomState : byte
    {
        /// <summary>Not enough players. Everyone may run around freely, no hazards.</summary>
        Waiting = 0,
        /// <summary>Round participants are at spawn points; movement allowed, no hazards.</summary>
        Countdown = 1,
        /// <summary>Knives fall. Last survivor wins.</summary>
        Playing = 2,
        /// <summary>Results are shown; next round starts after the timer.</summary>
        Ended = 3,
    }

    public enum EliminationCause : byte
    {
        None = 0,
        Knife = 1,
        Disconnected = 2,
    }
}
