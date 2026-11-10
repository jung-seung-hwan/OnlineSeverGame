using System;
using System.Collections.Generic;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Client.View;
using OnlineSeverGame.Shared.Protocol;
using UnityEngine;
using UnityEngine.UIElements;

namespace OnlineSeverGame.Client.UI
{
    // 화면 조각을 만드는 도구(UI Toolkit). 모양은 Resources/UI/Jelly.uss, 기준은 docs/work/ui-revamp.md 2장.
    // 다른 세션이 설정 화면·HUD 에 붙일 때도 이 함수들을 쓰면 같은 모양이 된다(UiHooks).
    public static class UiKit
    {
        // 버튼 이름(name) → 버튼. 클릭 자동 시험(UiTest)이 이름으로 찾는다. 같은 이름이 다시 만들어지면 새 것으로 바뀐다.
        static readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>();

        public static VisualElement El(string cls = null, VisualElement parent = null)
        {
            var e = new VisualElement();
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) e.AddToClassList(c);
            parent?.Add(e);
            return e;
        }

        public static Label Text(string text, string cls = null, VisualElement parent = null)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) l.AddToClassList(c);
            parent?.Add(l);
            return l;
        }

        // 젤리 버튼. name 은 시험·확장에서 찾는 이름(예: "btn-join"). 키보드 포커스는 받지 않는다:
        // 포커스가 남으면 게임 중 Space(점프)가 버튼을 다시 누르게 되기 때문이다.
        // 글은 버튼 자체가 아니라 안쪽 Label(.jb-label)에 넣는다: 광택 띠(자식 요소)가 있는 버튼은 글 폭을 재지 못해
        // 폭이 0으로 줄기 때문이다. 글을 바꿀 때는 UiKit.SetText(button, ...) / ButtonText(button) 를 쓴다.
        public static Button Button(string name, string text, Action onClick, string cls = null, VisualElement parent = null)
        {
            var b = new Button { name = name, focusable = false };
            b.RemoveFromClassList(UnityEngine.UIElements.Button.ussClassName); // 기본 테마 버튼 모양을 쓰지 않는다
            b.AddToClassList("jb");
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) b.AddToClassList(c);
            var shine = new VisualElement { pickingMode = PickingMode.Ignore };
            shine.AddToClassList("jb-shine");
            b.Add(shine);
            var label = new Label(text ?? "") { pickingMode = PickingMode.Ignore };
            label.AddToClassList("jb-label");
            b.Add(label);
            b.clicked += () =>
            {
                UiHooks.RaiseButtonClicked(name);
                onClick?.Invoke();
            };
            _buttons[name] = b;
            parent?.Add(b);
            return b;
        }

        public static string ButtonText(Button b)
        {
            var l = b.Q<Label>(className: "jb-label");
            return l != null ? l.text : b.text;
        }

        public static bool TryGetButton(string name, out Button b) => _buttons.TryGetValue(name, out b);

        public static IEnumerable<string> ButtonNames => _buttons.Keys;

        // 위아래로 쌓인 젤리 몸 가운데를 둥근 테 안에 크게 보여 주는 얼굴. 색은 플레이어 색 테두리.
        // kind = 캐릭터 종류(젤리·젤리곰·푸딩, v10). 세 종류 모두 프레임 안 몸 위치가 같아 같은 배율로 보인다.
        public static VisualElement Face(int colorIndex, float size, VisualElement parent = null,
            CharacterKind kind = CharacterKind.Jelly)
        {
            var ring = El("face", parent);
            ring.style.width = size;
            ring.style.height = size;
            float r = size / 2f;
            ring.style.borderTopLeftRadius = r; ring.style.borderTopRightRadius = r;
            ring.style.borderBottomLeftRadius = r; ring.style.borderBottomRightRadius = r;
            var c = PlayerColors.Get(colorIndex);
            ring.style.backgroundColor = Color.Lerp(c, new Color(0.08f, 0.06f, 0.1f), 0.55f);
            var sprite = El("face-sprite", ring);
            // 프레임 256 px 안에서 몸은 가운데 아래쪽(폭 약 0.4, 높이 약 0.5)에 있다. 2.2배로 키워 몸이 테를 채우게 한다.
            float s = size * 2.2f;
            sprite.style.width = s;
            sprite.style.height = s;
            sprite.style.left = (size - s) / 2f;
            sprite.style.top = size - s * 0.97f;
            var frame = ArtSprites.Character(kind, colorIndex, "idle").Frames[0];
            sprite.style.backgroundImage = new StyleBackground(frame);
            return ring;
        }

        // 스프라이트 한 프레임을 보여 주는 칸(단상 젤리 등). 프레임은 바깥에서 매 프레임 바꾼다.
        public static VisualElement SpriteBox(string cls, VisualElement parent = null)
        {
            var e = El(cls, parent);
            e.pickingMode = PickingMode.Ignore;
            e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            return e;
        }

        public static void SetSprite(VisualElement e, Sprite s)
        {
            if (s == null) return;
            var cur = e.style.backgroundImage.value;
            if (cur.sprite == s) return;
            e.style.backgroundImage = new StyleBackground(s);
        }

        public static Label KeyCap(string key, VisualElement parent = null) => Text(key, "keycap", parent);

        // 설정 화면 등에 쓰는 슬라이더 한 줄(0..1). 값이 바뀌면 set 을 부른다.
        public static Slider SliderRow(string name, string label, float value, Action<float> set, VisualElement parent = null)
        {
            var row = El("slider-row", parent);
            Text(label, "t-body", row).AddToClassList("unity-label");
            var s = new Slider(0f, 1f) { name = name, value = value, focusable = false };
            s.style.flexGrow = 1;
            s.RegisterValueChangedCallback(e => set?.Invoke(e.newValue));
            row.Add(s);
            return s;
        }

        public static void Show(VisualElement e, bool show)
        {
            var d = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (e.style.display != d) e.style.display = d;
        }

        public static void SetText(Label l, string text)
        {
            if (l.text != text) l.text = text;
        }

        public static void SetText(Button b, string text)
        {
            var l = b.Q<Label>(className: "jb-label");
            if (l != null) SetText(l, text);
            else if (b.text != text) b.text = text;
        }

        public static void SetClass(VisualElement e, string cls, bool on)
        {
            if (on) { if (!e.ClassListContains(cls)) e.AddToClassList(cls); }
            else if (e.ClassListContains(cls)) e.RemoveFromClassList(cls);
        }

        public static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        public static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t);
        }
    }
}
