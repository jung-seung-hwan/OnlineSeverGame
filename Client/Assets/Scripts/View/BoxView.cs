using OnlineSeverGame.Client.Model;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 밀리는 오브젝트(버터 블록). 그림이 없어 단순 도형: 어두운 테두리 + 노란 몸통 + 밝은 윗면 띠.
    // 위치는 아래쪽 가운데(서버 PushableState 와 같음).
    public sealed class BoxView : MonoBehaviour
    {
        SpriteRenderer _edge;
        SpriteRenderer _body;
        SpriteRenderer _top;
        Vector2 _size;

        public static BoxView Create(Transform parent)
        {
            var go = new GameObject("Box");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<BoxView>();
            v._edge = Shapes.Rect(go.transform, "Edge", new Color(0.45f, 0.33f, 0.12f), Vector2.one, 6);
            v._body = Shapes.Rect(go.transform, "Body", new Color(0.98f, 0.84f, 0.42f), Vector2.one, 7);
            v._top = Shapes.Rect(go.transform, "Top", new Color(1f, 0.95f, 0.72f), Vector2.one, 8);
            return v;
        }

        public void Apply(BoxRender b)
        {
            transform.localPosition = new Vector3(b.Position.x, b.Position.y, 0);
            if (b.Size == _size) return;
            _size = b.Size;
            const float edge = 0.06f;
            Shapes.Resize(_edge, b.Size);
            _edge.transform.localPosition = new Vector3(0, b.Size.y / 2f, 0);
            Shapes.Resize(_body, b.Size - new Vector2(edge * 2f, edge * 2f));
            _body.transform.localPosition = new Vector3(0, b.Size.y / 2f, 0);
            Shapes.Resize(_top, new Vector2(b.Size.x - edge * 2f, 0.14f));
            _top.transform.localPosition = new Vector3(0, b.Size.y - edge - 0.07f, 0);
        }
    }
}
