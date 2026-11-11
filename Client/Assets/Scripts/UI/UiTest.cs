using System;
using System.Collections;
using System.IO;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Shared.Protocol;
using UnityEngine;
using UnityEngine.UIElements;

namespace OnlineSeverGame.Client.UI
{
    // 버튼 클릭 자동 시험(-uitest). 사람 손 대신 버튼 한가운데를 "누르고 떼는" 포인터 이벤트를 패널에 넣고,
    // 그 결과(화면 전환, 언어, 서버가 돌려준 다음 맵·교체 방식)를 확인한다. docs/work/ui-revamp.md 7장.
    //   - 누르기 전에 그 점에서 패널이 고르는 요소(panel.Pick)가 그 버튼인지 본다: 다른 요소에 가려졌거나 화면 밖이면 실패.
    //   - 이벤트는 실제 마우스와 같은 길(PointerDown → PointerUp, Clickable)로 간다. 버튼 콜백을 직접 부르지 않는다.
    // 실행: 클라이언트가 '방 만들기'로 이 PC에 방을 연다(-port 로 포트를 고른다). 봇은 그 포트로 넣고, 시험이 '시작'을 누른다.
    //   Client.exe -uitest -port 7803 -name UiTester [-uitestshots <폴더>]   (봇: BotClient --port 7803)
    // 결과: 로그에 "[UITest] PASS/FAIL ..." 줄, 마지막에 "[UITest] DONE pass=N fail=M", 종료 코드 0(모두 통과)/1.
    // 시험 중에는 이름·서버 주소·언어를 저장하지 않는다(사용자 저장값 보호).
    public sealed class UiTest : MonoBehaviour
    {
        public static bool Active { get; private set; }

        public GameUI Ui;
        int _pass, _fail;
        string _shots;
        int _shotNo;

        void Awake()
        {
            Active = true;
            Loc.Persist = false;
            CharacterChoice.Persist = false;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-uitestshots") _shots = args[i + 1];
        }

