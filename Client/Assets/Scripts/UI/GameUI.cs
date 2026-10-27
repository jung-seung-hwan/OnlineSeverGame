using System.Collections.Generic;
using OnlineSeverGame.Client.Game;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Shared.Protocol;
using UnityEngine;

namespace OnlineSeverGame.Client.UI
{
    // IMGUI 로 그리는 임시 UI. 접속 → 대기 → 진행 → 관전 → 결과 화면.
    // 맵 선택(방장만): 대기·카운트다운 화면 왼쪽 명단 아래와 결과 화면 패널 안의 버튼, 또는 키보드 1~4(다음 맵),
    // R(교체 방식: 순환 → 무작위 → 고정). 키는 접속 중 어느 화면에서나 되고, 바꾼 맵은 다음 라운드부터 쓰인다.
    public sealed class GameUI : MonoBehaviour
    {
        public GameBootstrap Game;

        Font _font;
        GUIStyle _label, _title, _big, _center, _nameTag;
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

        void DrawConnect()
        {
            var r = Panel(460, 380);
            GUILayout.BeginArea(r);
            GUILayout.Space(8);
            GUILayout.Label("OnlineSeverGame", _title, GUILayout.Height(48));
            GUILayout.Space(12);
            GUILayout.Label("이름", _label);
            Game.PlayerName = GUILayout.TextField(Game.PlayerName ?? "", 16, GUILayout.Height(30));
            GUILayout.Label("서버 주소 (host:port)", _label);
            Game.ServerAddress = GUILayout.TextField(Game.ServerAddress ?? "", 64, GUILayout.Height(30));
            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            bool canConnect = !string.IsNullOrWhiteSpace(Game.PlayerName);
            GUI.enabled = canConnect;
            if (GUILayout.Button("접속", GUILayout.Height(40))) Game.Connect();
            GUI.enabled = true;
            if (GUILayout.Button("오프라인 미리보기", GUILayout.Height(40))) Game.StartPreview();
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
            var r = Panel(380, 160);
            GUILayout.BeginArea(r);
            GUILayout.Space(16);
            GUILayout.Label("접속 중… " + Game.ServerAddress, _center);
            GUILayout.Space(12);
            if (GUILayout.Button("취소", GUILayout.Height(36))) Game.Disconnect("접속을 취소했습니다.");
            GUILayout.EndArea();
        }

        // 대기·카운트다운 중에는 움직일 수 있으므로 가운데를 가리지 않는 위쪽 띠와 왼쪽 명단만 그린다.
        void DrawLobby(SessionState s)
        {
            var top = Filled(new Rect(UnityEngine.Screen.width / 2f - 230, 40, 460, 96));
            GUILayout.BeginArea(top);
            string head = s.Screen == ClientScreen.Countdown
                ? $"{Mathf.CeilToInt(Mathf.Max(0f, s.CountdownSeconds))}초 후 라운드 시작"
                : "다른 플레이어를 기다리는 중";
            GUILayout.Label(head, _big, GUILayout.Height(36));
            string sub = s.Screen == ClientScreen.Countdown
                ? $"라운드 {s.RoundNumber} · {s.MapName} · 참가 {s.ParticipantCount}명 · 떨어지기 전까지 자유롭게 움직이세요"
                : $"접속 {s.Roster.Count}/{s.Capacity} · 2명 이상이면 시작합니다";
            GUILayout.Label(sub, _center);
            GUILayout.EndArea();

            float sideH = 64 + 32 * s.Roster.Count;
            var side = Filled(new Rect(12, 48, 240, sideH));
            GUILayout.BeginArea(side);
            GUILayout.Label($"접속자 {s.Roster.Count}/{s.Capacity}", _label);
            foreach (var p in s.Roster) RosterRow(p, p.Id == s.LocalId, null, null, p.Id == s.HostId);
            GUILayout.EndArea();

            var map = Filled(new Rect(12, 48 + sideH + 8, 430, MapPanelHeight(s)));
            GUILayout.BeginArea(map);
            DrawMapChoice(Game.Session, s);
            GUILayout.EndArea();
            DrawTopBar(s, showRound: false);
        }

        void DrawHud(SessionState s)
        {
            GUI.Label(new Rect(16, 10, 400, 30), $"생존 {s.AliveCount}/{s.ParticipantCount}    {s.RoundSeconds:0.0}초", _label);
            DrawTopBar(s, showRound: true);
            if (s.Screen == ClientScreen.Playing) DrawHint();
        }

        void DrawTopBar(SessionState s, bool showRound)
        {
            string right = s.PingMs >= 0 ? $"핑 {s.PingMs}ms" : "";
            if (showRound && s.RoundNumber > 0) right = $"라운드 {s.RoundNumber}    " + right;
            var rs = new GUIStyle(_label) { alignment = TextAnchor.UpperRight };
            GUI.Label(new Rect(UnityEngine.Screen.width - 416, 10, 300, 30), right, rs);
            if (GUI.Button(new Rect(UnityEngine.Screen.width - 106, 8, 90, 30), "나가기")) Game.Disconnect("");
            if (!showRound) DrawHint();
        }

        void DrawHint()
        {
            var hint = new GUIStyle(_label) { fontSize = 14, alignment = TextAnchor.LowerCenter };
            hint.normal.textColor = new Color(1, 1, 1, 0.6f);
            GUI.Label(new Rect(0, UnityEngine.Screen.height - 30, UnityEngine.Screen.width, 24),
                "←/→ 이동   ↑·Space 점프   Shift 대시(2초마다, 맞히면 1초 기절)   ↓ 엎드리기   ↓+점프 발판 아래로   머리 밟기 = 기절   옆 칼: 머리 높이 ↓, 낮으면 점프", hint);
        }

