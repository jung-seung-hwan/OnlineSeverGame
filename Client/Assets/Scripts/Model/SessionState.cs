using System.Collections.Generic;

namespace OnlineSeverGame.Client.Model
{
    public enum ClientScreen
    {
        Connect,     // 이름·서버 주소 입력
        Connecting,
        Lobby,       // 라운드 대기
        Countdown,   // 라운드 시작 직전
        Playing,
        Spectating,  // 내가 죽은 뒤 관전
        Result,      // 승자 표시
    }

    public struct RosterEntry
    {
        public int Id;
        public string Name;
        public int ColorIndex;
        public bool Alive;
        public bool Connected;
        public float SurvivedSeconds; // 사망·탈락 시점 기록(결과 화면용), 생존 중이면 0
    }

    // UI 가 읽는 세션 상태. 네트워크 세션과 오프라인 미리보기가 같은 형태로 채운다.
    public sealed class SessionState
    {
        public ClientScreen Screen = ClientScreen.Connect;
        public int LocalId = -1;
        public int Capacity = 8;
        public readonly List<RosterEntry> Roster = new List<RosterEntry>();
        public float RoundSeconds;
        public float CountdownSeconds;
        public int WinnerId = -1;
        public string WinnerName = "";
        public string Status = "";     // 연결 오류·끊김 등 사용자에게 보일 한 줄
        public int PingMs = -1;

        public int AliveCount
        {
            get
            {
                int n = 0;
                foreach (var r in Roster) if (r.Alive) n++;
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
        // 결과 화면에서 다음 라운드 대기로 돌아가기 등 UI 요청
        void RequestReady();
        void Close();
    }
}
