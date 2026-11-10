using OnlineSeverGame.Client.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace OnlineSeverGame.Client.UI
{
    // 접속 화면과 접속 중 화면. 위에 움직이는 타이틀 로고, 아래에 카드 하나(내용만 바뀐다). 로고 크기와 위치는
    // 접속 카드 높이로만 정해서 접속 중으로 넘어가도 로고가 움직이지 않는다(docs/work/title.md 와 같은 규칙).
    public sealed class ConnectView : UiView
    {
        const float CardWidth = 500f;
        const float CardHeightForLayout = 300f; // 접속 카드(경고 두 줄까지)

        readonly TitleLogoElement _logo;
        readonly VisualElement _connectBox, _connectingBox, _dots;
        readonly Label _nameLabel, _serverLabel, _status, _connectingText;
        readonly TextField _name, _server;
        readonly Button _join, _preview, _settingsBtn, _langBtn, _cancel;
        bool _fieldsLoaded;

        public ConnectView(GameUI ui) : base(ui)
        {
            Root = UiKit.El("screen");
            Root.name = "screen-connect";
            Root.pickingMode = PickingMode.Ignore;
            var column = UiKit.El(null, Root);
            column.pickingMode = PickingMode.Ignore;
            column.style.position = Position.Absolute;
            column.style.left = 0; column.style.right = 0; column.style.top = 0; column.style.bottom = 0;
            column.style.alignItems = Align.Center;
            column.style.justifyContent = Justify.Center;

            _logo = new TitleLogoElement(animated: true);
            _logo.style.flexShrink = 0;
            column.Add(_logo);

            var card = UiKit.El("panel", column);
            card.style.width = CardWidth;
            card.style.marginTop = 14;
            card.style.flexShrink = 0;

            // ---- 접속 카드
            _connectBox = UiKit.El(null, card);
            var row = UiKit.El("field-row", _connectBox);
            _nameLabel = UiKit.Text("", "field-label", row);
            _name = new TextField { name = "field-name", maxLength = 16 };
            _name.AddToClassList("field");
            _name.RegisterValueChangedCallback(e => Ui.Game.PlayerName = e.newValue);
            row.Add(_name);
            row = UiKit.El("field-row", _connectBox);
            _serverLabel = UiKit.Text("", "field-label", row);
            _server = new TextField { name = "field-server", maxLength = 64 };
            _server.AddToClassList("field");
            _server.RegisterValueChangedCallback(e => Ui.Game.ServerAddress = e.newValue);
            row.Add(_server);

            var buttons = UiKit.El(null, _connectBox);
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 6;
            _join = UiKit.Button("btn-join", "", TryJoin, "jb--primary jb--big", buttons);
            _join.style.flexGrow = 1;
            _join.style.flexBasis = 0;
            _join.style.marginRight = 10;
            _preview = UiKit.Button("btn-preview", "", () => Ui.Game.StartPreview(), "jb--blue jb--big", buttons);
            _preview.style.flexGrow = 1.3f;
            _preview.style.flexBasis = 0;
            _preview.style.fontSize = 22;

            var bottom = UiKit.El(null, _connectBox);
            bottom.style.flexDirection = FlexDirection.Row;
            bottom.style.marginTop = 12;
            bottom.style.justifyContent = Justify.SpaceBetween;
            _settingsBtn = UiKit.Button("btn-settings-connect", "", Ui.OpenSettings, "jb--small jb--grape", bottom);
            _langBtn = UiKit.Button("btn-lang-connect", "", Loc.Toggle, "jb--small", bottom);

            _status = UiKit.Text("", "warn-box", _connectBox);
            _status.style.marginTop = 12;

            // ---- 접속 중 카드
            _connectingBox = UiKit.El(null, card);
            _connectingText = UiKit.Text("", "t-body t-center t-wrap", _connectingBox);
            _dots = UiKit.El(null, _connectingBox);
            _dots.style.flexDirection = FlexDirection.Row;
            _dots.style.justifyContent = Justify.Center;
            _dots.style.height = 40;
            _dots.style.marginTop = 6;
            int[] dotColors = { 2, 1, 3, 4 };
            foreach (int c in dotColors)
            {
                var d = UiKit.El("hud-dot", _dots);
                d.style.width = 18; d.style.height = 18;
                d.style.marginLeft = 5; d.style.marginRight = 5;
                d.style.backgroundColor = PlayerColors.Get(c);
            }
            _cancel = UiKit.Button("btn-cancel", "", () => Ui.Game.Disconnect(Loc.T("connect.cancelled")), null, _connectingBox);
        }

        public void TryJoin()
        {
            if (string.IsNullOrWhiteSpace(Ui.Game.PlayerName)) return;
            Ui.Game.Connect();
        }

        public void BlurFields()
        {
            _name.Blur();
            _server.Blur();
            _fieldsLoaded = false; // 돌아오면 저장값(다시 접속하면 바뀔 수 있음)을 다시 읽는다
        }

        public void Refresh(ClientScreen screen, float now)
        {
            bool connecting = screen == ClientScreen.Connecting;
            UiKit.Show(_connectBox, !connecting);
            UiKit.Show(_connectingBox, connecting);

            var size = Ui.PanelSize;
            var unit = TitleLogo.Size(1f, animated: true);
            float ppu = Mathf.Clamp(Mathf.Min(size.x * 0.55f / unit.x, (size.y - CardHeightForLayout - 50f) / unit.y), 2.5f, 9f);
            _logo.SetPpu(ppu);
            _logo.Tick(Time.time);

            if (!_fieldsLoaded)
            {
                _fieldsLoaded = true;
                _name.SetValueWithoutNotify(Ui.Game.PlayerName ?? "");
                _server.SetValueWithoutNotify(Ui.Game.ServerAddress ?? "");
            }

            UiKit.SetText(_nameLabel, Loc.T("connect.name"));
            UiKit.SetText(_serverLabel, Loc.T("connect.server"));
            UiKit.SetText(_join, Loc.T("connect.join"));
            UiKit.SetText(_preview, Loc.T("connect.preview"));
            UiKit.SetText(_settingsBtn, Loc.T("ui.settings"));
            UiKit.SetText(_langBtn, Loc.T("lang.label") + " · " + Loc.T("lang.button"));
            UiKit.SetText(_cancel, Loc.T("connect.cancel"));
            _join.SetEnabled(!string.IsNullOrWhiteSpace(Ui.Game.PlayerName));

            string status = Ui.Game.LastStatus;
            UiKit.Show(_status, !string.IsNullOrEmpty(status));
            if (!string.IsNullOrEmpty(status)) UiKit.SetText(_status, status);

            if (connecting)
            {
                UiKit.SetText(_connectingText, Loc.F("connect.connecting", Ui.Game.ServerAddress));
                // 젤리 점 넷이 차례로 통통 뛴다
                int i = 0;
                foreach (var d in _dots.Children())
                {
                    float ph = Mathf.Repeat(Time.time * 1.6f - i * 0.18f, 1f);
                    float jump = ph < 0.4f ? Mathf.Sin(ph / 0.4f * Mathf.PI) : 0f;
                    d.style.translate = new Translate(0, 10f - jump * 18f);
                    d.style.scale = new Scale(new Vector2(1f + (jump < 0.05f ? 0.15f : 0f), 1f - (jump < 0.05f ? 0.15f : 0f)));
                    i++;
                }
            }
        }
    }
}
