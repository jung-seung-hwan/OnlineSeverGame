using System.Collections.Generic;
using System.Text;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Client.View;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace OnlineSeverGame.Client.UI
{
    // 결과 화면. 가운데 카드: 왼쪽에 제목·1~3위 단상(젤리가 단상 위에서 움직인다, 1위는 승리 동작)·4위 아래 목록,
    // 오른쪽에 다음 맵 고르기·다음 라운드까지 남은 시간(줄어드는 막대)·나가기.
    public sealed class ResultView : UiView
    {
        static readonly float[] PodiumHeights = { 118f, 84f, 58f }; // 1위·2위·3위 자리 높이
        static readonly int[] PodiumColor = { 3, 6, 5 };             // 레몬(금)·하늘(은)·주황(동)

        readonly VisualElement _card, _podium, _rest, _nextFill;
        readonly Label _title, _sub, _next;
        readonly Button _leave;
        readonly MapPicker _map;
        readonly CharacterPicker _picker;
        readonly List<RosterEntry> _ranking = new List<RosterEntry>();
        readonly List<(VisualElement jelly, CharacterKind kind, int color, string anim)> _jellies =
            new List<(VisualElement, CharacterKind, int, string)>();
        readonly StringBuilder _sig = new StringBuilder();
        string _builtSig = "";
        float _countdownMax = 5f;
        float _cardAt = -10f;

        public ResultView(GameUI ui) : base(ui)
        {
            Root = UiKit.El("screen");
            Root.name = "screen-result";
            Root.pickingMode = PickingMode.Ignore;
            var center = UiKit.El("layer", Root);
            center.pickingMode = PickingMode.Ignore;
            center.style.alignItems = Align.Center;
            center.style.justifyContent = Justify.Center;

            _card = UiKit.El("panel", center);
            _card.style.flexDirection = FlexDirection.Row;
            _card.style.width = 1010;
            _card.style.marginTop = 40;

            var left = UiKit.El(null, _card);
            left.style.flexGrow = 1;
            left.style.flexShrink = 1;
            left.style.marginRight = 18;
            _title = UiKit.Text("", "t-h1 t-shadow", left);
            _title.style.whiteSpace = WhiteSpace.Normal;
            _sub = UiKit.Text("", "t-small t-muted t-center", left);
            _podium = UiKit.El(null, left);
            _podium.style.flexDirection = FlexDirection.Row;
            _podium.style.justifyContent = Justify.Center;
            _podium.style.alignItems = Align.FlexEnd;
            _podium.style.height = 290;
            _podium.style.marginTop = 6;
            _rest = UiKit.El(null, left);
            _rest.style.flexDirection = FlexDirection.Row;
            _rest.style.flexWrap = Wrap.Wrap;
            _rest.style.marginTop = 8;

            var right = UiKit.El(null, _card);
            right.style.width = 330;
            _map = new MapPicker(ui, "result", 330);
            right.Add(_map.Root);
            // 다음 라운드 캐릭터(결과 화면에서 고르면 다음 카운트다운부터). 이름 없이 작게: 고른 이름은 제목 줄 오른쪽에.
            _picker = new CharacterPicker(ui, "result", 330f, 64f, showNames: false);
            _picker.Root.style.marginTop = 8;
            right.Add(_picker.Root);
            var nextBox = UiKit.El(null, right);
            nextBox.style.marginTop = 12;
            _next = UiKit.Text("", "t-body t-center", nextBox);
            var bar = UiKit.El("gauge", nextBox);
            bar.style.width = new Length(100, LengthUnit.Percent);
            bar.style.height = 12;
            bar.style.marginTop = 6;
            _nextFill = UiKit.El("gauge-fill gauge-fill--ready", bar);
            _leave = UiKit.Button("btn-leave-result", "", Ui.Leave, null, right);
            _leave.style.marginTop = 14;
        }

        public void Refresh(IGameSession session, float now)
        {
            var s = session.State;
            if (Age(now) < 0.05f) { _countdownMax = Mathf.Max(1f, s.CountdownSeconds); _cardAt = now; }
            _countdownMax = Mathf.Max(_countdownMax, s.CountdownSeconds);

            _ranking.Clear();
            foreach (var p in s.Roster) if (p.Place > 0) _ranking.Add(p);
            _ranking.Sort((a, b) => a.Place != b.Place ? a.Place.CompareTo(b.Place) : b.SurvivedSeconds.CompareTo(a.SurvivedSeconds));

            string winner;
            if (s.WinnerIds.Count == 0) winner = Loc.T("result.noWinner");
            else if (s.WinnerIds.Contains(s.LocalId)) winner = s.WinnerIds.Count > 1 ? Loc.T("result.coWin") : Loc.T("result.win");
            else winner = Loc.F(s.WinnerIds.Count > 1 ? "result.coWinners" : "result.winner", s.WinnerName);
            UiKit.SetText(_title, winner);
            _title.style.color = s.WinnerIds.Contains(s.LocalId) ? PlayerColors.Get(3) : new Color(1f, 0.965f, 0.91f);
            UiKit.SetText(_sub, Loc.F("result.sub", s.RoundNumber, s.RoundSeconds));

            var stage = Stage.ById((byte)s.MapId) ?? Stage.Default;
            RebuildIfChanged(s, stage);

            // 단상 젤리 움직임
            foreach (var j in _jellies)
                UiKit.SetSprite(j.jelly, ArtSprites.Character(j.kind, j.color, j.anim).At(now + j.color * 0.31f));

            int secs = Mathf.CeilToInt(Mathf.Max(0f, s.CountdownSeconds));
            UiKit.SetText(_next, Loc.F("result.next", secs));
            _nextFill.style.width = new Length(Mathf.Clamp01(s.CountdownSeconds / _countdownMax) * 100f, LengthUnit.Percent);
            UiKit.SetText(_leave, Loc.T("hud.leave"));
            _map.Refresh(s);
            if (s.TryGet(s.LocalId, out RosterEntry me))
                _picker.Refresh(me.ColorIndex, me.Character, true, null, now);
            else
                _picker.Refresh(CharacterChoice.LastColor, CharacterChoice.Current, true, null, now);

            // 카드가 통 튀어나온다
            float t = (now - _cardAt) / 0.4f;
            float sc = t >= 1f ? 1f : Mathf.LerpUnclamped(0.85f, 1f, UiKit.EaseOutBack(t));
            _card.style.scale = new Scale(new Vector2(sc, sc));
        }

        void RebuildIfChanged(SessionState s, Stage stage)
        {
            _sig.Clear();
            _sig.Append(Loc.Current).Append('|').Append(s.LocalId).Append('|');
            foreach (var p in _ranking)
                _sig.Append(p.Id).Append(':').Append((int)p.Character).Append(':').Append(p.Place).Append(':').Append(p.Name).Append(':')
                    .Append(p.Survived).Append(p.LeftGame).Append((int)p.Cause).Append(':').Append(p.SurvivedSeconds.ToString("0.0")).Append(';');
            string sig = _sig.ToString();
            if (sig == _builtSig) return;
            _builtSig = sig;

            _podium.Clear();
            _rest.Clear();
            _jellies.Clear();
            // 단상 순서: 2위(왼쪽) · 1위(가운데) · 3위(오른쪽)
            int[] order = { 1, 0, 2 };
            foreach (int i in order)
            {
                if (i >= _ranking.Count) { _podium.Add(EmptyColumn()); continue; }
                _podium.Add(PodiumColumn(_ranking[i], i, _ranking[i].Id == s.LocalId, stage));
            }
            for (int i = 3; i < _ranking.Count; i++) _rest.Add(RestRow(_ranking[i], _ranking[i].Id == s.LocalId, stage));
        }

        static VisualElement EmptyColumn()
        {
            var col = UiKit.El("podium-col");
            col.style.height = 1;
            return col;
        }

        VisualElement PodiumColumn(RosterEntry p, int index, bool me, Stage stage)
        {
            var col = UiKit.El("podium-col");
            col.name = "podium-" + (index + 1);
            var jelly = UiKit.SpriteBox("podium-jelly", col);
            string anim = index == 0 && p.Place == 1 && !p.LeftGame ? "victory" : "idle";
            if (p.LeftGame) jelly.style.opacity = 0.35f;
            _jellies.Add((jelly, p.Character, p.ColorIndex, anim));

            var block = UiKit.El("podium-block", col);
            block.style.height = PodiumHeights[index];
            var bc = PlayerColors.Get(PodiumColor[index]);
            block.style.backgroundColor = bc;
            block.style.borderBottomColor = Color.Lerp(bc, Color.black, 0.4f);
            UiKit.Text(p.Place.ToString(), "podium-num", block);

            var name = UiKit.Text(p.Name + (me ? " " + Loc.T("roster.you") : ""), "podium-name", col);
            name.style.color = Color.Lerp(PlayerColors.Get(p.ColorIndex), Color.white, 0.5f);
            UiKit.Text(Tail(p, stage), "podium-time", col);
            return col;
        }

        static VisualElement RestRow(RosterEntry p, bool me, Stage stage)
        {
            var row = UiKit.El("rest-row");
            row.style.width = new Length(50, LengthUnit.Percent);
            var place = UiKit.Text(Loc.F("result.place", p.Place), "t-small t-muted", row);
            place.style.width = 40;
            UiKit.Face(p.ColorIndex, 26, row, p.Character).style.marginRight = 6;
            var name = UiKit.Text(p.Name + (me ? " " + Loc.T("roster.you") : ""), "t-small", row);
            name.style.flexShrink = 1;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            name.style.color = Color.Lerp(PlayerColors.Get(p.ColorIndex), Color.white, 0.5f);
            var tail = UiKit.Text(Tail(p, stage), "t-tiny t-muted", row);
            tail.style.marginLeft = 8;
            tail.style.flexShrink = 0;
            return row;
        }

        // 순위 옆 글: 예전과 같은 규칙(나감 / 생존 / "12.3초 · 눈덩이에 맞음" / 시간만)
        static string Tail(RosterEntry p, Stage stage)
        {
            if (p.LeftGame) return Loc.T("result.left");
            if (p.Survived) return Loc.T("result.survived");
            if (p.Cause == EliminationCause.SideKnife) return Loc.F("result.died", p.SurvivedSeconds, Loc.SideHazardName(stage.SideHazard));
            if (p.Cause == EliminationCause.Knife) return Loc.F("result.died", p.SurvivedSeconds, Loc.HazardName(stage.Hazard));
            if (p.Cause == EliminationCause.MapEvent) return Loc.F("result.burned", p.SurvivedSeconds); // v8: 주방 화구 불
            return Loc.F("hud.seconds", p.SurvivedSeconds);
        }
    }
}