        void DrawSpectating(SessionState s)
        {
            var r = Filled(new Rect(UnityEngine.Screen.width / 2f - 220, 48, 440, 100));
            GUILayout.BeginArea(r);
            GUILayout.Label(s.LocalInRound ? "탈락! 관전 중" : "관전 중", _big, GUILayout.Height(36));
            GUILayout.Label(s.LocalInRound ? $"남은 생존자 {s.AliveCount}명" : "다음 라운드부터 참가합니다", _center);
            GUILayout.EndArea();
            var next = new GUIStyle(_center) { fontSize = 14 };
            next.normal.textColor = new Color(1, 1, 1, 0.7f);
            GUI.Label(new Rect(r.x, r.yMax + 16, r.width, 22), $"다음 맵: {s.NextMapName} ({RotationName(s.Rotation)})", next);
        }

        void DrawResult(SessionState s)
        {
            _ranking.Clear();
            foreach (var p in s.Roster) if (p.Place > 0) _ranking.Add(p);
            _ranking.Sort((a, b) => a.Place != b.Place ? a.Place.CompareTo(b.Place) : b.SurvivedSeconds.CompareTo(a.SurvivedSeconds));

            var r = Panel(460, 210 + 30 * _ranking.Count + MapPanelHeight(s));
            GUILayout.BeginArea(r);
            GUILayout.Space(8);
            string winner;
            if (s.WinnerIds.Count == 0) winner = "승자 없음";
            else if (s.WinnerIds.Contains(s.LocalId)) winner = s.WinnerIds.Count > 1 ? "공동 승리!" : "승리!";
            else winner = (s.WinnerIds.Count > 1 ? "공동 승자: " : "승자: ") + s.WinnerName;
            GUILayout.Label(winner, _title, GUILayout.Height(48));
            GUILayout.Label($"라운드 {s.RoundNumber} · {s.RoundSeconds:0.0}초", _center);
            GUILayout.Space(6);
            foreach (var p in _ranking)
            {
                string tail = p.LeftGame ? "끊김" : (p.Survived ? "생존" : $"{p.SurvivedSeconds:0.0}초" + (p.BySideKnife ? " (옆 칼)" : ""));
                RosterRow(p, p.Id == s.LocalId, $"{p.Place}위  ", tail);
            }
            GUILayout.FlexibleSpace();
            DrawMapChoice(Game.Session, s);
            GUILayout.Label($"{Mathf.CeilToInt(Mathf.Max(0f, s.CountdownSeconds))}초 후 다음 라운드", _center);
            if (GUILayout.Button("나가기", GUILayout.Height(34))) Game.Disconnect("");
            GUILayout.EndArea();
        }

        static readonly string[] MapNames = { "주방", "겨울 처마", "창고", "디저트 가게" };
        static readonly MapRotation[] Rotations = { MapRotation.Cycle, MapRotation.Random, MapRotation.Fixed };

        static string RotationName(MapRotation r) =>
            r == MapRotation.Random ? "무작위" : r == MapRotation.Fixed ? "고정" : "순환";

        static float MapPanelHeight(SessionState s) => s.IsHost ? 150 : 76;

        // 다음 라운드 맵과 교체 방식. 방장에게는 버튼(맵 4개, 방식 3개)을 보여준다. 누르면 서버에 요청만 하고,
        // 화면 값은 서버가 돌려주는 RoomInfo 로 바뀐다.
        void DrawMapChoice(IGameSession session, SessionState s)
        {
            string host = s.TryGet(s.HostId, out RosterEntry h) ? h.Name + (s.IsHost ? " (나)" : "") : "-";
            GUILayout.Label($"다음 라운드 맵: {s.NextMapName}{(s.NextChosen ? " (방장 지정)" : "")}", _label);
            GUILayout.Label($"교체 방식: {RotationName(s.Rotation)}    방장: {host}", _label);
            if (!s.IsHost) return;
            var button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            GUILayout.BeginHorizontal();
            for (int i = 0; i < MapNames.Length; i++)
            {
                bool current = s.NextMapId == i + 1;
                GUI.color = current ? new Color(1f, 0.85f, 0.4f) : Color.white;
                if (GUILayout.Button($"{i + 1} {MapNames[i]}", button, GUILayout.Height(28))) session.RequestMap((byte)(i + 1), 0);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var r in Rotations)
            {
                GUI.color = s.Rotation == r ? new Color(1f, 0.85f, 0.4f) : Color.white;
                if (GUILayout.Button(RotationName(r), button, GUILayout.Height(28))) session.RequestMap(0, r);
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
            var hint = new GUIStyle(_label) { fontSize = 13 };
            hint.normal.textColor = new Color(1, 1, 1, 0.6f);
            GUILayout.Label("키 1~4 다음 맵, R 교체 방식 · 다음 라운드부터", hint);
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
            string name = (prefix ?? "") + p.Name + (isLocal ? "  (나)" : "") + (host ? "  ★방장" : "");
            GUILayout.Label(name, _label);
            GUILayout.FlexibleSpace();
            if (tail != null) GUILayout.Label(tail, _label);
            GUILayout.EndHorizontal();
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
