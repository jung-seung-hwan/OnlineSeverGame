using System.Collections.Generic;
using OnlineSeverGame.Client.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace OnlineSeverGame.Client.UI
{
    // 진행 HUD(진행·관전·결과 화면 아래층). 플레이를 가리지 않게 모든 것을 화면 가장자리에 작게 둔다:
    //   왼쪽 위  생존 수 + 참가자 점(살아 있으면 플레이어 색, 탈락하면 흐림)
    //   위 가운데 라운드 시간, 옆 위험물이 나오기 직전(17~22초) 경고 띠, 관전 중이면 관전 안내
    //   오른쪽 위 라운드·핑, 설정·나가기 버튼
    //   왼쪽 아래 대시 게이지(차면 하늘색 '준비'), 아래 가운데 조작 안내(라운드 처음 6초만)
    //   시작 순간 가운데에 '시작!'이 0.8초 튀어나온다.
    // 확장 자리(UiHooks.AddHudElement): 왼쪽 위·오른쪽 위 아래, 왼쪽 아래 게이지 옆, 오른쪽 아래, 아래 가운데.
    public sealed class HudView : UiView
    {
        readonly VisualElement _dots, _warnPill, _spectate, _dashBox, _gaugeFill, _topRight;
        readonly Label _alive, _timer, _warnText, _info, _dashLabel, _dashState, _specTitle, _specSub, _specNext, _go;
        readonly Button _settingsBtn, _leaveBtn;
        readonly Dictionary<HudSlot, VisualElement> _slots = new Dictionary<HudSlot, VisualElement>();
        readonly List<VisualElement> _dotPool = new List<VisualElement>();
        readonly HintBar _hint;

        public HudView(GameUI ui) : base(ui)
        {
            EnterOffset = -12f; // 위에서 살짝 내려온다
            Root = UiKit.El("screen");
            Root.name = "screen-hud";
            Root.pickingMode = PickingMode.Ignore;

            // ---- 왼쪽 위
            var tl = Corner(14, 12, null, null, Align.FlexStart);
            var alive = UiKit.El("pill", tl);
            _alive = UiKit.Text("", "t-body", alive);
            _alive.style.marginRight = 6;
            _dots = UiKit.El(null, alive);
            _dots.style.flexDirection = FlexDirection.Row;
            _slots[HudSlot.TopLeft] = Slot(tl, FlexDirection.Column);

            // ---- 위 가운데
            var tc = UiKit.El(null, Root);
            tc.pickingMode = PickingMode.Ignore;
            tc.style.position = Position.Absolute;
            tc.style.top = 12; tc.style.left = 0; tc.style.right = 0;
            tc.style.alignItems = Align.Center;
            var timerPill = UiKit.El("pill", tc);
            _timer = UiKit.Text("", "t-h2", timerPill);
            _timer.style.minWidth = 70;
            _timer.style.unityTextAlign = TextAnchor.MiddleCenter;
            _warnPill = UiKit.El("pill warn-pill", tc);
            _warnPill.style.marginTop = 8;
            _warnText = UiKit.Text("", "t-small", _warnPill);
            // 관전 안내: 떨어지는 위험물을 가리지 않게 시간 아래 작은 두 줄
            _spectate = UiKit.El("pill", tc);
            _spectate.style.marginTop = 8;
            _spectate.style.flexDirection = FlexDirection.Column;
            _spectate.style.backgroundColor = new Color(0.1f, 0.08f, 0.14f, 0.7f);
            var specRow = UiKit.El(null, _spectate);
            specRow.style.flexDirection = FlexDirection.Row;
            specRow.style.alignItems = Align.Center;
            _specTitle = UiKit.Text("", "t-body", specRow);
            _specTitle.style.color = PlayerColors.Get(3);
            _specSub = UiKit.Text("", "t-small", specRow);
            _specSub.style.marginLeft = 10;
            _specNext = UiKit.Text("", "t-tiny t-muted t-center", _spectate);

            // ---- 오른쪽 위
            _topRight = Corner(null, 12, 14, null, Align.FlexEnd);
            var bar = UiKit.El(null, _topRight);
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            _info = UiKit.Text("", "t-small t-muted t-shadow", bar);
            _info.style.marginRight = 8;
            _settingsBtn = UiKit.Button("btn-settings-hud", "", Ui.OpenSettings, "jb--small jb--grape", bar);
            _settingsBtn.style.marginRight = 6;
            _leaveBtn = UiKit.Button("btn-leave-hud", "", Ui.Leave, "jb--small", bar);
            _slots[HudSlot.TopRight] = Slot(_topRight, FlexDirection.Column);

            // ---- 왼쪽 아래: 대시 게이지
            var bl = Corner(14, null, null, 12, Align.FlexStart);
            bl.style.flexDirection = FlexDirection.Row;
            bl.style.alignItems = Align.FlexEnd;
            _dashBox = UiKit.El("pill", bl);
            UiKit.KeyCap("Shift", _dashBox);
            _dashLabel = UiKit.Text("", "t-small", _dashBox);
            _dashLabel.style.marginRight = 8;
            var gauge = UiKit.El("gauge", _dashBox);
            _gaugeFill = UiKit.El("gauge-fill", gauge);
            _dashState = UiKit.Text("", "t-tiny", _dashBox);
            _dashState.style.marginLeft = 8;
            _dashState.style.minWidth = 34;
            _slots[HudSlot.BottomLeft] = Slot(bl, FlexDirection.Row);

            // ---- 오른쪽 아래
            var br = Corner(null, null, 14, 12, Align.FlexEnd);
            _slots[HudSlot.BottomRight] = Slot(br, FlexDirection.Row);

            // ---- 아래 가운데
            var bc = UiKit.El(null, Root);
            bc.pickingMode = PickingMode.Ignore;
            bc.style.position = Position.Absolute;
            bc.style.bottom = 70; bc.style.left = 0; bc.style.right = 0;
            bc.style.alignItems = Align.Center;
            _slots[HudSlot.BottomCenter] = Slot(bc, FlexDirection.Row);
            _hint = new HintBar(Root);

            // ---- '시작!'
            var center = UiKit.El("layer", Root);
            center.pickingMode = PickingMode.Ignore;
            center.style.alignItems = Align.Center;
            center.style.justifyContent = Justify.Center;
            _go = UiKit.Text("", "t-display", center);
            _go.style.fontSize = 110;
            _go.style.color = PlayerColors.Get(3);

            RebuildSlots();
        }

        VisualElement Corner(float? left, float? top, float? right, float? bottom, Align align)
        {
            var c = UiKit.El(null, Root);
            c.pickingMode = PickingMode.Ignore;
            c.style.position = Position.Absolute;
            if (left.HasValue) c.style.left = left.Value;
            if (top.HasValue) c.style.top = top.Value;
            if (right.HasValue) c.style.right = right.Value;
            if (bottom.HasValue) c.style.bottom = bottom.Value;
            c.style.alignItems = align;
            return c;
        }

        static VisualElement Slot(VisualElement parent, FlexDirection dir)
        {
            var s = UiKit.El(null, parent);
            s.pickingMode = PickingMode.Ignore;
            s.style.flexDirection = dir;
            s.style.marginTop = 6;
            s.style.marginLeft = dir == FlexDirection.Row ? 8 : 0;
            return s;
        }

        // 확장 자리를 비우고 UiHooks 에 등록된 것을 다시 붙인다.
        public void RebuildSlots()
        {
            foreach (var kv in _slots)
            {
                kv.Value.Clear();
                UiHooks.BuildHud(kv.Key, kv.Value);
            }
        }

        public void Refresh(IGameSession session, float now)
        {
            var s = session.State;
            bool playing = s.Screen == ClientScreen.Playing;
            bool spectating = s.Screen == ClientScreen.Spectating;
            bool result = s.Screen == ClientScreen.Result;

            UiKit.SetText(_alive, Loc.F("hud.alive", s.AliveCount, s.ParticipantCount));
            RefreshDots(s);
            UiKit.SetText(_timer, Loc.F("hud.time", s.RoundSeconds));

            string info = s.PingMs >= 0 ? Loc.F("hud.ping", s.PingMs) : "";
            if (s.RoundNumber > 0) info = Loc.F("hud.round", s.RoundNumber) + (info.Length > 0 ? "  ·  " + info : "");
            UiKit.SetText(_info, info);
            UiKit.SetText(_settingsBtn, Loc.T("ui.settings"));
            UiKit.SetText(_leaveBtn, Loc.T("hud.leave"));

            // 옆 위험물 경고: 20초에 처음 나오므로 17초부터 22초까지(위험물 경고라 빨강을 쓴다)
            bool warn = playing && s.RoundSeconds >= 17f && s.RoundSeconds < 22f;
            UiKit.Show(_warnPill, warn);
            if (warn)
            {
                UiKit.SetText(_warnText, Loc.T("hint.side." + Mathf.Clamp(s.MapId, 1, 4)));
                _warnPill.style.opacity = 0.75f + 0.25f * Mathf.PingPong(now * 2f, 1f);
            }

            UiKit.Show(_spectate, spectating);
            if (spectating)
            {
                UiKit.SetText(_specTitle, s.LocalInRound ? Loc.T("spec.out") : Loc.T("spec.watching"));
                UiKit.SetText(_specSub, s.LocalInRound ? Loc.F("spec.alive", s.AliveCount) : Loc.T("spec.nextRound"));
                UiKit.SetText(_specNext, Loc.F("spec.nextMap", Loc.MapName(s.NextMapId > 0 ? s.NextMapId : s.MapId), Loc.RotationName(s.Rotation)));
            }

            // 대시 게이지: 내 캐릭터의 쿨다운(0 = 가능, 1 = 막 씀)
            float cooldown = 0f;
            bool alive = false;
            foreach (var p in session.World.Players)
                if (p.IsLocal) { cooldown = p.DashCooldown; alive = p.Alive; }
            UiKit.Show(_dashBox, playing && alive);
            float fill = 1f - Mathf.Clamp01(cooldown);
            _gaugeFill.style.width = new Length(fill * 100f, LengthUnit.Percent);
            UiKit.SetClass(_gaugeFill, "gauge-fill--ready", fill >= 1f);
            UiKit.SetText(_dashLabel, Loc.T("hud.dash"));
            UiKit.SetText(_dashState, fill >= 1f ? Loc.T("hud.dashReady") : "");

            // 조작 안내: 라운드 처음 6초(마지막 1초 동안 흐려짐)
            float hintAlpha = playing ? Mathf.Clamp01(6f - s.RoundSeconds) : 0f;
            _hint.Refresh(s, hintAlpha);

            // '시작!'
            float goT = playing ? s.RoundSeconds / 0.8f : 2f;
            UiKit.Show(_go, goT < 1f);
            if (goT < 1f)
            {
                UiKit.SetText(_go, Loc.T("countdown.go"));
                float sc = Mathf.LerpUnclamped(0.5f, 1f, UiKit.EaseOutBack(goT / 0.35f));
                _go.style.scale = new Scale(new Vector2(sc, sc));
                _go.style.opacity = goT > 0.6f ? (1f - goT) / 0.4f : 1f;
            }

            UiKit.Show(_topRight, true);
            _slots[HudSlot.BottomCenter].style.display = result ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void RefreshDots(SessionState s)
        {
            int n = 0;
            foreach (var p in s.Roster)
            {
                if (!p.InRound) continue;
                if (n >= _dotPool.Count)
                {
                    var d = UiKit.El("hud-dot", _dots);
                    _dotPool.Add(d);
                }
                var dot = _dotPool[n++];
                UiKit.Show(dot, true);
                var c = PlayerColors.Get(p.ColorIndex);
                dot.style.backgroundColor = p.Alive ? c : new Color(c.r, c.g, c.b, 0.18f);
                dot.style.opacity = p.Alive ? 1f : 0.6f;
            }
            for (int i = n; i < _dotPool.Count; i++) UiKit.Show(_dotPool[i], false);
        }
    }
}
