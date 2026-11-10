using System.Text;
using OnlineSeverGame.Client.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace OnlineSeverGame.Client.UI
{
    // 대기·카운트다운 화면. 이때도 캐릭터를 움직일 수 있으므로 가운데는 비워 둔다:
    // 왼쪽 접속자 카드(젤리 얼굴·플레이어 색·나/방장 표시), 위 가운데 상태 띠, 오른쪽 위 버튼·작은 로고·다음 맵 고르기,
    // 아래 가운데 조작 안내. 카운트다운이면 가운데에 큰 숫자가 1초마다 튀어나왔다 줄어든다.
    public sealed class LobbyView : UiView
    {
        readonly VisualElement _cards, _status;
        readonly Label _rosterTitle, _head, _sub, _tip, _ping, _bigNumber, _roundLine;
        readonly Button _settingsBtn, _leaveBtn;
        readonly MapPicker _map;
        readonly TitleLogoElement _logo;
        readonly HintBar _hint;
        readonly StringBuilder _sig = new StringBuilder();
        string _builtSig = "";

        public LobbyView(GameUI ui) : base(ui)
        {
            Root = UiKit.El("screen");
            Root.name = "screen-lobby";
            Root.pickingMode = PickingMode.Ignore;

            // ---- 왼쪽: 접속자 카드
            var left = UiKit.El("panel panel--tight panel--soft", Root);
            left.style.position = Position.Absolute;
            left.style.left = 14; left.style.top = 14;
            left.style.width = 300;
            _rosterTitle = UiKit.Text("", "t-h2", left);
            _rosterTitle.style.marginBottom = 8;
            _cards = UiKit.El(null, left);

            // ---- 위 가운데: 상태 띠
            _status = UiKit.El("panel panel--tight panel--soft", Root);
            _status.style.position = Position.Absolute;
            _status.style.top = 14;
            _status.style.left = new Length(50, LengthUnit.Percent);
            _status.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            _status.style.maxWidth = 560;
            _status.style.alignItems = Align.Center;
            _head = UiKit.Text("", "t-h2 t-center", _status);
            _sub = UiKit.Text("", "t-small t-muted t-center t-wrap", _status);
            _tip = UiKit.Text("", "t-small t-center t-wrap", _status);

            // ---- 오른쪽: 버튼, 작은 로고, 맵 고르기
            var right = UiKit.El(null, Root);
            right.pickingMode = PickingMode.Ignore;
            right.style.position = Position.Absolute;
            right.style.right = 14; right.style.top = 14;
            right.style.width = 330;
            right.style.alignItems = Align.FlexEnd;
            var bar = UiKit.El(null, right);
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            _ping = UiKit.Text("", "t-small t-muted t-shadow", bar);
            _ping.style.marginRight = 8;
            _settingsBtn = UiKit.Button("btn-settings-lobby", "", Ui.OpenSettings, "jb--small jb--grape", bar);
            _settingsBtn.style.marginRight = 6;
            _leaveBtn = UiKit.Button("btn-leave-lobby", "", Ui.Leave, "jb--small", bar);
            _logo = new TitleLogoElement(animated: false);
            _logo.SetPpu(2.2f);
            _logo.style.marginTop = 10;
            _logo.style.marginBottom = 10;
            right.Add(_logo);
            _map = new MapPicker(ui, "lobby", 330);
            right.Add(_map.Root);

            // ---- 가운데: 카운트다운 숫자
            var center = UiKit.El("layer", Root);
            center.pickingMode = PickingMode.Ignore;
            center.style.alignItems = Align.Center;
            center.style.justifyContent = Justify.Center;
            _roundLine = UiKit.Text("", "t-h2 t-center t-shadow", center);
            _bigNumber = UiKit.Text("", "t-display", center);
            _bigNumber.style.height = 190;
            _bigNumber.style.width = 300;

            _hint = new HintBar(Root);
        }

        public void Refresh(IGameSession session, float now)
        {
            var s = session.State;
            bool counting = s.Screen == ClientScreen.Countdown;

            RefreshCards(s);
            UiKit.SetText(_rosterTitle, Loc.F("lobby.roster", s.Roster.Count, s.Capacity));

            UiKit.SetText(_head, counting ? Loc.F("lobby.countdown", Mathf.CeilToInt(Mathf.Max(0f, s.CountdownSeconds))) : Loc.T("lobby.waiting"));
            UiKit.SetText(_sub, counting
                ? Loc.F("lobby.countdownSub", s.RoundNumber, Loc.MapName(s.MapId), s.ParticipantCount)
                : Loc.F("lobby.waitingSub", s.Roster.Count, s.Capacity));
            UiKit.Show(_tip, counting);
            if (counting) UiKit.SetText(_tip, Loc.T("lobby.countdownTip"));

            UiKit.SetText(_ping, s.PingMs >= 0 ? Loc.F("hud.ping", s.PingMs) : "");
            UiKit.SetText(_settingsBtn, Loc.T("ui.settings"));
            UiKit.SetText(_leaveBtn, Loc.T("hud.leave"));
            _map.Refresh(s);

            // 큰 숫자: 매 초 처음 0.25초에 1.7배에서 튀어 들어오고, 끝 0.2초에 흐려진다. 3·2·1 마다 플레이어 색.
            UiKit.Show(_bigNumber, counting);
            UiKit.Show(_roundLine, counting);
            if (counting)
            {
                float left = Mathf.Max(0f, s.CountdownSeconds);
                int n = Mathf.Max(1, Mathf.CeilToInt(left));
                float t = 1f - (left - (n - 1)); // 이 숫자가 뜬 뒤 지난 비율 0..1
                UiKit.SetText(_bigNumber, n.ToString());
                UiKit.SetText(_roundLine, Loc.F("countdown.round", s.RoundNumber, Loc.MapName(s.MapId)));
                float pop = UiKit.EaseOutBack(t / 0.25f);
                float scale = Mathf.LerpUnclamped(1.7f, 1f, pop);
                _bigNumber.style.scale = new Scale(new Vector2(scale, scale));
                _bigNumber.style.opacity = t > 0.8f ? Mathf.Clamp01((1f - t) / 0.2f) * 0.9f + 0.1f : 1f;
                int[] colors = { 2, 1, 4 }; // 1 = 라임, 2 = 블루베리, 3 = 포도
                _bigNumber.style.color = PlayerColors.Get(colors[Mathf.Clamp(n - 1, 0, 2)]);
            }

            _hint.Refresh(s, 1f);
        }

        void RefreshCards(SessionState s)
        {
            _sig.Clear();
            _sig.Append(s.LocalId).Append('|').Append(s.HostId).Append('|');
            foreach (var p in s.Roster) _sig.Append(p.Id).Append(':').Append(p.ColorIndex).Append(':').Append(p.Name).Append(';');
            _sig.Append(Loc.Current);
            string sig = _sig.ToString();
            if (sig == _builtSig) return;
            _builtSig = sig;
            _cards.Clear();
            foreach (var p in s.Roster) _cards.Add(PlayerCard(p, p.Id == s.LocalId, s.HostId != 0 && p.Id == s.HostId));
        }

        // 접속자 카드: 젤리 얼굴(플레이어 색 테두리) + 이름 + 나·방장 표시. 왼쪽 띠도 플레이어 색.
        public static VisualElement PlayerCard(RosterEntry p, bool me, bool host)
        {
            var card = UiKit.El("card");
            if (me) card.AddToClassList("card--me");
            card.name = "card-" + p.Id;
            var color = PlayerColors.Get(p.ColorIndex);
            var face = UiKit.Face(p.ColorIndex, 40, card);
            face.style.borderLeftColor = color; face.style.borderRightColor = color;
            face.style.borderTopColor = color; face.style.borderBottomColor = color;
            var name = UiKit.Text(p.Name, "card-name", card);
            name.style.color = Color.Lerp(color, Color.white, 0.55f);
            if (me) UiKit.Text(Loc.T("roster.you").Trim('(', ')'), "badge badge--me", card);
            if (host) UiKit.Text(Loc.T("roster.host"), "badge badge--host", card);
            return card;
        }
    }

    // 아래 가운데 조작 안내 두 줄(조작, 이 맵의 옆 위험물). 줄이 길면 감싸서 잘리지 않는다.
    public sealed class HintBar
    {
        readonly VisualElement _box;
        readonly Label _controls, _side;

        public HintBar(VisualElement parent)
        {
            _box = UiKit.El("pill", parent);
            _box.pickingMode = PickingMode.Ignore;
            _box.style.position = Position.Absolute;
            _box.style.bottom = 3;
            _box.style.left = new Length(50, LengthUnit.Percent);
            _box.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            _box.style.maxWidth = 1180;
            _box.style.flexDirection = FlexDirection.Column;
            _box.style.backgroundColor = new Color(0.1f, 0.08f, 0.14f, 0.6f);
            _box.style.paddingTop = 1; _box.style.paddingBottom = 2;
            _box.style.borderBottomWidth = 0;
            _controls = UiKit.Text("", "t-tiny t-center t-wrap", _box);
            _side = UiKit.Text("", "t-tiny t-center t-wrap t-muted", _box);
        }

        public void Refresh(SessionState s, float alpha)
        {
            UiKit.Show(_box, alpha > 0.01f);
            _box.style.opacity = alpha;
            UiKit.SetText(_controls, Loc.T("hint.controls"));
            UiKit.SetText(_side, Loc.T("hint.side." + Mathf.Clamp(s.MapId, 1, 4)));
        }
    }
}
