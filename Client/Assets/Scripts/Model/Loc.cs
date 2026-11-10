using System.Collections.Generic;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.Model
{
    public enum Language
    {
        Korean = 0,
        English = 1,
    }

    // 화면에 보이는 글을 한곳에 모은 표(docs/work/side-hazards-i18n.md 2장). 한국어와 영어.
    // 쓰는 법: Loc.T("key"), Loc.F("key", 인자...) (string.Format). 없는 키는 키 이름을 그대로 보여 준다(빠진 번역이 눈에 띄게).
    // 언어는 접속 화면·대기 화면의 언어 버튼으로 바꾸고 PlayerPrefs "lang"(ko / en)에 기억한다. 처음에는 OS 언어(한국어면 한국어,
    // 아니면 영어), 실행 인자 -lang ko|en 이 있으면 그것.
    // 서버가 보내는 맵 이름(Stage.Name "Kitchen" 등)은 화면에 쓰지 않는다: 맵 id 로 이 표의 이름을 고른다(MapName).
    public static class Loc
    {
        const string PrefKey = "lang";

        static Language _current;
        static bool _loaded;

        public static Language Current
        {
            get
            {
                if (!_loaded) Load();
                return _current;
            }
        }

        public static void Set(Language lang)
        {
            _current = lang;
            _loaded = true;
            PlayerPrefs.SetString(PrefKey, Code(lang));
            PlayerPrefs.Save();
        }

        // 실행 인자용: 저장하지 않고 이번 실행만.
        public static void Override(Language lang)
        {
            _current = lang;
            _loaded = true;
        }

        public static void Toggle() => Set(Current == Language.Korean ? Language.English : Language.Korean);

        public static bool TryParse(string code, out Language lang)
        {
            switch ((code ?? "").Trim().ToLowerInvariant())
            {
                case "ko": case "kr": case "korean": lang = Language.Korean; return true;
                case "en": case "english": lang = Language.English; return true;
                default: lang = Language.Korean; return false;
            }
        }

        static string Code(Language lang) => lang == Language.English ? "en" : "ko";

        static void Load()
        {
            _loaded = true;
            if (TryParse(PlayerPrefs.GetString(PrefKey, ""), out var saved)) _current = saved;
            else _current = Application.systemLanguage == SystemLanguage.Korean ? Language.Korean : Language.English;
        }

        public static string T(string key)
        {
            var table = Current == Language.English ? En : Ko;
            return table.TryGetValue(key, out string s) ? s : key;
        }

        public static string F(string key, params object[] args) => string.Format(T(key), args);

        // ---------------------------------------------------------------- 게임 용어

        public static string MapName(int stageId)
        {
            switch (stageId)
            {
                case 1: return T("map.1");
                case 2: return T("map.2");
                case 3: return T("map.3");
                case 4: return T("map.4");
                default: return F("map.n", stageId);
            }
        }

        public static string RotationName(MapRotation r) =>
            r == MapRotation.Random ? T("rot.random") : r == MapRotation.Fixed ? T("rot.fixed") : T("rot.cycle");

        // 위에서 떨어지는 위험물
        public static string HazardName(HazardKind kind)
        {
            switch (kind)
            {
                case HazardKind.Icicle: return T("hazard.icicle");
                case HazardKind.Crate: return T("hazard.crate");
                case HazardKind.Fork: return T("hazard.fork");
                default: return T("hazard.knife");
            }
        }

        // 옆에서 날아오는 위험물
        public static string SideHazardName(SideHazardKind kind)
        {
            switch (kind)
            {
                case SideHazardKind.Snowball: return T("side.snowball");
                case SideHazardKind.Crate: return T("side.crate");
                case SideHazardKind.Macaron: return T("side.macaron");
                default: return T("side.knife");
            }
        }

        // ---------------------------------------------------------------- 표

        static readonly Dictionary<string, string> Ko = new Dictionary<string, string>
        {
            // 접속
            { "connect.name", "이름" },
            { "connect.server", "서버 주소" },
            { "connect.join", "접속" },
            { "connect.preview", "오프라인 미리보기" },
            { "connect.connecting", "접속 중… {0}" },
            { "connect.cancel", "취소" },
            { "connect.cancelled", "접속을 취소했습니다." },
            { "lang.button", "English" },          // 누르면 바뀔 언어를 그 언어로 쓴다
            { "lang.label", "언어" },
            // 대기·카운트다운
            { "lobby.waiting", "다른 플레이어를 기다리는 중" },
            { "lobby.waitingSub", "접속 {0}/{1} · 인원이 모이면 시작합니다" },
            { "lobby.countdown", "{0}초 후 라운드 시작" },
            { "lobby.countdownSub", "라운드 {0} · {1} · 참가 {2}명" },
            { "lobby.countdownTip", "위험물이 나오기 전까지 자유롭게 움직이세요" },
            { "lobby.roster", "접속자 {0}/{1}" },
            { "roster.you", "(나)" },
            { "roster.host", "★방장" },
            // 진행 화면
            { "hud.alive", "생존 {0}/{1}" },
            { "hud.seconds", "{0:0.0}초" },
            { "hud.round", "라운드 {0}" },
            { "hud.ping", "핑 {0}ms" },
            { "hud.leave", "나가기" },
            { "hint.controls", "←/→ 이동   ↑·Space 점프   Shift 대시(맞히면 1초 기절)   ↓ 엎드리기   ↓+점프 발판 아래로   머리 밟기 = 기절   Z·X·C·V 이모트" },
            { "hint.side.1", "20초부터 양옆에서 칼이 날아옵니다 — 높이 오면 ↓ 엎드리기, 낮게 오면 점프" },
            { "hint.side.2", "20초부터 양옆에서 눈덩이가 날아옵니다 — 높이 오면 ↓ 엎드리기, 낮게 오면 점프" },
            { "hint.side.3", "20초부터 양옆에서 나무 상자가 날아옵니다 — 높이 오면 ↓ 엎드리기, 낮게 오면 점프" },
            { "hint.side.4", "20초부터 양옆에서 마카롱이 날아옵니다 — 높이 오면 ↓ 엎드리기, 낮게 오면 점프" },
            // 관전
            { "spec.out", "탈락! 관전 중" },
            { "spec.watching", "관전 중" },
            { "spec.alive", "남은 생존자 {0}명" },
            { "spec.nextRound", "다음 라운드부터 참가합니다" },
            { "spec.nextMap", "다음 맵: {0} ({1})" },
            // 결과
            { "result.noWinner", "승자 없음" },
            { "result.win", "승리!" },
            { "result.coWin", "공동 승리!" },
            { "result.winner", "승자: {0}" },
            { "result.coWinners", "공동 승자: {0}" },
            { "result.sub", "라운드 {0} · {1:0.0}초" },
            { "result.place", "{0}위" },
            { "result.left", "나감" },
            { "result.survived", "생존" },
            { "result.died", "{0:0.0}초 · {1}에 맞음" },
            { "result.next", "{0}초 후 다음 라운드" },
            // 맵 고르기
            { "map.next", "다음 라운드 맵: {0}" },
            { "map.chosen", " (방장 지정)" },
            { "map.rotation", "교체 방식: {0}" },
            { "map.host", "방장: {0}" },
            { "map.keys", "키 1~4: 다음 맵 · R: 교체 방식 (다음 라운드부터)" },
            { "map.1", "주방" },
            { "map.2", "겨울 처마" },
            { "map.3", "창고" },
            { "map.4", "디저트 가게" },
            { "map.n", "맵 {0}" },
            { "rot.cycle", "순환" },
            { "rot.random", "무작위" },
            { "rot.fixed", "고정" },
            // 위험물 이름(결과 화면의 탈락 이유, 안내 줄)
            { "hazard.knife", "식칼" },
            { "hazard.icicle", "고드름" },
            { "hazard.crate", "화물 상자" },
            { "hazard.fork", "포크" },
            { "side.knife", "날아온 칼" },
            { "side.snowball", "눈덩이" },
            { "side.crate", "나무 상자" },
            { "side.macaron", "마카롱" },
            // 미리보기
            { "preview.bot", "봇-{0}" },
            // 접속 오류
            { "net.badAddress", "서버 주소 형식이 잘못되었습니다 (예: 127.0.0.1:7777)" },
            { "net.noResponse", "서버 응답이 없습니다" },
            { "net.version", "게임 버전이 서버와 다릅니다 (서버 v{0}, 내 게임 v{1})" },
            { "net.unknownMap", "모르는 맵입니다 (번호 {0}). 게임을 새 버전으로 바꿔 주세요" },
            { "net.full", "방이 가득 찼습니다 (최대 8명)" },
            { "net.versionRejected", "게임 버전이 서버와 다릅니다" },
            { "net.badHello", "서버가 접속을 거부했습니다 (이름을 확인해 주세요)" },
            { "net.rejected", "접속이 거부되었습니다 (사유 {0})" },
            { "net.lost", "연결이 끊어졌습니다" },
            { "net.sendStuck", "서버가 응답하지 않아 연결을 끊었습니다" },
            { "net.timeout", "서버에 연결할 수 없습니다 ({0}, 시간 초과)" },
            { "net.closed", "서버가 연결을 끊었습니다" },
            { "net.badPacket", "서버에서 알 수 없는 데이터를 받았습니다 ({0})" },
            { "net.refused", "그 주소에 열린 서버가 없습니다 (연결 거부)" },
            { "net.reset", "서버와의 연결이 끊어졌습니다" },
            { "net.noHost", "서버 주소를 찾을 수 없습니다" },
            { "net.timedOut", "서버 응답 시간이 초과되었습니다" },
            { "net.error", "네트워크 오류: {0}" },
        };

        static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "connect.name", "Name" },
            { "connect.server", "Server" },
            { "connect.join", "Join" },
            { "connect.preview", "Offline preview" },
            { "connect.connecting", "Connecting… {0}" },
            { "connect.cancel", "Cancel" },
            { "connect.cancelled", "Connection cancelled." },
            { "lang.button", "한국어" },
            { "lang.label", "Language" },
            { "lobby.waiting", "Waiting for other players" },
            { "lobby.waitingSub", "{0}/{1} connected · starts when enough players join" },
            { "lobby.countdown", "Round starts in {0}" },
            { "lobby.countdownSub", "Round {0} · {1} · {2} players" },
            { "lobby.countdownTip", "Move freely until the hazards start" },
            { "lobby.roster", "Players {0}/{1}" },
            { "roster.you", "(you)" },
            { "roster.host", "★Host" },
            { "hud.alive", "Alive {0}/{1}" },
            { "hud.seconds", "{0:0.0}s" },
            { "hud.round", "Round {0}" },
            { "hud.ping", "Ping {0}ms" },
            { "hud.leave", "Leave" },
            { "hint.controls", "←/→ Move   ↑/Space Jump   Shift Dash (a hit stuns 1 s)   ↓ Crouch   ↓+Jump Drop through   Land on a head = stun   Z/X/C/V Emotes" },
            { "hint.side.1", "From 20 s, knives fly in from the sides — high: crouch (↓), low: jump" },
            { "hint.side.2", "From 20 s, snowballs fly in from the sides — high: crouch (↓), low: jump" },
            { "hint.side.3", "From 20 s, wooden crates fly in from the sides — high: crouch (↓), low: jump" },
            { "hint.side.4", "From 20 s, macarons fly in from the sides — high: crouch (↓), low: jump" },
            { "spec.out", "You're out! Spectating" },
            { "spec.watching", "Spectating" },
            { "spec.alive", "{0} still alive" },
            { "spec.nextRound", "You'll join from the next round" },
            { "spec.nextMap", "Next map: {0} ({1})" },
            { "result.noWinner", "No winner" },
            { "result.win", "You win!" },
            { "result.coWin", "Shared win!" },
            { "result.winner", "Winner: {0}" },
            { "result.coWinners", "Winners: {0}" },
            { "result.sub", "Round {0} · {1:0.0}s" },
            { "result.place", "#{0}" },
            { "result.left", "Left" },
            { "result.survived", "Survived" },
            { "result.died", "{0:0.0}s · {1}" },
            { "result.next", "Next round in {0}" },
            { "map.next", "Next map: {0}" },
            { "map.chosen", " (host's pick)" },
            { "map.rotation", "Rotation: {0}" },
            { "map.host", "Host: {0}" },
            { "map.keys", "Keys 1–4: next map · R: rotation (from the next round)" },
            { "map.1", "Kitchen" },
            { "map.2", "Winter Eaves" },
            { "map.3", "Warehouse" },
            { "map.4", "Dessert Shop" },
            { "map.n", "Map {0}" },
            { "rot.cycle", "Cycle" },
            { "rot.random", "Random" },
            { "rot.fixed", "Fixed" },
            { "hazard.knife", "knife" },
            { "hazard.icicle", "icicle" },
            { "hazard.crate", "falling crate" },
            { "hazard.fork", "fork" },
            { "side.knife", "flying knife" },
            { "side.snowball", "snowball" },
            { "side.crate", "wooden crate" },
            { "side.macaron", "macaron" },
            { "preview.bot", "Bot-{0}" },
            { "net.badAddress", "Invalid server address (e.g. 127.0.0.1:7777)" },
            { "net.noResponse", "No response from the server" },
            { "net.version", "Your game version differs from the server (server v{0}, yours v{1})" },
            { "net.unknownMap", "Unknown map (number {0}). Please update the game" },
            { "net.full", "The room is full (8 players max)" },
            { "net.versionRejected", "Your game version differs from the server" },
            { "net.badHello", "The server refused the connection (check your name)" },
            { "net.rejected", "Connection refused (reason {0})" },
            { "net.lost", "Disconnected" },
            { "net.sendStuck", "The server stopped responding" },
            { "net.timeout", "Can't reach the server ({0}, timed out)" },
            { "net.closed", "The server closed the connection" },
            { "net.badPacket", "Received data the game doesn't understand ({0})" },
            { "net.refused", "No server is running at that address (connection refused)" },
            { "net.reset", "The connection to the server was lost" },
            { "net.noHost", "Server address not found" },
            { "net.timedOut", "The server timed out" },
            { "net.error", "Network error: {0}" },
        };

        // 시험·검사용: 두 표의 키가 같은지(빠진 번역 찾기).
        public static IEnumerable<string> MissingKeys()
        {
            foreach (var k in Ko.Keys) if (!En.ContainsKey(k)) yield return "en:" + k;
            foreach (var k in En.Keys) if (!Ko.ContainsKey(k)) yield return "ko:" + k;
        }
    }
}
