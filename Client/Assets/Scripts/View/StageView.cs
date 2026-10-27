using OnlineSeverGame.Client.Model;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    public sealed class StageView : MonoBehaviour
    {
        static readonly Color Background = new Color(0.12f, 0.13f, 0.17f);
        static readonly Color Ground = new Color(0.35f, 0.30f, 0.27f);
        static readonly Color Platform = new Color(0.50f, 0.45f, 0.40f);
        static readonly Color Wall = new Color(0.20f, 0.21f, 0.26f);

        public void Build(StageLayout layout)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            // 배경과 좌우 벽 표시
            Shapes.Rect(transform, "Background", Background, new Vector2(layout.Width + 40f, layout.Height + 40f), -100)
                .transform.localPosition = new Vector3(layout.Width / 2f, layout.Height / 2f, 0);
            for (int side = -1; side <= 1; side += 2)
            {
                var wall = Shapes.Rect(transform, side < 0 ? "WallL" : "WallR", Wall, new Vector2(10f, layout.Height + 40f), -90);
                wall.transform.localPosition = new Vector3(layout.Width / 2f + side * (layout.Width / 2f + 5f), layout.Height / 2f, 0);
            }

            for (int i = 0; i < layout.Solids.Count; i++)
            {
                var s = layout.Solids[i];
                var color = i == 0 ? Ground : Platform;
                var sr = Shapes.Rect(transform, i == 0 ? "Ground" : "Platform" + i, color, new Vector2(s.Width, s.Height), -10);
                sr.transform.localPosition = new Vector3(s.CenterX, s.TopY - s.Height / 2f, 0);
            }
        }
    }
}
