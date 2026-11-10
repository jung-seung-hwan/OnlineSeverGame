using System;
using System.Collections.Generic;
using OnlineSeverGame.Client.Model;
using UnityEngine.UIElements;

namespace OnlineSeverGame.Client.UI
{
    // 다른 작업(음량 설정, 이모트, 효과음 등)이 UI 에 붙는 자리(docs/work/ui-revamp.md 5장).
    // GameUI 를 고치지 않고 자기 파일에서 등록만 하면 된다. 등록은 언제 해도 되고(게임 시작 전이면 화면이 만들어질 때,
    // 뒤면 바로) 화면이 다시 만들어져도 다시 붙는다.
    //
    //   // 설정 화면에 '소리' 칸 채우기(예: 음량 슬라이더)
    //   UiHooks.AddSettingsSection("sound", order: 10, build: box =>
    //       UiKit.SliderRow("slider-master", Loc.T("sound.master"), Audio.Master, v => Audio.Master = v, box));
    //
    //   // HUD 오른쪽 아래에 이모트 버튼 줄 붙이기
    //   UiHooks.AddHudElement(HudSlot.BottomRight, slot => UiKit.Button("btn-emote-1", Loc.T("emote.1"), Emote1, "jb--small", slot));
    //
    //   // 버튼 소리: 모든 젤리 버튼 클릭이 이름과 함께 온다
    //   UiHooks.ButtonClicked += name => Sfx.Play("ui_click");
    public enum HudSlot
    {
        TopLeft,      // 생존 수 아래
        TopRight,     // 라운드·핑 아래
        BottomLeft,   // 대시 게이지 옆
        BottomRight,  // 비어 있음(이모트 자리로 추천)
        BottomCenter, // 조작 안내 위
    }

    public static class UiHooks
    {
        public sealed class SettingsSection
        {
            public string Id;            // "sound" 는 이미 제목('소리')과 자리가 있다. 다른 id 는 titleKey 로 제목을 단다.
            public string TitleKey;      // Loc 키. null 이면 id 에 맞는 기본 제목
            public int Order;
            public Action<VisualElement> Build;
        }

        sealed class HudEntry
        {
            public HudSlot Slot;
            public Action<VisualElement> Build;
        }

        static readonly List<SettingsSection> _sections = new List<SettingsSection>();
        static readonly List<HudEntry> _hud = new List<HudEntry>();

        // 설정 화면을 다시 그려야 할 때(등록이 늘었을 때) GameUI 가 받는다.
        internal static event Action Changed;

        // 젤리 버튼이 눌릴 때마다(이름). 효과음 등에 쓴다.
        public static event Action<string> ButtonClicked;

        // 화면이 바뀔 때(이전, 다음). 전환 소리·음악 등에 쓴다.
        public static event Action<ClientScreen, ClientScreen> ScreenChanged;

        // 설정 화면 칸. id 가 같으면 바꿔 끼운다. build 는 칸 상자(세로로 쌓임)를 받아 안을 채운다.
        public static void AddSettingsSection(string id, int order, Action<VisualElement> build, string titleKey = null)
        {
            _sections.RemoveAll(s => s.Id == id);
            _sections.Add(new SettingsSection { Id = id, Order = order, Build = build, TitleKey = titleKey });
            _sections.Sort((a, b) => a.Order.CompareTo(b.Order));
            Changed?.Invoke();
        }

        // HUD 자리에 요소를 붙인다. build 는 그 자리 상자(가로로 쌓임)를 받는다. 진행·관전·결과 화면에서 보인다.
        public static void AddHudElement(HudSlot slot, Action<VisualElement> build)
        {
            _hud.Add(new HudEntry { Slot = slot, Build = build });
            Changed?.Invoke();
        }

        internal static IReadOnlyList<SettingsSection> SettingsSections => _sections;

        internal static void BuildHud(HudSlot slot, VisualElement box)
        {
            foreach (var h in _hud) if (h.Slot == slot) h.Build(box);
        }

        internal static void RaiseButtonClicked(string name) => ButtonClicked?.Invoke(name);

        internal static void RaiseScreenChanged(ClientScreen from, ClientScreen to) => ScreenChanged?.Invoke(from, to);
    }
}
