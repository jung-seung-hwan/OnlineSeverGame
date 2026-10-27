using OnlineSeverGame.Client.Model;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 밀리는 오브젝트. 그림이 없어 단순 도형: 어두운 테두리 + 몸통 + 밝은 윗면 띠. 색은 맵마다
    // (주방 버터 블록, 겨울 눈덩이, 창고 화물 상자, 디저트 마카롱). 위치는 아래쪽 가운데(서버 PushableState 와 같음).
    // 창고에서 개수 제한으로 사라지기 직전인 상자는 깜빡인다.
    public sealed class BoxView : MonoBehaviour
    {
        SpriteRenderer _edge;
        SpriteRenderer _body;
        SpriteRenderer _top;
        SpriteRenderer _art;   // 맵 그림(눈덩이·상자·마카롱)이 있으면 도형 대신
        int _stageId;
        bool _cracked;
        Vector2 _size;

        // 맵 id 순서: (테두리, 몸통, 윗면)
        static readonly Color[,] Colors =
        {
            { new Color(0.45f, 0.33f, 0.12f), new Color(0.98f, 0.84f, 0.42f), new Color(1f, 0.95f, 0.72f) },   // 1 버터
            { new Color(0.55f, 0.62f, 0.72f), new Color(0.92f, 0.95f, 1f), new Color(1f, 1f, 1f) },             // 2 눈덩이
            { new Color(0.30f, 0.19f, 0.10f), new Color(0.66f, 0.46f, 0.26f), new Color(0.85f, 0.68f, 0.45f) }, // 3 상자
            { new Color(0.55f, 0.30f, 0.38f), new Color(0.96f, 0.66f, 0.76f), new Color(1f, 0.92f, 0.86f) },    // 4 마카롱
        };

        public static BoxView Create(Transform parent, int stageId = 1)
        {
            var go = new GameObject("Box");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<BoxView>();
            int c = Mathf.Clamp(stageId - 1, 0, Colors.GetLength(0) - 1);
            v._edge = Shapes.Rect(go.transform, "Edge", Colors[c, 0], Vector2.one, 6);
            v._body = Shapes.Rect(go.transform, "Body", Colors[c, 1], Vector2.one, 7);
            v._top = Shapes.Rect(go.transform, "Top", Colors[c, 2], Vector2.one, 8);
            v._stageId = stageId;
            var art = ArtSprites.Box(stageId, false);
            if (art != null)
            {
                var a = new GameObject("Art");
                a.transform.SetParent(go.transform, false);
                v._art = a.AddComponent<SpriteRenderer>();
                v._art.sprite = art;
                v._art.sortingOrder = 7;
                v._edge.enabled = v._body.enabled = v._top.enabled = false;
            }
            return v;
        }

        public void Apply(BoxRender b)
        {
            transform.localPosition = new Vector3(b.Position.x, b.Position.y, 0);
            float alpha = b.Crumbling ? 0.35f + 0.65f * Mathf.PingPong(Time.time * 8f, 1f) : 1f;
            if (_art != null)
            {
                if (b.Crumbling != _cracked)
                {
                    _cracked = b.Crumbling;
                    _art.sprite = ArtSprites.Box(_stageId, _cracked) ?? _art.sprite;
                }
                _art.color = new Color(1f, 1f, 1f, alpha);
                return;
            }
            if (!Mathf.Approximately(_body.color.a, alpha))
            {
                foreach (var sr in new[] { _edge, _body, _top })
                    sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, alpha);
            }
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
