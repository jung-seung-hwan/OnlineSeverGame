using System;
using System.Collections.Generic;
using OnlineSeverGame.Client.Model;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 맵 배경 깊이층과 움직이는 배경(docs/work/map-visual.md). 그림과 배치는 Art/blender/build_depth.py 가 만든
    // Resources/Art/<맵 폴더>/layers.json 을 그대로 읽는다. 파일이 없으면 TryBuild 가 false 를 돌려주고
    // StageView 가 예전 한 장짜리 배경(<맵>_bg)을 그린다.
    //
    // 층: far(먼 배경) / mid(중간 소품) / attach(발판에 붙은 사슬·레일·창, 시차 없음) / front(벽 바깥 흐린 실루엣).
    // 시차: 가상 카메라 오프셋 × depth 만큼 층을 옮긴다. 가상 카메라 = 실제 카메라가 쉬는 자리에서 벗어난 만큼
    // (흔들림 등) + 내 캐릭터의 가로 위치(화면 가운데에서 끝까지 갈 때 FocusRange). 카메라는 움직이지 않으므로
    // 발판·캐릭터·위험물은 그대로이고 배경만 살짝 움직여 깊이가 보인다. 세로는 카메라 흔들림만 따른다
    // (바닥에 선 배경 소품이 뜨지 않게).
    // 움직이는 배경(fx)은 모두 발판보다 뒤(정렬 순서 -89 이하)에 그리고, 알파를 낮게 잡아 위험물·예고보다
    // 눈에 띄지 않게 한다.
    public sealed class BackdropView : MonoBehaviour
    {
        public const float FocusRange = 0.8f;   // 내 캐릭터가 화면 끝에 있을 때 가상 카메라 오프셋(유닛)
        const float MaxCameraOffset = 0.6f;

        [Serializable]
        sealed class LayerDef
        {
            public string name;
            public string file;
            public float[] rect;
            public float depth;
            public int order;
        }

        [Serializable]
        sealed class FxDef
        {
            public string kind, layer, sprite, color, glow;
            public string[] colors;
            public float[] at, area, points, speed, alpha, pivot, glowAt;
            public float size, rise, period, flicker, scale, amp;
            public int count;
        }

        [Serializable]
        sealed class Meta
        {
            public LayerDef[] layers;
            public FxDef[] fx;
        }

        abstract class Fx
        {
            public abstract void Step(float t, float dt);
        }

        sealed class Layer
        {
            public Transform Root;
            public float Depth;
            public int Order;
        }

        static Vector2 _focus;
        static float _focusAt = -100f;

        // 내 캐릭터 위치(살아 있을 때 매 프레임). 0.5초 동안 안 오면 가운데로 돌아간다.
        public static void SetFocus(Vector2 position)
        {
            _focus = position;
            _focusAt = Time.unscaledTime;
        }

        readonly List<Layer> _layers = new List<Layer>();
        readonly List<Fx> _fx = new List<Fx>();
        float _width, _height, _smooth, _time;

        public int LayerCount => _layers.Count;
        public int FxCount => _fx.Count;

        public static bool TryBuild(Transform parent, string folder, StageLayout layout)
        {
            var ta = Resources.Load<TextAsset>("Art/" + folder + "/layers");
            if (ta == null) return false;
            var meta = JsonUtility.FromJson<Meta>(ta.text);
            if (meta == null || meta.layers == null || meta.layers.Length == 0) return false;
            if (ArtSprites.MapPiece(folder, meta.layers[0].file) == null) return false;
            var go = new GameObject("Backdrop");
            go.transform.SetParent(parent, false);
            go.AddComponent<BackdropView>().Init(folder, meta, layout);
            return true;
        }

        void Init(string folder, Meta meta, StageLayout layout)
        {
            _width = layout.Width;
            _height = layout.Height;
            foreach (var l in meta.layers)
            {
                var sprite = ArtSprites.MapPiece(folder, l.file);
                if (sprite == null) continue;
                var root = new GameObject("Layer " + l.name).transform;
                root.SetParent(transform, false);
                var go = new GameObject(l.file);
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(l.rect[0], l.rect[1], 0);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = l.order;
                _layers.Add(new Layer { Root = root, Depth = l.depth, Order = l.order });
            }
            if (meta.fx == null) return;
            var rnd = new System.Random(folder.GetHashCode() & 0x7fffffff);
            foreach (var f in meta.fx)
            {
                Layer layer = Find(f.layer);
                if (layer == null) continue;
                Fx fx = Create(f, layer, rnd);
                if (fx != null) _fx.Add(fx);
            }
        }

        Layer Find(string name)
        {
            foreach (var l in _layers)
                if (l.Root.name == "Layer " + name) return l;
            return null;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            _time += dt;

            float target = 0f;
            if (Time.unscaledTime - _focusAt < 0.5f)
                target = Mathf.Clamp((_focus.x - _width / 2f) / (_width / 2f), -1f, 1f);
            _smooth = Mathf.Lerp(_smooth, target, 1f - Mathf.Exp(-2.5f * dt));
            Vector2 cam = Vector2.zero;
            var c = Camera.main;
            if (c != null)
            {
                cam = (Vector2)c.transform.position - new Vector2(_width / 2f, _height / 2f - 0.5f);
                cam = Vector2.ClampMagnitude(cam, MaxCameraOffset);
            }
            Vector2 virt = new Vector2(_smooth * FocusRange, 0f) + cam;
            foreach (var l in _layers)
                l.Root.localPosition = new Vector3(virt.x * l.Depth, virt.y * l.Depth, 0f);

            foreach (var f in _fx) f.Step(_time, dt);
        }

        // ---------------------------------------------------------------- fx

        static Color Hex(string hex, Color fallback)
        {
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var col) ? col : fallback;
        }

        static float A(FxDef f, int i, float d) => f.alpha != null && f.alpha.Length > i ? f.alpha[i] : d;

        static SpriteRenderer Make(Transform parent, string name, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        static Sprite FxSprite(string name)
        {
            switch (name)
            {
                case "fx_glow": return ArtSprites.Prop("map_fx", name, 128, 128);
                case "fx_puff": return ArtSprites.Prop("map_fx", name, 64, 64);
                case "fx_flake": return ArtSprites.Prop("map_fx", name, 16, 16);
                case "fx_sparkle": return ArtSprites.Prop("map_fx", name, 32, 32);
                case "fx_bar": return ArtSprites.Prop("map_fx", name, 64, 8);
                default: return null;
            }
        }

        static void SetAlpha(SpriteRenderer sr, Color c, float a)
        {
            c.a = Mathf.Clamp01(a);
            sr.color = c;
        }

        Fx Create(FxDef f, Layer layer, System.Random rnd)
        {
            int order = layer.Order + 1;
            switch (f.kind)
            {
                case "glow": return new Glow(f, layer.Root, order, (float)rnd.NextDouble() * 10f);
                case "steam": return new Steam(f, layer.Root, order);
                case "snow": return new Snow(f, layer.Root, order, rnd);
                case "dust": return new Dust(f, layer.Root, order, rnd);
                case "tube": return new Tube(f, layer.Root, order, rnd);
                case "sway": return new Sway(f, layer.Root, order, (float)rnd.NextDouble() * 10f);
                case "spin": return new Spin(f, layer.Root, order);
                case "bulbs": return new Bulbs(f, layer.Root, order);
                case "sparkle": return new Sparkle(f, layer.Root, order, rnd);
                default: return null;
            }
        }

        // 은은하게 숨 쉬는 빛 무리. flicker > 0 이면 불꽃처럼 흔들린다.
        sealed class Glow : Fx
        {
            readonly SpriteRenderer _sr;
            readonly Color _c;
            readonly float _a0, _a1, _period, _flicker, _phase;

            public Glow(FxDef f, Transform parent, int order, float phase)
            {
                _c = Hex(f.color, Color.white);
                _sr = Make(parent, "Glow", FxSprite("fx_glow"), _c, order);
                _sr.transform.localPosition = new Vector3(f.at[0], f.at[1], 0);
                _sr.transform.localScale = Vector3.one * f.size;
                _a0 = A(f, 0, 0.05f);
                _a1 = A(f, 1, 0.1f);
                _period = Mathf.Max(0.1f, f.period);
                _flicker = f.flicker;
                _phase = phase;
            }

            public override void Step(float t, float dt)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * t / _period + _phase);
                if (_flicker > 0f) k = Mathf.Lerp(k, Mathf.PerlinNoise(t * 5f, _phase), _flicker);
                SetAlpha(_sr, _c, Mathf.Lerp(_a0, _a1, k));
            }
        }

        // 냄비 김: 퍼프가 차례로 올라가며 커지고 사라진다.
        sealed class Steam : Fx
        {
            readonly SpriteRenderer[] _puffs;
            readonly Color _c;
            readonly Vector2 _at;
            readonly float _size, _rise, _period, _a1;

            public Steam(FxDef f, Transform parent, int order)
            {
                _c = Hex(f.color, Color.white);
                _at = new Vector2(f.at[0], f.at[1]);
                _size = f.size;
                _rise = f.rise;
                _period = Mathf.Max(0.5f, f.period);
                _a1 = A(f, 1, 0.2f);
                _puffs = new SpriteRenderer[Mathf.Max(1, f.count)];
                for (int i = 0; i < _puffs.Length; i++) _puffs[i] = Make(parent, "Steam", FxSprite("fx_puff"), _c, order);
            }

            public override void Step(float t, float dt)
            {
                for (int i = 0; i < _puffs.Length; i++)
                {
                    float u = Mathf.Repeat(t / _period + (float)i / _puffs.Length, 1f);
                    float sway = Mathf.Sin(u * 4f + i * 1.7f) * 0.3f * u;
                    _puffs[i].transform.localPosition = new Vector3(_at.x + sway, _at.y + _rise * u, 0);
                    _puffs[i].transform.localScale = Vector3.one * _size * (0.45f + 1.1f * u);
                    SetAlpha(_puffs[i], _c, _a1 * Mathf.Sin(Mathf.PI * u) * Mathf.Min(1f, u * 4f));
                }
            }
        }

        // 내리는 눈: 흔들리며 떨어지고 아래로 나가면 위에서 다시.
        sealed class Snow : Fx
        {
            readonly SpriteRenderer[] _flakes;
            readonly Vector2[] _pos;
            readonly float[] _speed, _phase;
            readonly Rect _area;

            public Snow(FxDef f, Transform parent, int order, System.Random rnd)
            {
                _area = Rect.MinMaxRect(f.area[0], f.area[1], f.area[2], f.area[3]);
                var c = Hex(f.color, Color.white);
                int n = Mathf.Max(1, f.count);
                _flakes = new SpriteRenderer[n];
                _pos = new Vector2[n];
                _speed = new float[n];
                _phase = new float[n];
                var sprite = FxSprite("fx_flake");
                for (int i = 0; i < n; i++)
                {
                    float a = Mathf.Lerp(A(f, 0, 0.3f), A(f, 1, 0.5f), (float)rnd.NextDouble());
                    c.a = a;
                    _flakes[i] = Make(parent, "Flake", sprite, c, order);
                    _flakes[i].transform.localScale = Vector3.one * f.size * (0.7f + 0.6f * (float)rnd.NextDouble()) / 0.25f;
                    _pos[i] = new Vector2(Mathf.Lerp(_area.xMin, _area.xMax, (float)rnd.NextDouble()),
                        Mathf.Lerp(_area.yMin, _area.yMax, (float)rnd.NextDouble()));
                    _speed[i] = Mathf.Lerp(f.speed[0], f.speed[1], (float)rnd.NextDouble());
                    _phase[i] = (float)rnd.NextDouble() * 6.28f;
                }
            }

            public override void Step(float t, float dt)
            {
                for (int i = 0; i < _flakes.Length; i++)
                {
                    var p = _pos[i];
                    p.y -= _speed[i] * dt;
                    p.x += (0.12f + Mathf.Sin(t * 0.9f + _phase[i]) * 0.25f) * dt;
                    if (p.y < _area.yMin) p.y += _area.height;
                    if (p.x > _area.xMax) p.x -= _area.width;
                    _pos[i] = p;
                    _flakes[i].transform.localPosition = new Vector3(p.x, p.y, 0);
                }
            }
        }

        // 빛줄기(사각형 points: 왼쪽 위, 오른쪽 위, 오른쪽 아래, 왼쪽 아래) 안에서 떠다니는 먼지.
        sealed class Dust : Fx
        {
            readonly SpriteRenderer[] _motes;
            readonly Vector2[] _uv;
            readonly float[] _speed, _phase, _alpha;
            readonly Vector2 _p0, _p1, _p2, _p3;
            readonly Color _c;

            public Dust(FxDef f, Transform parent, int order, System.Random rnd)
            {
                var p = f.points;
                _p0 = new Vector2(p[0], p[1]);
                _p1 = new Vector2(p[2], p[3]);
                _p2 = new Vector2(p[4], p[5]);
                _p3 = new Vector2(p[6], p[7]);
                _c = Hex(f.color, Color.white);
                int n = Mathf.Max(1, f.count);
                _motes = new SpriteRenderer[n];
                _uv = new Vector2[n];
                _speed = new float[n];
                _phase = new float[n];
                _alpha = new float[n];
                var sprite = FxSprite("fx_flake");
                for (int i = 0; i < n; i++)
                {
                    _motes[i] = Make(parent, "Dust", sprite, _c, order);
                    _motes[i].transform.localScale = Vector3.one * f.size * (0.6f + 0.8f * (float)rnd.NextDouble()) / 0.25f;
                    _uv[i] = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
                    _speed[i] = Mathf.Lerp(f.speed[0], f.speed[1], (float)rnd.NextDouble());
                    _phase[i] = (float)rnd.NextDouble() * 100f;
                    _alpha[i] = Mathf.Lerp(A(f, 0, 0.2f), A(f, 1, 0.4f), (float)rnd.NextDouble());
                }
            }

            public override void Step(float t, float dt)
            {
                for (int i = 0; i < _motes.Length; i++)
                {
                    var uv = _uv[i];
                    // 아주 천천히 가라앉으며 좌우로 흔들린다(빛줄기 길이 9 유닛 기준 속도).
                    uv.y = Mathf.Repeat(uv.y + _speed[i] * dt / 9f, 1f);
                    uv.x = Mathf.Clamp01(uv.x + (Mathf.PerlinNoise(_phase[i], t * 0.2f) - 0.5f) * dt * 0.15f);
                    _uv[i] = uv;
                    Vector2 top = Vector2.Lerp(_p0, _p1, uv.x), bottom = Vector2.Lerp(_p3, _p2, uv.x);
                    Vector2 pos = Vector2.Lerp(top, bottom, uv.y);
                    _motes[i].transform.localPosition = new Vector3(pos.x, pos.y, 0);
                    float edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(uv.x, 1f - uv.x), Mathf.Min(uv.y, 1f - uv.y)) * 6f);
                    float twinkle = 0.6f + 0.4f * Mathf.Sin(t * 1.3f + _phase[i]);
                    SetAlpha(_motes[i], _c, _alpha[i] * edge * twinkle);
                }
            }
        }

        // 형광등: 켜진 관 + 아래 빛 무리. flicker = 1 이면 period 초마다 한 번씩 지직거린다.
        sealed class Tube : Fx
        {
            readonly SpriteRenderer _bar, _glow;
            readonly Color _c;
            readonly float _a0, _a1, _period, _flicker;
            readonly System.Random _rnd;
            float _next, _burstEnd, _toggleAt;
            bool _on = true;

            public Tube(FxDef f, Transform parent, int order, System.Random rnd)
            {
                _c = Hex(f.color, Color.white);
                var r = Rect.MinMaxRect(f.area[0], f.area[1], f.area[2], f.area[3]);
                _bar = Make(parent, "Tube", FxSprite("fx_bar"), _c, order + 1);
                _bar.transform.localPosition = new Vector3(r.center.x, r.center.y, 0);
                _bar.transform.localScale = new Vector3(r.width, r.height / 0.125f, 1f);
                _glow = Make(parent, "TubeGlow", FxSprite("fx_glow"), _c, order);
                _glow.transform.localPosition = new Vector3(r.center.x, r.center.y - f.size * 0.25f, 0);
                _glow.transform.localScale = new Vector3(f.size, f.size * 0.75f, 1f);
                _a0 = A(f, 0, 0.4f);
                _a1 = A(f, 1, 0.5f);
                _period = Mathf.Max(1f, f.period);
                _flicker = f.flicker;
                _rnd = rnd;
                _next = (float)rnd.NextDouble() * _period;
            }

            public override void Step(float t, float dt)
            {
                float a = Mathf.Lerp(_a0, _a1, 0.5f + 0.5f * Mathf.Sin(t * 0.7f));
                if (_flicker > 0f)
                {
                    if (t >= _next)
                    {
                        _burstEnd = t + 0.5f + (float)_rnd.NextDouble() * 0.6f;
                        _next = t + _period * (0.7f + 0.6f * (float)_rnd.NextDouble());
                    }
                    if (t < _burstEnd)
                    {
                        if (t >= _toggleAt)
                        {
                            _on = !_on;
                            _toggleAt = t + 0.03f + (float)_rnd.NextDouble() * 0.09f;
                        }
                        if (!_on) a *= 0.15f;
                    }
                    else _on = true;
                }
                SetAlpha(_bar, _c, a);
                SetAlpha(_glow, _c, a * 0.16f);
            }
        }

        // 저절로 흔들리는 매단 소품(겨울 처마 등불). glow 가 있으면 몸통에 빛 무리를 붙인다.
        sealed class Sway : Fx
        {
            readonly Transform _pivot;
            readonly SpriteRenderer _glow;
            readonly Color _glowC;
            readonly float _amp, _period, _phase;

            public Sway(FxDef f, Transform parent, int order, float phase)
            {
                _pivot = new GameObject("Sway").transform;
                _pivot.SetParent(parent, false);
                _pivot.localPosition = new Vector3(f.at[0], f.at[1], 0);
                _pivot.localScale = Vector3.one * (f.scale > 0f ? f.scale : 1f);
                int slash = f.sprite.IndexOf('/');
                var sprite = ArtSprites.Prop(f.sprite.Substring(0, slash), f.sprite.Substring(slash + 1), f.pivot[0], f.pivot[1]);
                Make(_pivot, "Art", sprite, Hex(f.color, Color.white), order + 1);
                if (!string.IsNullOrEmpty(f.glow))
                {
                    _glowC = Hex(f.glow, Color.white);
                    _glow = Make(_pivot, "Glow", FxSprite("fx_glow"), _glowC, order);
                    _glow.transform.localPosition = f.glowAt != null && f.glowAt.Length >= 2
                        ? new Vector3(f.glowAt[0], f.glowAt[1], 0) : Vector3.zero;
                    _glow.transform.localScale = Vector3.one * 0.9f;
                }
                _amp = f.amp;
                _period = Mathf.Max(0.5f, f.period);
                _phase = phase;
            }

            public override void Step(float t, float dt)
            {
                float k = Mathf.Sin(2f * Mathf.PI * t / _period + _phase);
                _pivot.localRotation = Quaternion.Euler(0, 0, _amp * k);
                if (_glow != null)
                    SetAlpha(_glow, _glowC, 0.12f + 0.05f * Mathf.PerlinNoise(t * 4f, _phase));
            }
        }

        // 천천히 도는 것(창고 환풍기). amp = 도/초.
        sealed class Spin : Fx
        {
            readonly Transform _t;
            readonly float _speed;

            public Spin(FxDef f, Transform parent, int order)
            {
                int slash = f.sprite.IndexOf('/');
                var sprite = ArtSprites.Prop(f.sprite.Substring(0, slash), f.sprite.Substring(slash + 1), f.pivot[0], f.pivot[1]);
                var sr = Make(parent, "Spin", sprite, Hex(f.color, Color.white), order);
                _t = sr.transform;
                _t.localPosition = new Vector3(f.at[0], f.at[1], 0);
                _t.localScale = Vector3.one * (f.scale > 0f ? f.scale : 1f);
                _speed = f.amp;
            }

            public override void Step(float t, float dt)
            {
                _t.localRotation = Quaternion.Euler(0, 0, -_speed * t);
            }
        }

        // 꼬마전구 줄: 색을 돌려 가며 물결처럼 켜졌다 꺼진다.
        sealed class Bulbs : Fx
        {
            readonly SpriteRenderer[] _bulbs;
            readonly Color[] _cols;
            readonly float _a0, _a1, _period;

            public Bulbs(FxDef f, Transform parent, int order)
            {
                int n = f.points.Length / 2;
                _bulbs = new SpriteRenderer[n];
                _cols = new Color[n];
                var sprite = FxSprite("fx_glow");
                for (int i = 0; i < n; i++)
                {
                    _cols[i] = Hex(f.colors != null && f.colors.Length > 0 ? f.colors[i % f.colors.Length] : f.color, Color.white);
                    _bulbs[i] = Make(parent, "Bulb", sprite, _cols[i], order);
                    _bulbs[i].transform.localPosition = new Vector3(f.points[2 * i], f.points[2 * i + 1], 0);
                    _bulbs[i].transform.localScale = Vector3.one * f.size;
                }
                _a0 = A(f, 0, 0f);
                _a1 = A(f, 1, 0.4f);
                _period = Mathf.Max(0.5f, f.period);
            }

            public override void Step(float t, float dt)
            {
                for (int i = 0; i < _bulbs.Length; i++)
                {
                    float w = Mathf.Sin(2f * Mathf.PI * (t / _period - i * 0.07f));
                    float k = 0.35f + 0.65f * Mathf.Clamp01(w) * Mathf.Clamp01(w);
                    SetAlpha(_bulbs[i], _cols[i], Mathf.Lerp(_a0, _a1, k));
                }
            }
        }

        // 진열장 유리의 반짝임: 점마다 다른 때에 짧게 반짝인다.
        sealed class Sparkle : Fx
        {
            readonly SpriteRenderer[] _s;
            readonly float[] _phase;
            readonly Color _c;
            readonly float _a1, _period, _size;

            public Sparkle(FxDef f, Transform parent, int order, System.Random rnd)
            {
                int n = f.points.Length / 2;
                _s = new SpriteRenderer[n];
                _phase = new float[n];
                _c = Hex(f.color, Color.white);
                var sprite = FxSprite("fx_sparkle");
                for (int i = 0; i < n; i++)
                {
                    _s[i] = Make(parent, "Sparkle", sprite, _c, order + 1);
                    _s[i].transform.localPosition = new Vector3(f.points[2 * i], f.points[2 * i + 1], 0);
                    _phase[i] = (float)rnd.NextDouble();
                }
                _a1 = A(f, 1, 0.5f);
                _period = Mathf.Max(0.5f, f.period);
                _size = f.size;
            }

            public override void Step(float t, float dt)
            {
                for (int i = 0; i < _s.Length; i++)
                {
                    float u = Mathf.Repeat(t / (_period * (0.8f + 0.4f * _phase[i])) + _phase[i], 1f);
                    float k = u < 0.25f ? Mathf.Sin(u / 0.25f * Mathf.PI) : 0f;
                    SetAlpha(_s[i], _c, _a1 * k);
                    _s[i].transform.localScale = Vector3.one * _size * (0.4f + 0.6f * k) / 0.5f;
                    _s[i].transform.localRotation = Quaternion.Euler(0, 0, 45f * u);
                }
            }
        }
    }
}
