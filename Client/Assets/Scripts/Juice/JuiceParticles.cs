using UnityEngine;

namespace OnlineSeverGame.Client.Juice
{
    // 스프라이트 파티클 풀(파티클 시스템·셰이더 없이 SpriteRenderer 만 쓴다: 빌드에서 셰이더가 빠질 걱정이 없다).
    // 시간은 Time.deltaTime(배율 적용)이라 대시 충돌 멈춤·슬로모션 때 파티클도 같이 멈추고 느려진다.
    // 그림은 실행 중에 만든 원·고리·별·사각형.
    public sealed class JuiceParticles : MonoBehaviour
    {
        const int Capacity = 420;

        sealed class P
        {
            public SpriteRenderer Sr;
            public bool Live;
            public Vector2 Pos, Vel;
            public float Age, Life, Gravity, Drag;
            public float Size0, Size1;
            public float Rot, RotVel;
            public Color Color;
            public bool Bounce;
            public float FloorY;
            public Vector2 Stretch;  // 크기 배수(가로, 세로)
            public bool FadeIn;
        }

        readonly P[] _pool = new P[Capacity];
        int _next;
        public int Emitted;

        static Sprite _circle, _ring, _star;

        public static Sprite Circle => _circle != null ? _circle : (_circle = MakeSprite(64, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            return Mathf.Clamp01((1f - d) * 10f);
        }));

