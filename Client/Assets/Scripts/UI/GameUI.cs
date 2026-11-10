using System.Collections.Generic;
using OnlineSeverGame.Client.Game;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.UI
{
    // IMGUI 로 그리는 임시 UI. 접속 → 대기 → 진행 → 관전 → 결과 화면.
    // 타이틀 로고(TitleLogo): 접속·접속 중 화면 위에 움직이는 큰 로고, 대기·카운트다운 화면 오른쪽 위에 멈춘 작은 로고.
    // 맵 선택(방장만): 대기·카운트다운 화면 왼쪽 명단 아래와 결과 화면 패널 안의 버튼, 또는 키보드 1~4(다음 맵),
    // R(교체 방식: 순환 → 무작위 → 고정). 키는 접속 중 어느 화면에서나 되고, 바꾼 맵은 다음 라운드부터 쓰인다.
    // 글은 모두 Loc 표에서 가져온다(한국어/영어). 언어 버튼: 접속 화면 패널 오른쪽 위, 접속 뒤에는 위쪽 나가기 버튼 왼쪽.
    // 정해진 폭보다 긴 한 줄 글은 FitStyle 로 글자를 줄여 잘리지 않게 한다(docs/work/side-hazards-i18n.md 1장).
    public sealed class GameUI : MonoBehaviour
    {
        public GameBootstrap Game;

        Font _font;
        GUIStyle _label, _title, _big, _center, _nameTag, _tail;
        bool _stylesReady;
        readonly List<RosterEntry> _ranking = new List<RosterEntry>();

        void EnsureStyles()
        {
            if (_stylesReady) return;
            // 기본 폰트에 한글이 없을 수 있어 OS 폰트를 쓴다.
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 18);
            GUI.skin.font = _font;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            _label.normal.textColor = Color.white;
            _title = new GUIStyle(_label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _big = new GUIStyle(_label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _center = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter };
            _nameTag = new GUIStyle(_label) { fontSize = 14, alignment = TextAnchor.LowerCenter };
            _tail = new GUIStyle(_label) { fontSize = 16 };
            GUI.skin.textField.fontSize = 18;
            GUI.skin.button.fontSize = 18;
            _stylesReady = true;
        }

        void OnGUI()
        {
            EnsureStyles();
            GUI.skin.font = _font;
            var session = Game.Session;
            var state = session?.State;
            var screen = state?.Screen ?? ClientScreen.Connect;

            if (screen != ClientScreen.Connect && screen != ClientScreen.Connecting)
            {
                DrawNameTags(session);
                HandleMapKeys(session);
            }

            switch (screen)
            {
                case ClientScreen.Connect: DrawConnect(); break;
                case ClientScreen.Connecting: DrawConnecting(); break;
                case ClientScreen.Lobby:
                case ClientScreen.Countdown: DrawLobby(state); break;
                case ClientScreen.Playing: DrawHud(state); break;
                case ClientScreen.Spectating: DrawHud(state); DrawSpectating(state); break;
                case ClientScreen.Result: DrawHud(state); DrawResult(state); break;
            }
        }

        const float ConnectPanelHeight = 246f; // 언어 줄 + 접속 실패 문구 두 줄까지 들어간다

        // 움직이는 타이틀 로고를 위에 그리고, 그 아래에 놓을 폭 w · 높이 h 패널의 내용 영역을 돌려준다.
        // 로고 크기(픽셀/로고 단위)와 위치는 접속 패널 높이로만 정해서, 접속 중 화면으로 넘어가도 로고가 움직이지 않는다.
        Rect TitleAndPanel(float w, float h)
        {
            const float gap = 16f;
            float sw = UnityEngine.Screen.width, sh = UnityEngine.Screen.height;
            var unit = TitleLogo.Size(1f, animated: true);
            float ppu = Mathf.Clamp(Mathf.Min(sw * 0.55f / unit.x, (sh - ConnectPanelHeight - gap - 24f) / unit.y), 2.5f, 9f);
            var logo = TitleLogo.Size(ppu, animated: true);
            float total = logo.y + gap + ConnectPanelHeight;
            float top = Mathf.Max(8f, (sh - total) / 2f);
            TitleLogo.Draw(new Vector2((sw - logo.x) / 2f, top), ppu, animated: true);
            return Filled(new Rect((sw - w) / 2f, top + logo.y + gap, w, h));
        }

        void DrawConnect()
        {
            var r = TitleAndPanel(460, ConnectPanelHeight);
            GUILayout.BeginArea(r);
            // 이름표를 입력칸 왼쪽에 두어 패널을 낮게 한다(로고 자리를 넓히려고).
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("connect.name"), _label, GUILayout.Width(110), GUILayout.Height(30));
            Game.PlayerName = GUILayout.TextField(Game.PlayerName ?? "", 16, GUILayout.Height(30));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("connect.server"), _label, GUILayout.Width(110), GUILayout.Height(30));
            Game.ServerAddress = GUILayout.TextField(Game.ServerAddress ?? "", 64, GUILayout.Height(30));
            GUILayout.EndHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            bool canConnect = !string.IsNullOrWhiteSpace(Game.PlayerName);
            GUI.enabled = canConnect;
            if (GUILayout.Button(Loc.T("connect.join"), GUILayout.Height(40))) Game.Connect();
            GUI.enabled = true;
            if (GUILayout.Button(Loc.T("connect.preview"), GUILayout.Height(40))) Game.StartPreview();
            GUILayout.EndHorizontal();
            // 언어 줄: 오른쪽 끝에 "언어 [English]" (바꿀 언어 이름이 버튼에 보인다)
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(Loc.T("lang.label"), new GUIStyle(_label) { alignment = TextAnchor.MiddleRight, padding = new RectOffset(0, 6, 0, 0) }, GUILayout.Height(30));
            if (GUILayout.Button(Loc.T("lang.button"), GUILayout.Width(110), GUILayout.Height(30))) Loc.Toggle();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(Game.LastStatus))
            {
                GUILayout.Space(6);
                var warn = new GUIStyle(_label) { wordWrap = true };
                warn.normal.textColor = new Color(1f, 0.6f, 0.5f);
                GUILayout.Label(Game.LastStatus, warn);
            }
            GUILayout.EndArea();

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && canConnect)
                Game.Connect();
        }

        void DrawConnecting()
        {
            var r = TitleAndPanel(380, 120);
            GUILayout.BeginArea(r);
            GUILayout.Space(16);
            string connecting = Loc.F("connect.connecting", Game.ServerAddress);
            GUILayout.Label(connecting, FitStyle(_center, connecting, r.width));
            GUILayout.Space(12);
            if (GUILayout.Button(Loc.T("connect.cancel"), GUILayout.Height(36))) Game.Disconnect(Loc.T("connect.cancelled"));
            GUILayout.EndArea();
        }

        // 대기·카운트다운 중에는 움직일 수 있으므로 가운데를 가리지 않는 위쪽 띠와 왼쪽 명단만 그린다.
        void DrawLobby(SessionState s)
        {
            // 가운데 띠: 큰 글씨 한 줄 + 작은 글씨 한두 줄. 예전에는 작은 글씨 한 줄에 다 넣어 오른쪽이 잘렸다.
            bool counting = s.Screen == ClientScreen.Countdown;
            const float topW = 520;
            var top = Filled(new Rect(UnityEngine.Screen.width / 2f - topW / 2f, 40, topW, counting ? 136 : 96));
            GUILayout.BeginArea(top);
            string head = counting
                ? Loc.F("lobby.countdown", Mathf.CeilToInt(Mathf.Max(0f, s.CountdownSeconds)))
                : Loc.T("lobby.waiting");
            GUILayout.Label(head, FitStyle(_big, head, top.width), GUILayout.Height(36));
            string sub = counting
                ? Loc.F("lobby.countdownSub", s.RoundNumber, Loc.MapName(s.MapId), s.ParticipantCount)
                : Loc.F("lobby.waitingSub", s.Roster.Count, s.Capacity);
            GUILayout.Label(sub, FitStyle(_center, sub, top.width));
            if (counting)
            {
                string tip = Loc.T("lobby.countdownTip");
                GUILayout.Label(tip, FitStyle(_center, tip, top.width));
            }
            GUILayout.EndArea();

            float sideH = 64 + 32 * s.Roster.Count;
            var side = Filled(new Rect(12, 48, RosterWidth, sideH));
            GUILayout.BeginArea(side);
            GUILayout.Label(Loc.F("lobby.roster", s.Roster.Count, s.Capacity), _label);
            foreach (var p in s.Roster) RosterRow(p, p.Id == s.LocalId, null, null, p.Id == s.HostId);
            GUILayout.EndArea();

            var map = Filled(new Rect(12, 48 + sideH + 8, MapPanelWidth, MapPanelHeight(s)));
            GUILayout.BeginArea(map);
            DrawMapChoice(Game.Session, s);
            GUILayout.EndArea();
            DrawTopBar(s, showRound: false);

            // 대기 화면 머리: 나가기 버튼 아래 오른쪽에 멈춘 작은 로고(움직이는 예고는 게임 예고와 헷갈려 뺀다).
            const float smallPpu = 2.2f;
            var size = TitleLogo.Size(smallPpu, animated: false);
            TitleLogo.Draw(new Vector2(UnityEngine.Screen.width - size.x - 16, 48), smallPpu, animated: false);
        }

        void DrawHud(SessionState s)
        {
            GUI.Label(new Rect(16, 10, 400, 30), Loc.F("hud.alive", s.AliveCount, s.ParticipantCount) + "    " + Loc.F("hud.seconds", s.RoundSeconds), _label);
            DrawTopBar(s, showRound: true);
            if (s.Screen == ClientScreen.Playing) DrawHint();
            DrawMapEvent();
        }

        // 맵 이벤트 알림(docs/work/map-layout.md 4.5): 예고 2초는 깜빡이며 "곧: 강풍 →", 켜져 있는 동안 "강풍 →  7초".
        // 이벤트는 라운드 틱만으로 정해지므로 맵이 그리는 시각(Layout.DrawRoundTick)의 상태를 그대로 읽는다.
        void DrawMapEvent()
        {
            var layout = Game.Layout;
            if (layout == null || layout.Source == null || layout.Source.EventSpec == null) return;
            var e = layout.Source.AtRoundTick((uint)System.Math.Max(0.0, layout.DrawRoundTick)).Event;
            if (e.Phase == MapEventPhase.None) return;
            if (e.IsWarning && Mathf.Repeat(Time.time * 4f, 1f) < 0.3f) return;
            string arrow = e.Kind == MapEventKind.Wind || e.Kind == MapEventKind.Conveyor ? (e.Dir > 0 ? "→" : "←") : "";
            string text = e.IsWarning
                ? Loc.F("event.warning", Loc.MapEventName(e.Kind), arrow)
                : Loc.F("event.active", Loc.MapEventName(e.Kind), arrow, e.TicksLeft / (float)GameConstants.TickRate);
            var style = new GUIStyle(_big) { fontSize = 24 };
            style.normal.textColor = e.Kind == MapEventKind.Fire ? new Color(1f, 0.55f, 0.2f) : new Color(1f, 0.95f, 0.7f);
            float w = 520f;
            GUI.Label(new Rect((UnityEngine.Screen.width - w) / 2f, 44, w, 34), text, FitStyle(style, text, w));
        }

        void DrawTopBar(SessionState s, bool showRound)
        {
            string right = s.PingMs >= 0 ? Loc.F("hud.ping", s.PingMs) : "";
            if (showRound && s.RoundNumber > 0) right = Loc.F("hud.round", s.RoundNumber) + "    " + right;
            var rs = new GUIStyle(_label) { alignment = TextAnchor.UpperRight };
            GUI.Label(new Rect(UnityEngine.Screen.width - 526, 10, 300, 30), right, rs);
            LanguageButton(new Rect(UnityEngine.Screen.width - 216, 8, 100, 30));
            if (GUI.Button(new Rect(UnityEngine.Screen.width - 106, 8, 90, 30), Loc.T("hud.leave"))) Game.Disconnect("");
            if (!showRound) DrawHint();
        }

        void DrawHint()
        {
            // 두 줄: 조작, 이 맵의 옆 위험물. 예전 한 줄(약 1100 px)은 그보다 좁은 창에서 양끝이 잘렸다.
            var hint = new GUIStyle(_label) { fontSize = 14, alignment = TextAnchor.LowerCenter };
            hint.normal.textColor = new Color(1, 1, 1, 0.6f);
            float w = UnityEngine.Screen.width;
            int mapId = Game.Session != null ? Game.Session.State.MapId : 1;
            string controls = Loc.T("hint.controls");
            string side = Loc.T("hint.side." + Mathf.Clamp(mapId, 1, 4));
            GUI.Label(new Rect(0, UnityEngine.Screen.height - 50, w, 22), controls, FitStyle(hint, controls, w - 16));
            GUI.Label(new Rect(0, UnityEngine.Screen.height - 28, w, 22), side, FitStyle(hint, side, w - 16));
        }

        void DrawSpectating(SessionState s)
        {
            var r = Filled(new Rect(UnityEngine.Screen.width / 2f - 220, 48, 440, 100));
            GUILayout.BeginArea(r);
            GUILayout.Label(s.LocalInRound ? Loc.T("spec.out") : Loc.T("spec.watching"), _big, GUILayout.Height(36));
            GUILayout.Label(s.LocalInRound ? Loc.F("spec.alive", s.AliveCount) : Loc.T("spec.nextRound"), _center);
            GUILayout.EndArea();
            var next = new GUIStyle(_center) { fontSize = 14 };
            next.normal.textColor = new Color(1, 1, 1, 0.7f);
            GUI.Label(new Rect(r.x, r.yMax + 16, r.width, 22), Loc.F("spec.nextMap", Loc.MapName(s.NextMapId), Loc.RotationName(s.Rotation)), next);
        }

        void DrawResult(SessionState s)
        {
            _ranking.Clear();
            foreach (var p in s.Roster) if (p.Place > 0) _ranking.Add(p);
            _ranking.Sort((a, b) => a.Place != b.Place ? a.Place.CompareTo(b.Place) : b.SurvivedSeconds.CompareTo(a.SurvivedSeconds));

            var r = Panel(ResultWidth, 210 + 30 * _ranking.Count + MapPanelHeight(s));
            GUILayout.BeginArea(r);
            GUILayout.Space(8);
            string winner;
            if (s.WinnerIds.Count == 0) winner = Loc.T("result.noWinner");
            else if (s.WinnerIds.Contains(s.LocalId)) winner = s.WinnerIds.Count > 1 ? Loc.T("result.coWin") : Loc.T("result.win");
            else winner = Loc.F(s.WinnerIds.Count > 1 ? "result.coWinners" : "result.winner", s.WinnerName);
            GUILayout.Label(winner, FitStyle(_title, winner, r.width), GUILayout.Height(48));
            GUILayout.Label(Loc.F("result.sub", s.RoundNumber, s.RoundSeconds), _center);
            GUILayout.Space(6);
            var stage = Stage.ById((byte)s.MapId) ?? Stage.Default;
            foreach (var p in _ranking)
            {
                // 예전: "12.3초 (옆 칼)". 지금: 무엇에 맞았는지 그 맵의 위험물 이름으로("12.3초 · 눈덩이에 맞음").
                string tail;
                if (p.LeftGame) tail = Loc.T("result.left");
                else if (p.Survived) tail = Loc.T("result.survived");
                else if (p.Cause == EliminationCause.SideKnife) tail = Loc.F("result.died", p.SurvivedSeconds, Loc.SideHazardName(stage.SideHazard));
                else if (p.Cause == EliminationCause.Knife) tail = Loc.F("result.died", p.SurvivedSeconds, Loc.HazardName(stage.Hazard));
                else if (p.Cause == EliminationCause.MapEvent) tail = Loc.F("result.burned", p.SurvivedSeconds);
                else tail = Loc.F("hud.seconds", p.SurvivedSeconds);
                RosterRow(p, p.Id == s.LocalId, Loc.F("result.place", p.Place) + "  ", tail);
            }
            GUILayout.FlexibleSpace();
            DrawMapChoice(Game.Session, s);
            GUILayout.Label(Loc.F("result.next", Mathf.CeilToInt(Mathf.Max(0f, s.CountdownSeconds))), _center);
            if (GUILayout.Button(Loc.T("hud.leave"), GUILayout.Height(34))) Game.Disconnect("");
            GUILayout.EndArea();
        }

        static readonly MapRotation[] Rotations = { MapRotation.Cycle, MapRotation.Random, MapRotation.Fixed };

        // 패널 폭. 예전(명단 240, 맵 430, 결과 460)은 "이름 (나) ★방장"·"4 디저트 가게" 버튼·결과 줄 끝이 잘릴 수 있었다.
        const float RosterWidth = 300;
        const float MapPanelWidth = 470;
        const float ResultWidth = 540;

        static float MapPanelHeight(SessionState s) => s.IsHost ? 218 : 92;

        // 다음 라운드 맵과 교체 방식. 방장에게는 버튼(맵 4개, 방식 3개)을 보여준다. 누르면 서버에 요청만 하고,
        // 화면 값은 서버가 돌려주는 RoomInfo 로 바뀐다.
        void DrawMapChoice(IGameSession session, SessionState s)
        {
            string host = s.TryGet(s.HostId, out RosterEntry h) ? h.Name + (s.IsHost ? " " + Loc.T("roster.you") : "") : "-";
            string next = Loc.F("map.next", Loc.MapName(s.NextMapId)) + (s.NextChosen ? Loc.T("map.chosen") : "");
            GUILayout.Label(next, FitStyle(_label, next, MapPanelWidth - 28));
            string line = Loc.F("map.rotation", Loc.RotationName(s.Rotation)) + "    " + Loc.F("map.host", host);
            GUILayout.Label(line, FitStyle(_label, line, MapPanelWidth - 28));
            if (!s.IsHost) return;
            var button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            // 맵 버튼은 2 × 2: 한 줄에 넷이면 "4 디저트 가게"·"2 Winter Eaves"가 버튼 폭을 넘는다.
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 2; col++)
                {
                    int id = row * 2 + col + 1;
                    GUI.color = s.NextMapId == id ? new Color(1f, 0.85f, 0.4f) : Color.white;
                    if (GUILayout.Button(id + "  " + Loc.MapName(id), button, GUILayout.Height(28))) session.RequestMap((byte)id, 0);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            foreach (var r in Rotations)
            {
                GUI.color = s.Rotation == r ? new Color(1f, 0.85f, 0.4f) : Color.white;
                if (GUILayout.Button(Loc.RotationName(r), button, GUILayout.Height(28))) session.RequestMap(0, r);
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
            var hint = new GUIStyle(_label) { fontSize = 13 };
            hint.normal.textColor = new Color(1, 1, 1, 0.6f);
            GUILayout.Label(Loc.T("map.keys"), FitStyle(hint, Loc.T("map.keys"), MapPanelWidth - 28));
        }

        void HandleMapKeys(IGameSession session)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || session == null || !session.State.IsHost) return;
            var s = session.State;
            if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha4)
                session.RequestMap((byte)(e.keyCode - KeyCode.Alpha1 + 1), 0);
            else if (e.keyCode >= KeyCode.Keypad1 && e.keyCode <= KeyCode.Keypad4)
                session.RequestMap((byte)(e.keyCode - KeyCode.Keypad1 + 1), 0);
            else if (e.keyCode == KeyCode.R)
            {
                int i = System.Array.IndexOf(Rotations, s.Rotation);
                session.RequestMap(0, Rotations[(i + 1) % Rotations.Length]);
            }
            else return;
            e.Use();
        }

        void RosterRow(RosterEntry p, bool isLocal, string prefix, string tail = null, bool host = false)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(26));
            var swatch = GUILayoutUtility.GetRect(20, 20, GUILayout.Width(20), GUILayout.Height(20));
            var old = GUI.color;
            GUI.color = PlayerColors.Get(p.ColorIndex);
            GUI.DrawTexture(swatch, Texture2D.whiteTexture);
            GUI.color = old;
            GUILayout.Space(8);
            string name = (prefix ?? "") + p.Name + (isLocal ? " " + Loc.T("roster.you") : "") + (host ? " " + Loc.T("roster.host") : "");
            GUILayout.Label(name, _label);
            GUILayout.FlexibleSpace();
            if (tail != null) GUILayout.Label(tail, _tail);
            GUILayout.EndHorizontal();
        }

        // 언어 버튼: 바꿀 언어의 이름("English" / "한국어")을 보여 주고 누르면 바꾼다(PlayerPrefs 에 기억).
        static void LanguageButton(Rect r)
        {
            var style = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            if (GUI.Button(r, Loc.T("lang.button"), style)) Loc.Toggle();
        }

        // 한 줄 글이 width 를 넘으면 들어갈 때까지 글자를 줄인 스타일(최소 11). 넘지 않으면 그대로.
        static GUIStyle FitStyle(GUIStyle style, string text, float width)
        {
            var content = new GUIContent(text);
            if (style.CalcSize(content).x <= width) return style;
            var fit = new GUIStyle(style);
            int size = style.fontSize > 0 ? style.fontSize : 18;
            while (size > 11)
            {
                size--;
                fit.fontSize = size;
                if (fit.CalcSize(content).x <= width) break;
            }
            return fit;
        }

        void DrawNameTags(IGameSession session)
        {
            var cam = Game.WorldCamera;
            if (cam == null) return;
            float headY = Game.Layout.PlayerSize.y + 0.25f;
            foreach (var p in session.World.Players)
            {
                var sp = cam.WorldToScreenPoint(new Vector3(p.Position.x, p.Position.y + headY, 0));
                var rect = new Rect(sp.x - 80, UnityEngine.Screen.height - sp.y - 24, 160, 22);
                var c = PlayerColors.Get(p.ColorIndex);
                _nameTag.normal.textColor = p.Alive ? Color.Lerp(c, Color.white, 0.5f) : new Color(1, 1, 1, 0.3f);
                GUI.Label(rect, p.IsLocal ? "▼ " + p.Name : p.Name, _nameTag);
                if (p.IsLocal && p.Alive) DrawDashGauge(sp, p.DashCooldown);
            }
        }

        // 내 캐릭터 이름표 아래 대시 게이지: 차오르는 동안 회색, 다 차면(대시 가능) 밝은 하늘색.
        static void DrawDashGauge(Vector3 tagScreenPos, float cooldown)
        {
            const float w = 56f, h = 7f;
            var back = new Rect(tagScreenPos.x - w / 2f, UnityEngine.Screen.height - tagScreenPos.y + 1f, w, h);
            float fill = 1f - Mathf.Clamp01(cooldown);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(back, Texture2D.whiteTexture);
            GUI.color = fill >= 1f ? new Color(0.45f, 0.95f, 1f, 0.95f) : new Color(0.75f, 0.75f, 0.78f, 0.85f);
            GUI.DrawTexture(new Rect(back.x + 1f, back.y + 1f, (w - 2f) * fill, h - 2f), Texture2D.whiteTexture);
            GUI.color = old;
        }

        static readonly Color PanelColor = new Color(0.06f, 0.06f, 0.08f, 0.92f);

        // 가운데 불투명 패널. 반환값은 패딩을 뺀 내용 영역이다.
        static Rect Panel(float w, float h) =>
            Filled(new Rect((UnityEngine.Screen.width - w) / 2f, (UnityEngine.Screen.height - h) / 2f, w, h));

        static Rect Filled(Rect r)
        {
            var old = GUI.color;
            GUI.color = PanelColor;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
            const float pad = 14f;
            return new Rect(r.x + pad, r.y + pad, r.width - pad * 2, r.height - pad * 2);
        }
    }
}