        IEnumerator Start()
        {
            Debug.Log("[UITest] start");
            yield return Frames(30);
            var game = Ui.Game;

            // ---------------- 접속 화면
            Check(Ui.Shown == ClientScreen.Connect, "starts on connect screen");
            var startLang = Loc.Current;
            yield return Click("btn-settings-connect");
            Check(Ui.SettingsOpen, "connect: settings button opens settings");
            yield return Frames(20);
            ExpectCovered("btn-join", "settings: buttons under the settings window can't be clicked");
            yield return Click("btn-lang-en");
            Check(Loc.Current == Language.English, "settings: English button switches language");
            yield return Frames(2);
            Check(ButtonText("btn-join") == "Join", "settings: join button text follows language (\"" + ButtonText("btn-join") + "\")");
            yield return Shot("settings-en");
            yield return Click("btn-lang-ko");
            Check(Loc.Current == Language.Korean, "settings: 한국어 button switches language");
            yield return Click("btn-settings-close");
            Check(!Ui.SettingsOpen, "settings: close button closes settings");
            yield return Click("btn-lang-connect");
            Check(Loc.Current == Language.English, "connect: language chip toggles to English");
            yield return Click("btn-lang-connect");
            Check(Loc.Current == Language.Korean, "connect: language chip toggles back to Korean");
            Loc.Set(startLang); // 나머지 화면은 시작 언어(-lang)로 찍는다
            // 캐릭터 고르기(docs/work/char-select.md): 접속 전에는 고른 값만 바뀌고 Hello 에 실린다
            yield return Click("connect-char-pudding");
            Check(CharacterChoice.Current == CharacterKind.Pudding, "connect: pudding tile picks pudding");
            yield return Click("connect-char-bear");
            Check(CharacterChoice.Current == CharacterKind.Bear, "connect: bear tile picks bear");
            yield return Shot("connect");

            // ---------------- 방 만들기 → 대기(이 PC가 방장, docs/work/player-host.md)
            yield return Click("btn-host");
            yield return Until(() => Ui.Shown == ClientScreen.Lobby || Ui.Shown == ClientScreen.Countdown, 10f, "join: lobby screen appears");
            yield return Until(() => game.Session != null && game.Session.State.IsHost, 5f, "lobby: first player becomes host");
            yield return Until(() => MyCharacter(game) == CharacterKind.Bear, 3f, "lobby: joined as the character picked before joining (Hello)");
            yield return Click("lobby-char-pudding");
            yield return Until(() => MyCharacter(game) == CharacterKind.Pudding, 3f, "lobby: pudding tile changes my character at once (server PlayerCharacter)");
            yield return Shot("lobby-pudding");
            yield return Click("lobby-char-bear");
            yield return Until(() => MyCharacter(game) == CharacterKind.Bear, 3f, "lobby: bear tile changes it back");
            yield return Frames(10);
            int[] maps = { 3, 2, 4, 1 };
            foreach (int id in maps)
            {
                yield return Click("lobby-map-" + id);
                yield return Until(() => game.Session != null && game.Session.State.NextMapId == id && game.Session.State.NextChosen, 3f,
                    "lobby: map button " + id + " sets next map (server RoomInfo)");
            }
            yield return Shot("lobby-host");
            yield return Click("lobby-rot-random");
            yield return Until(() => game.Session != null && game.Session.State.Rotation == MapRotation.Random, 3f, "lobby: rotation Random button");
            yield return Click("lobby-rot-fixed");
            yield return Until(() => game.Session != null && game.Session.State.Rotation == MapRotation.Fixed, 3f, "lobby: rotation Fixed button");
            yield return Click("lobby-rot-cycle");
            yield return Until(() => game.Session != null && game.Session.State.Rotation == MapRotation.Cycle, 3f, "lobby: rotation Cycle button");
            // 봇(v14, docs/work/bots.md): 방장이 넣고 빼고 난이도를 고른다
            int botsBefore = game.Session.State.BotCount;
            yield return Click("lobby-bot-add");
            yield return Until(() => game.Session != null && game.Session.State.BotCount == botsBefore + 1, 3f, "lobby: + bot adds a server bot");
            yield return Click("lobby-bot-add");
            yield return Until(() => game.Session != null && game.Session.State.BotCount == botsBefore + 2, 3f, "lobby: + bot again adds another");
            yield return Click("lobby-bot-level-hard");
            yield return Until(() => game.Session != null && game.Session.State.BotLevel == BotLevel.Hard, 3f, "lobby: Hard sets the bot level (server RoomInfo)");
            yield return Shot("lobby-bots");
            yield return Click("lobby-bot-remove");
            yield return Until(() => game.Session != null && game.Session.State.BotCount == botsBefore + 1, 3f, "lobby: - bot removes one");
            yield return Click("lobby-bot-level-normal");
            yield return Until(() => game.Session != null && game.Session.State.BotLevel == BotLevel.Normal, 3f, "lobby: Normal sets the bot level back");
            yield return Click("btn-settings-lobby");
            Check(Ui.SettingsOpen, "lobby: settings button opens settings");
            yield return Shot("lobby-settings");
            yield return Click("btn-settings-close");
            Check(!Ui.SettingsOpen, "lobby: settings closes");

            // ---------------- 라운드(방장이 '시작'을 누르면 시작. 봇은 -port 로 연 이 방에 붙는다)
            Check(game.Session != null && game.Session.State.HostStarts, "lobby: player-hosted room waits for the host");
            Check(Ui.Shown == ClientScreen.Lobby, "lobby: no countdown before the host presses Start");
            yield return Shot("lobby-host-address");
            Debug.Log("[UITest] pressing Start");
            yield return Click("lobby-start");
            yield return Until(() => Ui.Shown == ClientScreen.Countdown, 10f, "round: countdown starts when the host presses Start");
            yield return Shot("countdown");
            yield return Until(() => Ui.Shown == ClientScreen.Playing || Ui.Shown == ClientScreen.Spectating, 10f, "round: playing screen");
            if (Ui.Shown == ClientScreen.Playing)
            {
                yield return Click("btn-settings-hud");
                Check(Ui.SettingsOpen, "hud: settings button opens settings");
                yield return Click("btn-settings-close");
                Check(!Ui.SettingsOpen, "hud: settings closes");
            }
            yield return Until(() => Ui.Shown == ClientScreen.Result, 150f, "round: result screen");
            yield return Frames(20);
            yield return Shot("result");
            if (game.Session != null && game.Session.State.IsHost)
            {
                yield return Click("result-map-2");
                yield return Until(() => game.Session != null && game.Session.State.NextMapId == 2, 3f, "result: map button 2 sets next map");
                yield return Click("result-rot-random");
                yield return Until(() => game.Session != null && game.Session.State.Rotation == MapRotation.Random, 3f, "result: rotation Random button");
            }
            else Fail("result: still host on result screen");
            // 결과 화면에서 고르면 다음 라운드부터: 지금 캐릭터는 그대로
            // (지금 캐릭터와 다른 칸을 누른다: 창이 보이므로 사람이 그사이 칸을 눌렀을 수도 있다)
            var drawn = MyCharacter(game);
            var other = drawn == CharacterKind.Jelly ? CharacterKind.Pudding : CharacterKind.Jelly;
            yield return Click("result-char-" + (other == CharacterKind.Jelly ? "jelly" : "pudding"));
            Check(CharacterChoice.Current == other, "result: " + other + " tile picks it");
            yield return Frames(20);
            Check(MyCharacter(game) == drawn, "result: the drawn character (" + drawn + ") stays until the next round");
            yield return Shot("result-next-round");
            yield return Click("btn-leave-result");
            yield return Until(() => Ui.Shown == ClientScreen.Connect, 5f, "result: leave button returns to connect screen");
            yield return Frames(20);
            yield return Shot("back-to-connect");

            // ---------------- 혼자 하기(이 PC 안의 서버 + 봇 7명, 난이도 선택)
            var savedLevel = game.OfflineLevel;
            yield return Click("btn-level-easy");
            Check(game.OfflineLevel == BotLevel.Easy, "connect: Easy picks the solo level");
            yield return Click("btn-solo");
            yield return Until(() => Ui.Shown == ClientScreen.Lobby, 10f, "solo: lobby appears");
            yield return Until(() => game.Session != null && game.Session.State.BotCount == 7 && game.Session.State.IsHost, 5f, "solo: 7 bots, I am the host");
            Check(game.IsOffline && game.Session.State.BotLevel == BotLevel.Easy, "solo: offline room with Easy bots");
            yield return Frames(20);
            yield return Shot("solo-lobby");
            yield return Click("lobby-start");
            yield return Until(() => Ui.Shown == ClientScreen.Countdown, 10f, "solo: Start begins the countdown");
            yield return Click("btn-leave-lobby");
            yield return Until(() => Ui.Shown == ClientScreen.Connect && !game.IsHosting, 5f, "solo: leave closes the solo room");
            game.OfflineLevel = savedLevel;

            Loc.Override(startLang);
            Debug.Log($"[UITest] DONE pass={_pass} fail={_fail}");
            yield return Frames(5);
            Application.Quit(_fail == 0 ? 0 : 1);
        }