        public static Sprite Ring => _ring != null ? _ring : (_ring = MakeSprite(128, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            return Mathf.Clamp01((1f - d) * 18f) * Mathf.Clamp01((d - 0.78f) * 18f);
        }));

        public static Sprite Star => _star != null ? _star : (_star = MakeSprite(64, (x, y) =>
        {
            // 둥근 끝 다섯 갈래 별
            float a = Mathf.Atan2(y, x) + Mathf.PI / 2f;
            float r = Mathf.Sqrt(x * x + y * y);
            float k = Mathf.Cos(5f * a) * 0.5f + 0.5f;
            float edge = Mathf.Lerp(0.45f, 1f, Mathf.Pow(k, 2.2f));
            return Mathf.Clamp01((edge - r) * 12f);
        }));

        delegate float Shape(float x, float y);

        static Sprite MakeSprite(int size, Shape shape)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    float x = (i + 0.5f) / size * 2f - 1f, y = (j + 0.5f) / size * 2f - 1f;
                    px[j * size + i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(shape(x, y)) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size); // 1 x 1 월드 단위
        }

        void Awake()
        {
            for (int i = 0; i < Capacity; i++)
            {
                var go = new GameObject("P");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.enabled = false;
                sr.sortingOrder = 13;
                _pool[i] = new P { Sr = sr };
            }
        }

        P Take()
        {
            // 가장 오래된 것부터 덮어쓴다(풀이 차면 오래된 파티클이 먼저 사라진다)
            for (int k = 0; k < Capacity; k++)
            {
                var p = _pool[(_next + k) % Capacity];
                if (!p.Live) { _next = (_next + k + 1) % Capacity; return p; }
            }
            var q = _pool[_next];
            _next = (_next + 1) % Capacity;
            return q;
        }

        P Emit(Sprite sprite, Vector2 pos, Vector2 vel, float life, float size0, float size1, Color color, int order = 13)
        {
            var p = Take();
            p.Live = true;
            p.Pos = pos; p.Vel = vel;
            p.Age = 0f; p.Life = life;
            p.Gravity = 0f; p.Drag = 0f;
            p.Size0 = size0; p.Size1 = size1;
            p.Rot = 0f; p.RotVel = 0f;
            p.Color = color;
            p.Bounce = false;
            p.Stretch = Vector2.one;
            p.FadeIn = false;
            p.Sr.sprite = sprite;
            p.Sr.sortingOrder = order;
            p.Sr.enabled = true;
            Emitted++;
            Place(p);
            return p;
        }

        // ---------------------------------------------------------------- 효과

        static readonly Color DustColor = new Color(0.92f, 0.9f, 0.86f, 0.75f);

        // 착지·점프·대시 먼지. dir: 0 = 양옆으로 퍼짐, ±1 = 그쪽으로 밀려남(대시 뒤쪽)
        public void Dust(Vector2 feet, int count, float strength, float dir = 0f)
        {
            for (int i = 0; i < count; i++)
            {
                float side = dir != 0f ? dir : (i % 2 == 0 ? -1f : 1f);
                var vel = new Vector2(side * Random.Range(1.2f, 3.2f) * strength, Random.Range(0.3f, 1.3f) * strength);
                var p = Emit(Circle, feet + new Vector2(Random.Range(-0.2f, 0.2f), 0.05f), vel, Random.Range(0.28f, 0.45f),
                    Random.Range(0.18f, 0.3f) * strength, Random.Range(0.42f, 0.6f) * strength, DustColor, 13);
                p.Drag = 5f;
                p.Gravity = -1.5f; // 살짝 떠오른다
            }
        }

        // 위험물이 꽂힐 때 파편 + 먼지
        public void Shards(Vector2 at, Color color, int count, float spread = 1f)
        {
            for (int i = 0; i < count; i++)
            {
                float a = Random.Range(20f, 160f) * Mathf.Deg2Rad;
                float sp = Random.Range(3f, 7.5f) * spread;
                var p = Emit(View.Shapes.White, at, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp, Random.Range(0.35f, 0.6f),
                    Random.Range(0.07f, 0.13f), 0.04f, color, 16);
                p.Gravity = 26f;
                p.RotVel = Random.Range(-720f, 720f);
                p.Stretch = new Vector2(1f, Random.Range(1.5f, 2.6f));
                p.Bounce = true;
                p.FloorY = at.y;
            }
            Dust(at, 4, 0.8f);
        }

        // 젤리가 터질 때 같은 색 방울. big = 라운드를 끝낸 마지막 사망
        public void Droplets(Vector2 center, Color color, bool big)
        {
            int count = big ? 26 : 16;
            color.a = 0.95f;
            for (int i = 0; i < count; i++)
            {
                float a = Random.Range(15f, 165f) * Mathf.Deg2Rad;
                float sp = Random.Range(2.5f, big ? 9f : 7f);
                var p = Emit(Circle, center + new Vector2(0f, 0.35f), new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp,
                    Random.Range(0.6f, 1.0f), Random.Range(0.12f, 0.28f), 0.06f, color, 13);
                p.Gravity = 22f;
                p.Bounce = true;
                p.FloorY = center.y + 0.04f;
            }
            var light = Color.Lerp(color, Color.white, 0.55f);
            light.a = 0.8f;
            Emit(Ring, center + new Vector2(0f, 0.4f), Vector2.zero, 0.32f, 0.4f, big ? 2.6f : 1.8f, light, 13);
        }

        // 밟기·대시 충돌 충격: 고리 + 작은 별
        public void Impact(Vector2 at, Color color, float size, int stars)
        {
            color.a = 0.85f;
            Emit(Ring, at, Vector2.zero, 0.22f, 0.2f, size, color, 14);
            for (int i = 0; i < stars; i++)
            {
                float a = (i + Random.value * 0.6f) / stars * Mathf.PI * 2f;
                var p = Emit(Star, at, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(3f, 5f), 0.3f, 0.26f, 0.08f,
                    new Color(1f, 0.95f, 0.55f, 1f), 14);
                p.Drag = 6f;
                p.RotVel = Random.Range(-400f, 400f);
            }
        }

        // 기절 면역 반짝임(한 개)
        public void Sparkle(Vector2 at)
        {
            var p = Emit(Star, at, new Vector2(0f, 0.6f), 0.35f, 0.16f, 0.02f, new Color(1f, 1f, 1f, 0.85f), 14);
            p.RotVel = 300f;
        }

        // ---------------------------------------------------------------- 진행

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            foreach (var p in _pool)
            {
                if (!p.Live) continue;
                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    p.Live = false;
                    p.Sr.enabled = false;
                    continue;
                }
                p.Vel.y -= p.Gravity * dt;
                if (p.Drag > 0f) p.Vel *= Mathf.Exp(-p.Drag * dt);
                p.Pos += p.Vel * dt;
                if (p.Bounce && p.Pos.y < p.FloorY && p.Vel.y < 0f)
                {
                    p.Pos.y = p.FloorY;
                    p.Vel.y = -p.Vel.y * 0.28f;
                    p.Vel.x *= 0.55f;
                    p.RotVel *= 0.5f;
                    p.Stretch = new Vector2(p.Stretch.x * 1.5f, p.Stretch.y * 0.6f); // 바닥에 철퍽
                }
                p.Rot += p.RotVel * dt;
                Place(p);
            }
        }

        static void Place(P p)
        {
            float t = p.Age / p.Life;
            float size = Mathf.Lerp(p.Size0, p.Size1, t);
            var tr = p.Sr.transform;
            tr.localPosition = new Vector3(p.Pos.x, p.Pos.y, 0f);
            tr.localRotation = Quaternion.Euler(0f, 0f, p.Rot);
            tr.localScale = new Vector3(size * p.Stretch.x, size * p.Stretch.y, 1f);
            var c = p.Color;
            c.a *= 1f - t * t;
            p.Sr.color = c;
        }

        public void Clear()
        {
            foreach (var p in _pool)
            {
                p.Live = false;
                p.Sr.enabled = false;
            }
        }
    }
}
