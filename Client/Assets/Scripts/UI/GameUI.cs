using System.Collections.Generic;
using OnlineSeverGame.Client.Game;
using OnlineSeverGame.Client.Model;
using UnityEngine;

namespace OnlineSeverGame.Client.UI
{
    // IMGUI 로 그리는 임시 UI. 접속 → 대기 → 진행 → 관전 → 결과 화면.
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
                DrawNameTags(session);

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

        void DrawLobby(SessionState s)
        {
            var r = Panel(420, 190 + 30 * s.Capacity);
            GUILayout.BeginArea(r);
            GUILayout.Space(8);
            string head = s.Screen == ClientScreen.Countdown || s.CountdownSeconds > 0f
                ? $"곧 시작합니다  {Mathf.CeilToInt(Mathf.Max(0f, s.CountdownSeconds))}"
                : "라운드 대기 중";
            GUILayout.Label(head, _big, GUILayout.Height(40));
            GUILayout.Label($"참가자 {s.Roster.Count}/{s.Capacity}", _center);
            GUILayout.Space(6);
            foreach (var p in s.Roster) RosterRow(p, p.Id == s.LocalId, null);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("나가기", GUILayout.Height(32))) Game.Disconnect("");
            GUILayout.EndArea();
        }

        void DrawHud(SessionState s)
        {
            GUI.Label(new Rect(16, 10, 400, 30), $"생존 {s.AliveCount}/{s.Roster.Count}    {s.RoundSeconds:0.0}초", _label);
            string right = s.PingMs >= 0 ? $"핑 {s.PingMs}ms" : "";
            var rs = new GUIStyle(_label) { alignment = TextAnchor.UpperRight };
            GUI.Label(new Rect(UnityEngine.Screen.width - 316, 10, 200, 30), right, rs);
            if (GUI.Button(new Rect(UnityEngine.Screen.width - 106, 8, 90, 30), "나가기")) Game.Disconnect("");
            if (s.Screen == ClientScreen.Playing)
            {
                var hint = new GUIStyle(_label) { fontSize = 14, alignment = TextAnchor.LowerCenter };
                hint.normal.textColor = new Color(1, 1, 1, 0.6f);
                GUI.Label(new Rect(0, UnityEngine.Screen.height - 30, UnityEngine.Screen.width, 24),
                    "←/→ 이동   ↑·Space 점프   Shift 대시(부딪히면 밀치기)", hint);
            }
        }

        void DrawSpectating(SessionState s)
        {
            var r = Filled(new Rect(UnityEngine.Screen.width / 2f - 220, 48, 440, 100));
            GUILayout.BeginArea(r);
            GUILayout.Label("탈락! 관전 중", _big, GUILayout.Height(36));
            GUILayout.Label($"남은 생존자 {s.AliveCount}명", _center);
            GUILayout.EndArea();
        }

        void DrawResult(SessionState s)
        {
            _ranking.Clear();
            _ranking.AddRange(s.Roster);
            // 승자 → 생존 시간이 긴 순
            _ranking.Sort((a, b) =>
            {
                if (a.Id == s.WinnerId) return -1;
                if (b.Id == s.WinnerId) return 1;
                return b.SurvivedSeconds.CompareTo(a.SurvivedSeconds);
            });

            var r = Panel(440, 220 + 30 * _ranking.Count);
            GUILayout.BeginArea(r);
            GUILayout.Space(8);
            string winner = s.WinnerId < 0 ? "승자 없음" : (s.WinnerId == s.LocalId ? "승리!" : $"승자: {s.WinnerName}");
            GUILayout.Label(winner, _title, GUILayout.Height(48));
            GUILayout.Label($"라운드 {s.RoundSeconds:0.0}초", _center);
            GUILayout.Space(6);
            int rank = 1;
            foreach (var p in _ranking)
            {
                string tail = p.Id == s.WinnerId ? "생존" : (p.Connected ? $"{p.SurvivedSeconds:0.0}초" : "끊김");
                RosterRow(p, p.Id == s.LocalId, $"{rank}위  ", tail);
                rank++;
            }
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("다음 라운드", GUILayout.Height(34))) Game.Session?.RequestReady();
            if (GUILayout.Button("나가기", GUILayout.Height(34))) Game.Disconnect("");
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void RosterRow(RosterEntry p, bool isLocal, string prefix, string tail = null)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(26));
            var swatch = GUILayoutUtility.GetRect(20, 20, GUILayout.Width(20), GUILayout.Height(20));
            var old = GUI.color;
            GUI.color = PlayerColors.Get(p.ColorIndex);
            GUI.DrawTexture(swatch, Texture2D.whiteTexture);
            GUI.color = old;
            GUILayout.Space(8);
            string name = (prefix ?? "") + p.Name + (isLocal ? "  (나)" : "");
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
            }
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
