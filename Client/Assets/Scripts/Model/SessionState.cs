using System.Collections.Generic;
using OnlineSeverGame.Shared.Protocol;

namespace OnlineSeverGame.Client.Model
{
    public enum ClientScreen
    {
        Connect,     // 이름·서버 주소 입력
        Connecting,
        Lobby,       // 라운드 대기(인원 부족). 자유 이동 가능
        Countdown,   // 라운드 시작 직전
        Playing,
        Spectating,  // 내가 죽었거나 라운드 도중 들어와 관전
        Result,      // 승자 표시
    }

    public struct RosterEntry
    {
        public int Id;
        public string Name;
        public int ColorIndex;
        public CharacterKind Character; // 지금 그려지는 캐릭터(v11, 서버가 알려 준 값)
        public bool Alive;
        public bool InRound;
        public bool Connected;
        public int Place;             // 결과 순위(1 = 승자), 0 = 없음
        public float SurvivedSeconds; // 결과 화면용 생존 시간
        public bool LeftGame;         // 연결 끊김으로 탈락
        public bool Survived;         // 라운드 끝까지 살아남음(동시 사망 공동 승자는 false)
        public bool BySideKnife;      // 옆 위험물(맵마다 칼·눈덩이·나무 상자·마카롱)에 맞아 탈락
        public EliminationCause Cause; // 결과 화면에 보일 탈락 이유
    }

    // UI 가 읽는 세션 상태. 네트워크 세션과 오프라인 미리보기가 같은 형태로 채운다.
    public sealed class SessionState
    {
        public ClientScreen Screen = ClientScreen.Connect;
        public int LocalId = -1;
        public int Capacity = 8;
        public int RoundNumber;
        public int MapId = 1;         // 이번 라운드 맵 id. 화면 이름은 Loc.MapName(MapId)(언어를 바꾸면 바로 바뀌게)
        public string MapName = "";   // 이번 라운드 맵(로그용, 받은 때의 언어)
        public readonly List<RosterEntry> Roster = new List<RosterEntry>();
        public float RoundSeconds;
        public float CountdownSeconds;  // Countdown: 시작까지, Result: 다음 라운드까지
        public readonly List<int> WinnerIds = new List<int>();
        public string WinnerName = "";  // 공동 승자는 쉼표로
        public bool LocalInRound = true;
        public string Status = "";      // 연결 오류·끊김 등 사용자에게 보일 한 줄
        public int PingMs = -1;
        // 방 정보(RoomInfo): 방장 = 가장 먼저 들어와 아직 남은 사람. 다음 맵은 새 라운드(카운트다운)부터 쓰인다.
        public int HostId;
        public int NextMapId;
        public string NextMapName = "";
        public MapRotation Rotation = MapRotation.Cycle;
        public bool NextChosen;         // 방장이 직접 고른 다음 맵
        public bool HostStarts;         // (v13) 플레이어가 연 방: 첫 판은 방장이 '시작'을 눌러야 시작
        public BotLevel BotLevel = BotLevel.Normal; // (v14) 서버가 돌리는 봇의 난이도(방 하나에 하나)
        public int BotMask;             // (v14) 봇인 플레이어: 비트 (id - 1)

        public bool IsHost => HostId != 0 && HostId == LocalId;
        public bool IsBot(int id) => id >= 1 && id <= 8 && (BotMask & (1 << (id - 1))) != 0;

        public int BotCount
        {
            get
            {
                int n = 0;
                foreach (var r in Roster) if (IsBot(r.Id)) n++;
                return n;
            }
        }
        // 시험용(-touchshot): 위험물이 처음 내 몸에 닿아 보인 횟수(예측 시각 그리기 기준)
        public int HazardTouches;

        public int AliveCount
        {
            get
            {
                int n = 0;
                foreach (var r in Roster) if (r.Alive && r.InRound) n++;
                return n;
            }
        }

        public int ParticipantCount
        {
            get
            {
                int n = 0;
                foreach (var r in Roster) if (r.InRound) n++;
                return n;
            }
        }

        public bool TryGet(int id, out RosterEntry entry)
        {
            foreach (var r in Roster)
            {
                if (r.Id == id) { entry = r; return true; }
            }
            entry = default;
            return false;
        }
    }

    public interface IGameSession
    {
        SessionState State { get; }
        WorldRender World { get; }
        // 매 Update 호출. 입력 전송·수신 처리·보간을 여기서 한다.
        void Update(float deltaTime, Input.InputCollector input);
        // 방장만 의미가 있다: 다음 맵(0 = 그대로)·교체 방식(0 = 그대로). 미리보기는 무시한다.
        void RequestMap(byte stageId, MapRotation rotation);
        // 이모트(v7): 살아 있을 때만, 1초에 한 번. 서버가 모두에게(나 포함) 알린다. 미리보기는 바로 보인다.
        void SendEmote(EmoteKind emote);
        // 캐릭터 고르기(v11): 대기·카운트다운이면 바로, 라운드 진행·결과 중이면 다음 라운드부터 바뀐다(서버가 정함).
        // 미리보기는 바로 바뀐다.
        void RequestCharacter(CharacterKind kind);
        // (v13) 방장이 첫 판을 시작한다(HostStarts 방, 대기 중일 때만). 미리보기는 무시한다.
        void RequestStart();
        // (v14) 방장이 봇을 넣고 빼거나 난이도를 바꾼다(level 은 SetLevel 때만). 미리보기는 무시한다.
        void RequestBot(BotAction action, BotLevel level);
        void Close();
    }
}
