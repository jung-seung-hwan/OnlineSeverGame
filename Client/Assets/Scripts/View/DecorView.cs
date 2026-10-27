using System.Collections.Generic;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 흔들리기만 하는 장식(Stage.Decorations). 서버는 모른다. 클라이언트가 그린 캐릭터 위치로만 판단해
    // 캐릭터가 닿거나 지나가면 그 속도만큼 툭 쳐서 스프링처럼 흔들리다 멈춘다.
    // 매단 장식은 윗변 가운데, 세운 장식은 아랫변 가운데를 축으로 돈다. 그림이 없어 단순 도형으로 그린다.
    public sealed class DecorView : MonoBehaviour
    {
        const float Stiffness = 45f;     // 되돌아오는 힘
        const float Damping = 2.8f;      // 클수록 빨리 멈춘다
        const float PushPerSpeed = 0.35f; // 캐릭터 가로 속도(u/s) 1당 각속도(rad/s)
        const float EnterKick = 1.2f;    // 처음 닿는 순간 최소 각속도
        const float MaxAngle = 0.7f;

        sealed class Item
        {
            public Decoration Def;
            public Transform Pivot;
            public Rect Rest;          // 쉬고 있을 때 덮는 월드 사각형
            public float Angle;        // rad, 반시계 +
            public float AngVel;
            public readonly HashSet<int> Touching = new HashSet<int>();
        }

        readonly List<Item> _items = new List<Item>();
        readonly Dictionary<int, Vector2> _lastPos = new Dictionary<int, Vector2>();
        readonly HashSet<int> _seen = new HashSet<int>();

        public int SwayCount { get; private set; } // 시험 로그용: 닿아서 흔들기 시작한 횟수

        public void Build(StageLayout layout)
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            _items.Clear();
            foreach (var d in layout.Source.Decorations)
            {
                var pivot = new GameObject("Decor " + d.Kind).transform;
                pivot.SetParent(transform, false);
                pivot.localPosition = new Vector3(StageLayout.ToWorld(d.X), StageLayout.ToWorld(d.Y), 0);
                var size = new Vector2(StageLayout.ToWorld(d.Width), StageLayout.ToWorld(d.Height));
                Draw(pivot, d.Kind, size, d.Hanging ? -1f : 1f);
                var b = d.Bounds;
                _items.Add(new Item
                {
                    Def = d,
                    Pivot = pivot,
                    Rest = Rect.MinMaxRect(StageLayout.ToWorld(b.Left), StageLayout.ToWorld(b.Bottom),
                        StageLayout.ToWorld(b.Right), StageLayout.ToWorld(b.Top)),
                });
            }
        }

        public void Step(List<PlayerRender> players, float dt)
        {
            if (dt <= 0f) return;
            _seen.Clear();
            foreach (var p in players)
            {
                _seen.Add(p.Id);
                Vector2 last = _lastPos.TryGetValue(p.Id, out var lp) ? lp : p.Position;
                _lastPos[p.Id] = p.Position;
                float vx = (p.Position.x - last.x) / dt;
                if (Mathf.Abs(p.Position.x - last.x) > 2f) vx = 0f; // 순간이동(라운드 시작)은 무시
                float h = p.Crouching ? 0.72f : 1.2f;
                var body = Rect.MinMaxRect(p.Position.x - 0.4f, p.Position.y, p.Position.x + 0.4f, p.Position.y + h);

                foreach (var it in _items)
                {
                    bool touch = p.Alive && !p.Spectator && body.Overlaps(it.Rest);
                    if (!touch)
                    {
                        it.Touching.Remove(p.Id);
                        continue;
                    }
                    // 매단 것은 오른쪽으로 밀면 아래끝이 오른쪽으로(+), 세운 것은 위끝이 오른쪽으로(-) 간다.
                    float sign = it.Def.Hanging ? 1f : -1f;
                    float dir = Mathf.Abs(vx) > 0.1f ? Mathf.Sign(vx) : (p.Position.x < it.Pivot.localPosition.x ? 1f : -1f);
                    if (it.Touching.Add(p.Id))
                    {
                        it.AngVel += sign * dir * Mathf.Max(EnterKick, Mathf.Abs(vx) * PushPerSpeed);
                        SwayCount++;
                    }
                    else
                    {
                        it.AngVel += sign * vx * PushPerSpeed * dt * 4f; // 안에서 움직이면 계속 밀린다
                    }
                }
            }
            // 사라진 플레이어 정리
            if (_lastPos.Count > _seen.Count)
            {
                var gone = new List<int>();
                foreach (var id in _lastPos.Keys) if (!_seen.Contains(id)) gone.Add(id);
                foreach (var id in gone) _lastPos.Remove(id);
            }

            foreach (var it in _items)
            {
                float acc = -Stiffness * it.Angle - Damping * it.AngVel;
                it.AngVel += acc * dt;
                it.Angle = Mathf.Clamp(it.Angle + it.AngVel * dt, -MaxAngle, MaxAngle);
                it.Pivot.localRotation = Quaternion.Euler(0, 0, it.Angle * Mathf.Rad2Deg);
            }
        }

        // 종류별 단순 도형. up = +1 이면 축에서 위로, -1 이면 아래로 자란다.
        static void Draw(Transform pivot, string kind, Vector2 size, float up)
        {
            const int order = 4;
            switch (kind)
            {
                case "towel":
                {
                    Part(pivot, "Hook", new Color(0.35f, 0.3f, 0.28f), new Vector2(0.12f, 0.12f), 0.06f * up, order + 1);
                    float h = size.y - 0.1f;
                    Part(pivot, "Cloth", new Color(0.93f, 0.9f, 0.84f), new Vector2(size.x, h), (0.1f + h / 2f) * up, order);
                    for (int i = 0; i < 2; i++)
                        Part(pivot, "Stripe", new Color(0.8f, 0.27f, 0.25f), new Vector2(size.x, 0.08f), (0.1f + h * (0.6f + i * 0.18f)) * up, order + 1);
                    break;
                }
                case "ladle":
                {
                    float bowl = Mathf.Min(size.x, 0.36f);
                    Part(pivot, "Handle", new Color(0.62f, 0.64f, 0.68f), new Vector2(0.08f, size.y - bowl * 0.7f), (size.y - bowl * 0.7f) / 2f * up, order);
                    Part(pivot, "Bowl", new Color(0.75f, 0.77f, 0.8f), new Vector2(bowl, bowl * 0.7f), (size.y - bowl * 0.35f) * up, order + 1);
                    break;
                }
                case "garlic":
                {
                    float bulb = size.y * 0.55f;
                    Part(pivot, "String", new Color(0.55f, 0.45f, 0.3f), new Vector2(0.04f, size.y - bulb), (size.y - bulb) / 2f * up, order);
                    Part(pivot, "Bulb", new Color(0.96f, 0.93f, 0.85f), new Vector2(size.x, bulb), (size.y - bulb / 2f) * up, order + 1);
                    Part(pivot, "Shade", new Color(0.86f, 0.8f, 0.72f), new Vector2(size.x * 0.5f, bulb * 0.8f), (size.y - bulb / 2f) * up, order + 2);
                    break;
                }
                case "herb":
                {
                    var green = new Color(0.3f, 0.62f, 0.3f);
                    Part(pivot, "Stem", new Color(0.25f, 0.45f, 0.22f), new Vector2(0.06f, size.y), size.y / 2f * up, order);
                    for (int i = 0; i < 3; i++)
                    {
                        float y = size.y * (0.45f + i * 0.2f);
                        var leaf = Part(pivot, "Leaf", green, new Vector2(size.x * 0.5f, 0.12f), y * up, order + 1);
                        leaf.transform.localPosition += new Vector3((i % 2 == 0 ? 1 : -1) * size.x * 0.22f, 0, 0);
                        leaf.transform.localRotation = Quaternion.Euler(0, 0, i % 2 == 0 ? 25f : -25f);
                    }
                    break;
                }
                default:
                    Part(pivot, "Shape", new Color(0.6f, 0.6f, 0.62f), size, size.y / 2f * up, order);
                    break;
            }
        }

        static SpriteRenderer Part(Transform pivot, string name, Color color, Vector2 size, float centerY, int order)
        {
            var sr = Shapes.Rect(pivot, name, color, size, order);
            sr.transform.localPosition = new Vector3(0, centerY, 0);
            return sr;
        }
    }
}
