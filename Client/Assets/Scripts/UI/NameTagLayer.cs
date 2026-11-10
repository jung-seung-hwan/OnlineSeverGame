using System.Collections.Generic;
using OnlineSeverGame.Client.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace OnlineSeverGame.Client.UI
{
    // 캐릭터 머리 위 이름표(플레이어 색, 어두운 외곽선). 내 캐릭터는 이름 위에 내 색 삼각형, 이름 아래에 작은 대시 게이지
    // (예전과 같은 자리: 차오르는 동안 회색, 다 차면 하늘색). 탈락한 사람 이름은 흐리게.
    public sealed class NameTagLayer : UiView
    {
        sealed class Tag
        {
            public Label Name;
            public VisualElement Arrow;
            public VisualElement Gauge, Fill;
            public Color ArrowColor;
        }

        readonly List<Tag> _tags = new List<Tag>();

        public NameTagLayer(GameUI ui) : base(ui)
        {
            Root = UiKit.El("layer");
            Root.name = "name-tags";
            Root.pickingMode = PickingMode.Ignore;
            EnterOffset = 0f;
        }

        Tag Make()
        {
            var t = new Tag();
            t.Name = UiKit.Text("", "name-tag", Root);
            t.Arrow = UiKit.El("me-arrow", Root);
            t.Arrow.pickingMode = PickingMode.Ignore;
            t.Arrow.generateVisualContent += mgc =>
            {
                var p = mgc.painter2D;
                p.fillColor = t.ArrowColor;
                p.strokeColor = new Color(0.08f, 0.06f, 0.11f);
                p.lineWidth = 1.5f;
                p.BeginPath();
                p.MoveTo(new Vector2(0, 0));
                p.LineTo(new Vector2(14, 0));
                p.LineTo(new Vector2(7, 10));
                p.ClosePath();
                p.Fill();
                p.Stroke();
            };
            t.Gauge = UiKit.El("gauge", Root);
            t.Gauge.pickingMode = PickingMode.Ignore;
            t.Gauge.style.position = Position.Absolute;
            t.Gauge.style.width = 58;
            t.Gauge.style.height = 9;
            t.Gauge.style.marginLeft = -29;
            t.Gauge.style.paddingLeft = 1; t.Gauge.style.paddingRight = 1; t.Gauge.style.paddingTop = 1; t.Gauge.style.paddingBottom = 1;
            t.Fill = UiKit.El("gauge-fill", t.Gauge);
            _tags.Add(t);
            return t;
        }

        public void Refresh(IGameSession session)
        {
            var cam = Ui.Game.WorldCamera;
            var panel = Root.panel;
            int n = 0;
            if (cam != null && panel != null && session != null)
            {
                float headY = Ui.Game.Layout.PlayerSize.y + 0.25f;
                foreach (var p in session.World.Players)
                {
                    var t = n < _tags.Count ? _tags[n] : Make();
                    n++;
                    var pos = RuntimePanelUtils.CameraTransformWorldToPanel(panel, new Vector3(p.Position.x, p.Position.y + headY, 0f), cam);
                    var c = PlayerColors.Get(p.ColorIndex);
                    UiKit.Show(t.Name, true);
                    UiKit.SetText(t.Name, p.Name);
                    t.Name.style.left = pos.x;
                    t.Name.style.top = pos.y - 24f;
                    t.Name.style.color = p.Alive ? Color.Lerp(c, Color.white, 0.5f) : new Color(1f, 1f, 1f, 0.3f);

                    bool me = p.IsLocal;
                    UiKit.Show(t.Arrow, me && p.Alive);
                    if (me && p.Alive)
                    {
                        if (t.ArrowColor != c) { t.ArrowColor = c; t.Arrow.MarkDirtyRepaint(); }
                        t.Arrow.style.left = pos.x;
                        t.Arrow.style.top = pos.y - 36f;
                    }
                    UiKit.Show(t.Gauge, me && p.Alive);
                    if (me && p.Alive)
                    {
                        float fill = 1f - Mathf.Clamp01(p.DashCooldown);
                        t.Gauge.style.left = pos.x;
                        t.Gauge.style.top = pos.y + 1f;
                        t.Fill.style.width = new Length(fill * 100f, LengthUnit.Percent);
                        UiKit.SetClass(t.Fill, "gauge-fill--ready", fill >= 1f);
                    }
                }
            }
            for (int i = n; i < _tags.Count; i++)
            {
                UiKit.Show(_tags[i].Name, false);
                UiKit.Show(_tags[i].Arrow, false);
                UiKit.Show(_tags[i].Gauge, false);
            }
        }
    }
}
