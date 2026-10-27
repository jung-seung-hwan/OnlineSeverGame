using OnlineSeverGame.Client.Model;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 플레이어 사각형. 눈(방향 표시), 대시 잔상, 내 캐릭터 테두리를 가진다.
    public sealed class PlayerView : MonoBehaviour
    {
        SpriteRenderer _outline;
        SpriteRenderer _body;
        SpriteRenderer _eye;
        SpriteRenderer _trail;
        Vector2 _size;

        public PlayerRender State { get; private set; }

        public static PlayerView Create(Transform parent, Vector2 size)
        {
            var go = new GameObject("Player");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<PlayerView>();
            v._size = size;
            v._trail = Shapes.Rect(go.transform, "Trail", Color.white, size, 9);
            v._outline = Shapes.Rect(go.transform, "Outline", Color.white, size + new Vector2(0.16f, 0.16f), 10);
            v._body = Shapes.Rect(go.transform, "Body", Color.white, size, 11);
            v._eye = Shapes.Rect(go.transform, "Eye", new Color(0.1f, 0.1f, 0.1f), new Vector2(0.14f, 0.22f), 12);
            return v;
        }

        public void Apply(PlayerRender s)
        {
            State = s;
            transform.localPosition = new Vector3(s.Position.x, s.Position.y, 0);

            var color = PlayerColors.Get(s.ColorIndex);
            if (!s.Alive) color = Color.Lerp(color, Color.gray, 0.7f) * new Color(1, 1, 1, 0.35f);
            _body.color = color;
            _body.transform.localPosition = new Vector3(0, _size.y / 2f, 0);

            _outline.enabled = s.IsLocal && s.Alive;
            _outline.color = Color.white;
            _outline.transform.localPosition = _body.transform.localPosition;

            int facing = s.Facing < 0 ? -1 : 1;
            _eye.enabled = s.Alive;
            _eye.transform.localPosition = new Vector3(facing * _size.x * 0.22f, _size.y * 0.7f, 0);

            _trail.enabled = s.Alive && s.Dashing;
            _trail.color = new Color(color.r, color.g, color.b, 0.35f);
            _trail.transform.localPosition = new Vector3(-facing * _size.x * 0.6f, _size.y / 2f, 0);
        }
    }
}