        // ---------------------------------------------------------------- 도구

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < end)
            {
                if (cond()) { Pass(what); yield break; }
                yield return null;
            }
            Fail(what + " (timeout " + timeout + " s)");
        }

        void Check(bool ok, string what)
        {
            if (ok) Pass(what);
            else Fail(what);
        }

        void Pass(string what) { _pass++; Debug.Log("[UITest] PASS " + what); }
        void Fail(string what) { _fail++; Debug.Log("[UITest] FAIL " + what); }

        static CharacterKind MyCharacter(OnlineSeverGame.Client.Game.GameBootstrap game) =>
            game.Session != null && game.Session.State.TryGet(game.Session.State.LocalId, out RosterEntry me) ? me.Character : 0;

        static string ButtonText(string name) => UiKit.TryGetButton(name, out var b) ? UiKit.ButtonText(b) : null;

        // 시험이 '가려짐'을 제대로 잡는지 확인: 그 버튼 한가운데의 맨 위 요소가 버튼이 아니어야 통과.
        void ExpectCovered(string name, string what)
        {
            if (!UiKit.TryGetButton(name, out var b) || b.panel == null) { Fail(what + ": button not found"); return; }
            var picked = b.panel.Pick(b.worldBound.center);
            Check(picked != b && (picked == null || !b.Contains(picked)), what + " (top element: " + (picked == null ? "none" : picked.name) + ")");
        }

        // 버튼 한가운데를 누르고 뗀다. 그 점의 맨 위 요소가 버튼(또는 그 안)이 아니면 실패로 적고 누르지 않는다.
        IEnumerator Click(string name)
        {
            yield return Frames(3); // 직전 변화가 배치(layout)에 반영되게
            if (!UiKit.TryGetButton(name, out var b) || b.panel == null)
            {
                Fail("click " + name + ": button not found");
                yield break;
            }
            var r = b.worldBound;
            if (b.resolvedStyle.display == DisplayStyle.None || r.width < 1f || r.height < 1f || float.IsNaN(r.x))
            {
                Fail("click " + name + ": not visible");
                yield break;
            }
            var center = r.center;
            var picked = b.panel.Pick(center);
            if (picked != b && (picked == null || !b.Contains(picked)))
            {
                Fail("click " + name + ": covered by " + (picked == null ? "nothing" : picked.name + "/" + picked.GetType().Name));
                yield break;
            }
            if (!b.enabledInHierarchy)
            {
                Fail("click " + name + ": disabled");
                yield break;
            }
            var root = b.panel.visualTree;
            var down = new Event { type = EventType.MouseDown, mousePosition = center, button = 0, clickCount = 1 };
            using (var e = PointerDownEvent.GetPooled(down)) root.SendEvent(e);
            yield return null;
            var up = new Event { type = EventType.MouseUp, mousePosition = center, button = 0, clickCount = 1 };
            using (var e = PointerUpEvent.GetPooled(up)) root.SendEvent(e);
            Debug.Log($"[UITest] click {name} at panel ({center.x:0},{center.y:0})");
            yield return Frames(3);
        }

        IEnumerator Shot(string label)
        {
            if (string.IsNullOrEmpty(_shots)) yield break;
            yield return Frames(15); // 등장 움직임이 끝나게
            Directory.CreateDirectory(_shots);
            string path = Path.Combine(_shots, (++_shotNo).ToString("00") + "-" + label + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[UITest] shot " + path);
            yield return Frames(2);
        }
    }
}
