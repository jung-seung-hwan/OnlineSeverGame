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
        RoomInfo = 32,    // S
        Emote = 33,       // S (v7)
        Ping = 40,        // C
        Pong = 41,        // S
        MapRequest = 50,  // C
        EmoteRequest = 51, // C (v7)
        PlayerCharacter = 34, // S (v11)
        CharacterRequest = 52, // C (v11)
        StartRequest = 53, // C (v13)
    }

    /// <summary>
    /// Character kinds (v11): which body a player is drawn with. Purely visual; hazards hit the same HitShapes for
    /// every kind (they are measured on the narrowest of the three). 0 on the wire = not given (Hello) and is never
    /// broadcast. docs/work/char-select.md.
    /// </summary>
    public enum CharacterKind : byte
    {
        Jelly = 1,   // 젤리
        Bear = 2,    // 젤리곰
        Pudding = 3, // 푸딩
    }

    /// <summary>
    /// Emotes (v7): a short body animation everyone sees. Purely visual; the server only checks and relays them.
    /// 0 on the wire is never sent.
    /// </summary>
    public enum EmoteKind : byte
    {
        Wave = 1,   // 인사
        Taunt = 2,  // 놀리기
        Cheer = 3,  // 기뻐하기
        Sad = 4,    // 슬퍼하기
    }

    /// <summary>How the map changes between rounds (RoomInfo, MapRequest). 0 on the wire = none / keep.</summary>
    public enum MapRotation : byte
    {
        /// <summary>Kitchen, winter eaves, warehouse, dessert shop, kitchen, ... from wherever it is now.</summary>
        Cycle = 1,
        /// <summary>A random map, never the one just played.</summary>
        Random = 2,
        /// <summary>The same map every round.</summary>
        Fixed = 3,
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
        /// <summary>Hit by a side knife flying across the map.</summary>
        SideKnife = 3,
        /// <summary>Caught by the map event (v7: the kitchen burners' fire). docs/work/map-layout.md 4.1.</summary>
        MapEvent = 4,
    }
}
