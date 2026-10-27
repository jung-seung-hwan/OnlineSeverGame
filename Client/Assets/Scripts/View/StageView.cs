using OnlineSeverGame.Client.Model;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 주방 맵(ArtSprites): 배경, 도마 바닥, 선반 발판, 가장 높은 발판은 매단 냄비 뚜껑.
    // 그림은 Stage.Default 좌표로 렌더했고, 발판 그림은 각 발판 윗면 가운데에 놓는다.
    public sealed class StageView : MonoBehaviour
    {
        static readonly Color Backdrop = new Color(0.08f, 0.09f, 0.11f);   // 배경 그림 밖(넓은 화면)
        static readonly Color OutsideWall = new Color(0f, 0f, 0f, 0.45f);   // 좌우 벽 바깥을 어둡게

        public void Build(StageLayout layout)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            Shapes.Rect(transform, "Backdrop", Backdrop, new Vector2(layout.Width + 40f, layout.Height + 40f), -101)
                .transform.localPosition = new Vector3(layout.Width / 2f, layout.Height / 2f, 0);
            Place("kitchen_bg", ArtSprites.BackgroundRect, Vector2.zero, -100);
            Place("floor_board", ArtSprites.FloorRect, Vector2.zero, -10);

            float highest = float.MinValue;
            for (int i = 1; i < layout.Solids.Count; i++) highest = Mathf.Max(highest, layout.Solids[i].TopY);
            for (int i = 1; i < layout.Solids.Count; i++)
            {
                var s = layout.Solids[i];
                bool lid = s.TopY >= highest && layout.Solids.Count > 2;
                Place(lid ? "pot_lid" : "shelf", lid ? ArtSprites.LidRect : ArtSprites.ShelfRect,
                    new Vector2(s.CenterX, s.TopY), -9).name = "Platform" + i;
            }

            // 벽 바깥(x < 0, x > Width)은 그림이 이어지므로 어둡게 덮어 벽 위치를 보여 준다.
            for (int side = -1; side <= 1; side += 2)
            {
                var wall = Shapes.Rect(transform, side < 0 ? "WallL" : "WallR", OutsideWall, new Vector2(10f, layout.Height + 40f), -8);
                wall.transform.localPosition = new Vector3(layout.Width / 2f + side * (layout.Width / 2f + 5f), layout.Height / 2f, 0);
            }
        }

        // rect 는 그림이 덮는 사각형(at 기준). at 에 피벗을 놓는다.
        GameObject Place(string name, Rect rect, Vector2 at, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(at.x, at.y, 0);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ArtSprites.Map(name, rect);
            sr.sortingOrder = order;
            return go;
        }
    }
}
