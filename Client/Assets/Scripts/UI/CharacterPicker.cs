using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Client.View;
using OnlineSeverGame.Shared.Protocol;
using UnityEngine;
using UnityEngine.UIElements;

namespace OnlineSeverGame.Client.UI
{
    // 캐릭터 고르기 칸(젤리·젤리곰·푸딩, docs/work/char-select.md). 접속 화면·대기 화면·결과 화면에 하나씩.
    // 세 칸 모두 내 색으로 그린다(색은 플레이어 번호. 접속 전에는 마지막으로 받은 번호의 색). 고른 칸은 레몬색 테 + 대기 동작,
    // 나머지는 첫 프레임을 조금 흐리게. 누르면 PlayerPrefs 에 기억하고(CharacterChoice) 접속 중이면 서버에 요청한다.
    // 서버는 대기·카운트다운이면 바로, 라운드 진행·결과 중이면 다음 라운드부터 바꾼다 → 그 사이에는 "다음 라운드부터 ○○" 를 보인다.
    public sealed class CharacterPicker
    {
        const float NameStrip = 22f;

        public readonly VisualElement Root;
        readonly GameUI _ui;
        readonly Label _title, _current, _note;
        readonly Button[] _tiles = new Button[3];
        readonly VisualElement[] _sprites = new VisualElement[3];
        readonly Label[] _names = new Label[3];
        readonly bool _showNames;

        // prefix 는 버튼 이름 앞머리("lobby" → "lobby-char-bear"). width = 칸 셋이 들어갈 폭, tileHeight = 칸 높이.
        public CharacterPicker(GameUI ui, string prefix, float width, float tileHeight, bool showNames)
        {
            _ui = ui;
            _showNames = showNames;
            Root = UiKit.El(null);
            Root.name = prefix + "-characters";

            var head = UiKit.El(null, Root);
            head.style.flexDirection = FlexDirection.Row;
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.alignItems = Align.Center;
            head.style.marginBottom = 4;
            _title = UiKit.Text("", "t-body", head);
            _current = UiKit.Text("", "t-small t-muted", head);

            var row = UiKit.El(null, Root);
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            float gap = 8f;
            float tw = Mathf.Floor((width - 2 * gap) / 3f);
            for (int i = 0; i < 3; i++)
            {
                var kind = CharacterChoice.All[i];
                var b = UiKit.Button(prefix + "-char-" + ArtSprites.KindPrefix(kind), "", () => Pick(kind), null, row);
                b.ClearClassList();
                b.AddToClassList("thumb");
                b.Clear(); // 광택 띠 대신 그림과 이름
                b.style.width = tw;
                b.style.height = tileHeight;
                b.style.overflow = Overflow.Hidden;
                b.style.backgroundColor = new Color(0.16f, 0.13f, 0.21f);

                // 프레임 256 px, 피벗(발)은 아래에서 8 px. 몸 높이가 칸의 약 3/4 이 되게(몸 ≈ 프레임 높이의 0.6).
                // 안쪽 높이 = 칸 높이 - 위 테 3 - 아래 테 5. 발은 이름 띠 바로 위(이름이 없으면 바닥에서 4 px 위).
                float floor = tileHeight - 8f - (showNames ? NameStrip : 4f) - 2f;
                float box = Mathf.Min(floor * 1.15f, tw * 1.6f);
                var sprite = UiKit.SpriteBox("char-sprite", b);
                sprite.style.position = Position.Absolute;
                sprite.style.width = box;
                sprite.style.height = box;
                sprite.style.left = (tw - 6f - box) / 2f; // 테 두께(3 px × 2)를 뺀 안쪽 가운데
                sprite.style.top = floor - box * (248f / 256f);
                _sprites[i] = sprite;

                var name = UiKit.Text("", "t-tiny t-center", b);
                name.style.position = Position.Absolute;
                name.style.left = 0; name.style.right = 0; name.style.bottom = 0;
                name.style.height = NameStrip;
                name.style.backgroundColor = new Color(0.08f, 0.06f, 0.11f, 0.78f);
                name.style.unityTextAlign = TextAnchor.MiddleCenter;
                UiKit.Show(name, showNames);
                _names[i] = name;
                _tiles[i] = b;
            }

            _note = UiKit.Text("", "t-tiny t-muted t-wrap", Root);
            _note.style.marginTop = 4;
        }

        void Pick(CharacterKind kind)
        {
            CharacterChoice.Set(kind);
            _ui.Game.Session?.RequestCharacter(kind);
        }

        // colorIndex = 그릴 색(내 플레이어 번호 - 1). applied = 서버가 알려 준 지금 내 캐릭터(접속 전이면 고른 값과 같게).
        // waitsForNextRound = 지금 바꾸면 다음 라운드부터인 화면(결과 화면).
        public void Refresh(int colorIndex, CharacterKind applied, bool waitsForNextRound, string extraNote, float now)
        {
            var chosen = CharacterChoice.Current;
            UiKit.SetText(_title, Loc.T("char.title"));
            UiKit.SetText(_current, _showNames ? "" : Loc.CharacterName(chosen));
            for (int i = 0; i < 3; i++)
            {
                var kind = CharacterChoice.All[i];
                bool on = kind == chosen;
                UiKit.SetClass(_tiles[i], "thumb--selected", on);
                UiKit.SetText(_names[i], Loc.CharacterName(kind));
                var clip = ArtSprites.Character(kind, colorIndex, "idle");
                UiKit.SetSprite(_sprites[i], on ? clip.At(now) : clip.Frames[0]);
                _sprites[i].style.opacity = on ? 1f : 0.7f;
            }

            string note = extraNote ?? "";
            if (applied != chosen && waitsForNextRound) note = Loc.F("char.next", Loc.CharacterName(chosen));
            UiKit.Show(_note, note.Length > 0);
            UiKit.SetText(_note, note);
        }
    }
}
