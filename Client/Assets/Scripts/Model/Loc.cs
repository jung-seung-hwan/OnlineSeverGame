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

        // 시험(-uitest)에서는 false: 언어 버튼을 눌러도 사용자 저장값을 바꾸지 않는다.
        public static bool Persist = true;

        public static void Set(Language lang)
        {
            _current = lang;
            _loaded = true;
            if (!Persist) return;
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

        // 캐릭터 이름(젤리·젤리곰·푸딩, v11). 모르는 값은 젤리.
        public static string CharacterName(CharacterKind kind) =>
            ProtocolConstants.IsValidCharacter((byte)kind) ? T("char." + (int)kind) : T("char.1");

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

        // 라운드 중간 맵 이벤트 이름
        public static string MapEventName(MapEventKind kind)
        {
            switch (kind)
            {
                case MapEventKind.Fire: return T("event.fire");
                case MapEventKind.Wind: return T("event.wind");
                case MapEventKind.Conveyor: return T("event.conveyor");
                case MapEventKind.Syrup: return T("event.syrup");
                default: return "";
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
            { "connect.server", "주소" },
            { "connect.join", "참가" },
            { "connect.host", "방 만들기" },
            { "connect.joinTitle", "친구 방에 참가: 방장이 알려 준 주소를 넣으세요 (예: 123.45.67.89:7777)" },
            { "connect.opening", "방을 여는 중…" },
            { "connect.preview", "오프라인 미리보기" },
            { "connect.connecting", "접속 중… {0}" },
            { "connect.cancel", "취소" },
            { "connect.cancelled", "접속을 취소했습니다." },
            { "lang.button", "English" },          // 누르면 바뀔 언어를 그 언어로 쓴다
            { "lang.label", "언어" },
            // 대기·카운트다운
            { "lobby.waiting", "다른 플레이어를 기다리는 중" },
            { "lobby.waitingSub", "접속 {0}/{1} · 인원이 모이면 시작합니다" },
            { "lobby.hostReady", "준비되면 시작을 누르세요" },
            { "lobby.waitHost", "방장이 시작하기를 기다리는 중" },
            { "lobby.hostStartsSub", "접속 {0}/{1} · 방장이 시작을 누르면 라운드가 시작됩니다 (혼자서도 가능)" },
            { "lobby.start", "시작" },
            { "host.title", "친구가 접속할 주소" },
            { "host.trying", "인터넷: 공유기 포트를 여는 중…" },
            { "host.internet", "인터넷: {0}" },
            { "host.opened", "공유기 포트를 자동으로 열었습니다" },
            { "host.notOpened", "인터넷: 공유기 포트를 자동으로 열지 못했습니다" },
            { "host.doubleNat", "인터넷: 공유기가 두 단계라 밖에서 바로 들어올 수 없습니다" },
            { "host.forwardHint", "인터넷으로 받으려면 공유기 설정에서 TCP {0} 포트를 이 PC로 포트포워딩하세요. 같은 공유기에서는 아래 주소로 바로 됩니다." },
            { "host.lan", "같은 공유기: {0}" },
            { "host.copy", "주소 복사 ({0})" },
            { "host.copied", "복사했습니다" },
            { "host.portBusy", "포트 {0}을(를) 이미 다른 프로그램이 쓰고 있어 방을 만들 수 없습니다" },
            { "host.startFailed", "방을 만들 수 없습니다 ({0})" },
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
            { "hint.controls", "←/→ 이동   ↑·Space 점프   Shift 대시(맞히면 1.25초 기절)   ↓ 엎드리기   ↓+점프 발판 아래로   머리 밟기 = 기절   Z·X·C·V 이모트" },
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
            { "result.burned", "{0:0.0}초 · 불에 닿음" },
            // 맵 이벤트 (docs/work/map-layout.md 4)
            { "event.fire", "화구에 불!" },
            { "event.wind", "강풍" },
            { "event.conveyor", "컨베이어 벨트" },
            { "event.syrup", "시럽 바닥 (걷기 절반)" },
            { "event.warning", "곧: {0} {1}" },
            { "event.active", "{0} {1}  {2:0}초" },
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
            // UI 개편(docs/work/ui-revamp.md): 설정·HUD·카운트다운·단상·맵 고르기
            { "ui.settings", "설정" },
            { "ui.close", "닫기" },
            { "lang.ko", "한국어" },
            { "lang.en", "English" },
            { "settings.title", "설정" },
            { "settings.language", "언어" },
            { "settings.sound", "소리" },
            { "settings.soundEmpty", "음량 조절은 곧 추가됩니다" },
            { "settings.controls", "조작" },
            { "settings.esc", "Esc: 설정 열기·닫기" },
            { "ctl.move", "이동" },
            { "ctl.jump", "점프" },
            { "ctl.dash", "대시 (맞히면 1.25초 기절)" },
            { "ctl.crouch", "엎드리기" },
            { "ctl.drop", "발판 아래로 내려가기" },
            { "ctl.stomp", "머리 밟기 = 기절" },
            { "ctl.host", "방장: 다음 맵 · 교체 방식" },
            { "ctl.headKey", "위에서" },
            { "lobby.mapTitle", "다음 맵" },
            { "map.youPick", "내가 방장! 다음 맵을 고르세요" },
            { "map.hostPicks", "방장 {0} 님이 고릅니다" },
            { "map.keysShort", "키 1~4 · R" },
            { "countdown.round", "라운드 {0} · {1}" },
            { "countdown.go", "시작!" },
            { "hud.dash", "대시" },
            { "hud.dashReady", "준비" },
            { "hud.time", "{0:0.0}" },
            { "result.title.round", "라운드 {0} 결과" },
            { "result.survivedFor", "{0:0.0}초 버팀" },
            // 미리보기
            { "preview.bot", "봇-{0}" },
            { "char.1", "젤리" },
            { "char.2", "젤리곰" },
            { "char.3", "푸딩" },
            { "char.title", "캐릭터" },
            { "char.next", "다음 라운드부터 {0}" },
            { "char.colorNote", "색은 들어온 순서(플레이어 번호)로 정해져요" },
            // 접속 오류
            { "net.badAddress", "주소 형식이 잘못되었습니다 (예: 123.45.67.89:7777)" },
            { "net.noResponse", "방장 PC가 응답하지 않습니다" },
            { "net.version", "게임 버전이 방장과 다릅니다 (방장 v{0}, 내 게임 v{1}). 둘 다 최신 버전으로 업데이트하세요" },
            { "net.unknownMap", "모르는 맵입니다 (번호 {0}). 게임을 새 버전으로 바꿔 주세요" },
            { "net.full", "방이 가득 찼습니다 (최대 8명)" },
            { "net.versionRejected", "게임 버전이 방장과 다릅니다. 둘 다 최신 버전으로 업데이트하세요" },
            { "net.badHello", "방장이 접속을 거부했습니다 (이름을 확인해 주세요)" },
            { "net.rejected", "접속이 거부되었습니다 (사유 {0})" },
            { "net.lost", "연결이 끊어졌습니다" },
            { "net.sendStuck", "방장 PC가 응답하지 않아 연결을 끊었습니다" },
            { "net.timeout", "방장 PC에 연결할 수 없습니다 ({0}, 시간 초과). 주소와 방장의 포트포워딩을 확인하세요" },
            { "net.closed", "방이 닫혔습니다 (방장이 나갔을 수 있어요)" },
            { "net.badPacket", "알 수 없는 데이터를 받았습니다 ({0})" },
            { "net.refused", "그 주소에 열린 방이 없습니다 (연결 거부)" },
            { "net.reset", "방장과의 연결이 끊어졌습니다" },
            { "net.noHost", "서버 주소를 찾을 수 없습니다" },
            { "net.timedOut", "서버 응답 시간이 초과되었습니다" },
            { "net.error", "네트워크 오류: {0}" },
        };

        static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "connect.name", "Name" },
            { "connect.server", "Address" },
            { "connect.join", "Join" },
            { "connect.host", "Create room" },
            { "connect.joinTitle", "Join a friend's room: enter the address the host gave you (e.g. 123.45.67.89:7777)" },
            { "connect.opening", "Opening the room…" },
            { "connect.preview", "Offline preview" },
            { "connect.connecting", "Connecting… {0}" },
            { "connect.cancel", "Cancel" },
            { "connect.cancelled", "Connection cancelled." },
            { "lang.button", "한국어" },
            { "lang.label", "Language" },
            { "lobby.waiting", "Waiting for other players" },
            { "lobby.waitingSub", "{0}/{1} connected · starts when enough players join" },
            { "lobby.hostReady", "Press Start when you are ready" },
            { "lobby.waitHost", "Waiting for the host to start" },
            { "lobby.hostStartsSub", "{0}/{1} connected · the round starts when the host presses Start (works solo too)" },
            { "lobby.start", "Start" },
            { "host.title", "Address for your friends" },
            { "host.trying", "Internet: opening a port on your router…" },
            { "host.internet", "Internet: {0}" },
            { "host.opened", "The router port was opened automatically" },
            { "host.notOpened", "Internet: could not open the router port automatically" },
            { "host.doubleNat", "Internet: there are two routers in the way, so friends cannot reach you directly" },
            { "host.forwardHint", "To play over the internet, forward TCP port {0} to this PC in your router settings. On the same router, use the address below." },
            { "host.lan", "Same router: {0}" },
            { "host.copy", "Copy address ({0})" },
            { "host.copied", "Copied" },
            { "host.portBusy", "Port {0} is already used by another program, so the room cannot be created" },
            { "host.startFailed", "Cannot create the room ({0})" },
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
            { "hint.controls", "←/→ Move   ↑/Space Jump   Shift Dash (a hit stuns 1.25 s)   ↓ Crouch   ↓+Jump Drop through   Land on a head = stun   Z/X/C/V Emotes" },
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
            { "result.burned", "{0:0.0}s · burned" },
            { "event.fire", "Burners on fire!" },
            { "event.wind", "Strong wind" },
            { "event.conveyor", "Conveyor belt" },
            { "event.syrup", "Syrup floor (half speed)" },
            { "event.warning", "Coming: {0} {1}" },
            { "event.active", "{0} {1}  {2:0} s" },
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
            { "ui.settings", "Settings" },
            { "ui.close", "Close" },
            { "lang.ko", "한국어" },
            { "lang.en", "English" },
            { "settings.title", "Settings" },
            { "settings.language", "Language" },
            { "settings.sound", "Sound" },
            { "settings.soundEmpty", "Volume controls are coming soon" },
            { "settings.controls", "Controls" },
            { "settings.esc", "Esc: open / close settings" },
            { "ctl.move", "Move" },
            { "ctl.jump", "Jump" },
            { "ctl.dash", "Dash (a hit stuns 1.25 s)" },
            { "ctl.crouch", "Crouch" },
            { "ctl.drop", "Drop through a platform" },
            { "ctl.stomp", "Land on a head = stun" },
            { "ctl.host", "Host: next map · rotation" },
            { "ctl.headKey", "From above" },
            { "lobby.mapTitle", "Next map" },
            { "map.youPick", "You're the host! Pick the next map" },
            { "map.hostPicks", "{0} (host) picks the map" },
            { "map.keysShort", "Keys 1–4 · R" },
            { "countdown.round", "Round {0} · {1}" },
            { "countdown.go", "GO!" },
            { "hud.dash", "Dash" },
            { "hud.dashReady", "Ready" },
            { "hud.time", "{0:0.0}" },
            { "result.title.round", "Round {0} results" },
            { "result.survivedFor", "Lasted {0:0.0}s" },
            { "preview.bot", "Bot-{0}" },
            { "char.1", "Jelly" },
            { "char.2", "Jelly Bear" },
            { "char.3", "Pudding" },
            { "char.title", "Character" },
            { "char.next", "{0} from the next round" },
            { "char.colorNote", "Your color comes from your player number" },
            { "net.badAddress", "Invalid address (e.g. 123.45.67.89:7777)" },
            { "net.noResponse", "The host's PC is not responding" },
            { "net.version", "Your game version differs from the host's (host v{0}, yours v{1}). Both of you should update" },
            { "net.unknownMap", "Unknown map (number {0}). Please update the game" },
            { "net.full", "The room is full (8 players max)" },
            { "net.versionRejected", "Your game version differs from the host's. Both of you should update" },
            { "net.badHello", "The host refused the connection (check your name)" },
            { "net.rejected", "Connection refused (reason {0})" },
            { "net.lost", "Disconnected" },
            { "net.sendStuck", "The host's PC stopped responding" },
            { "net.timeout", "Can't reach the host's PC ({0}, timed out). Check the address and the host's port forwarding" },
            { "net.closed", "The room was closed (the host may have left)" },
            { "net.badPacket", "Received data the game doesn't understand ({0})" },
            { "net.refused", "No room is open at that address (connection refused)" },
            { "net.reset", "The connection to the host was lost" },
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
