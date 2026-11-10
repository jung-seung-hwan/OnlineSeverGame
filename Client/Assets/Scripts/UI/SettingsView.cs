using OnlineSeverGame.Client.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace OnlineSeverGame.Client.UI
{
    // 설정 창(어느 화면에서나 위에 뜬다. 버튼 또는 Esc 로 열고 닫는다. 바깥 어두운 곳을 눌러도 닫힌다).
    //   언어: 한국어 / English (PlayerPrefs "lang" 에 기억, 예전 언어 버튼과 같은 Loc.Set)
    //   소리: 다른 작업이 UiHooks.AddSettingsSection("sound", ...) 로 채우는 자리. 비어 있으면 '곧 추가됩니다'.
    //   그 밖의 칸: UiHooks 로 등록한 순서대로 소리 아래에 붙는다.
    //   조작: 키 안내(읽기만)
    // 게임은 멈추지 않는다(온라인 게임이라). 창이 떠 있어도 캐릭터 조작 키는 그대로 들어간다.
    public sealed class SettingsView
    {
        public readonly VisualElement Root;
        readonly GameUI _ui;
        readonly VisualElement _card, _soundBox, _extraBox, _controls;
        readonly Label _title, _langTitle, _soundTitle, _soundEmpty, _controlsTitle, _esc;
        readonly Button _close, _ko, _en;
        float _openedAt = -10f;
        Language _builtControls = (Language)(-1);

        public bool IsOpen { get; private set; }

        public SettingsView(GameUI ui)
        {
            _ui = ui;
            Root = UiKit.El("modal-back");
            Root.name = "settings";
            Root.RegisterCallback<PointerDownEvent>(e => { if (e.target == Root) Close(); });

            _card = UiKit.El("panel", Root);
            _card.name = "settings-card";
            _card.style.width = 580;

            var head = UiKit.El(null, _card);
            head.style.flexDirection = FlexDirection.Row;
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.alignItems = Align.Center;
            _title = UiKit.Text("", "t-h1", head);
            _close = UiKit.Button("btn-settings-close", "", Close, "jb--small", head);

            _langTitle = UiKit.Text("", "section-title", _card);
            var langRow = UiKit.El("seg-row", _card);
            langRow.style.marginTop = 0;
            _ko = UiKit.Button("btn-lang-ko", "", () => Loc.Set(Language.Korean), null, langRow);
            _en = UiKit.Button("btn-lang-en", "", () => Loc.Set(Language.English), null, langRow);
            _en.style.marginRight = 0;

            _soundTitle = UiKit.Text("", "section-title", _card);
            _soundBox = UiKit.El(null, _card);
            _soundBox.name = "settings-sound";
            _soundEmpty = UiKit.Text("", "t-small t-muted", _card);
            _extraBox = UiKit.El(null, _card);

            _controlsTitle = UiKit.Text("", "section-title", _card);
            _controls = UiKit.El(null, _card);
            _esc = UiKit.Text("", "t-tiny t-muted t-right", _card);
            _esc.style.marginTop = 8;

            RebuildSections();
            Close();
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            _openedAt = _ui.Now;
            UiKit.Show(Root, true);
        }

        public void Close()
        {
            IsOpen = false;
            UiKit.Show(Root, false);
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        // UiHooks 로 등록된 칸을 다시 만든다. "sound" 는 소리 칸에, 나머지는 그 아래 제목을 달아 붙인다.
        public void RebuildSections()
        {
            _soundBox.Clear();
            _extraBox.Clear();
            bool sound = false;
            foreach (var sec in UiHooks.SettingsSections)
            {
                if (sec.Id == "sound")
                {
                    sound = true;
                    sec.Build(_soundBox);
                    continue;
                }
                var title = UiKit.Text(Loc.T(sec.TitleKey ?? sec.Id), "section-title", _extraBox);
                title.name = "settings-title-" + sec.Id;
                var box = UiKit.El(null, _extraBox);
                box.name = "settings-" + sec.Id;
                sec.Build(box);
            }
            UiKit.Show(_soundEmpty, !sound);
        }

        public void Refresh(ClientScreen screen, float now)
        {
            if (!IsOpen) return;
            UiKit.SetText(_title, Loc.T("settings.title"));
            UiKit.SetText(_close, Loc.T("ui.close"));
            UiKit.SetText(_langTitle, Loc.T("settings.language"));
            UiKit.SetText(_ko, Loc.T("lang.ko"));
            UiKit.SetText(_en, Loc.T("lang.en"));
            UiKit.SetClass(_ko, "jb--selected", Loc.Current == Language.Korean);
            UiKit.SetClass(_en, "jb--selected", Loc.Current == Language.English);
            UiKit.SetText(_soundTitle, Loc.T("settings.sound"));
            UiKit.SetText(_soundEmpty, Loc.T("settings.soundEmpty"));
            UiKit.SetText(_controlsTitle, Loc.T("settings.controls"));
            UiKit.SetText(_esc, Loc.T("settings.esc"));
            if (_builtControls != Loc.Current)
            {
                BuildControls();
                RebuildSections(); // 등록된 칸도 지금 언어로 다시 만든다
            }

            float t = (now - _openedAt) / 0.25f;
            float sc = t >= 1f ? 1f : Mathf.LerpUnclamped(0.8f, 1f, UiKit.EaseOutBack(t));
            _card.style.scale = new Scale(new Vector2(sc, sc));
            Root.style.opacity = Mathf.Clamp01(t * 2f);
        }

        void BuildControls()
        {
            _builtControls = Loc.Current;
            _controls.Clear();
            Row(new[] { "←", "→" }, "ctl.move");
            Row(new[] { "↑", "Space" }, "ctl.jump");
            Row(new[] { "Shift" }, "ctl.dash");
            Row(new[] { "↓" }, "ctl.crouch");
            Row(new[] { "↓", "+", "↑" }, "ctl.drop");
            Row(new[] { "1~4", "R" }, "ctl.host");
            var stomp = UiKit.Text(Loc.T("ctl.stomp"), "t-small t-muted", _controls);
            stomp.style.marginTop = 2;
        }

        void Row(string[] keys, string what)
        {
            var row = UiKit.El(null, _controls);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 30;
            var keyBox = UiKit.El(null, row);
            keyBox.style.flexDirection = FlexDirection.Row;
            keyBox.style.width = 150;
            foreach (var k in keys)
            {
                if (k == "+") { UiKit.Text("+", "t-small", keyBox).style.marginRight = 6; continue; }
                UiKit.KeyCap(k, keyBox);
            }
            UiKit.Text(Loc.T(what), "t-small", row);
        }
    }
}
