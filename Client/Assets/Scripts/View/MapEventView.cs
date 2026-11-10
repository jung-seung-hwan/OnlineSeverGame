using System.Collections.Generic;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 라운드 중간 맵 이벤트 그리기 (docs/work/map-layout.md 4.5). 상태는 Shared MapEventPhysics 가 라운드 틱만으로 정하므로
    // 서버와 같은 값을 그린다(StageView 가 그리는 시각의 Stage 를 넘겨 줌). 그림이 오기 전까지 단색 도형:
    //  - 주방 불: 예고 2초 동안 그 화구가 빨갛게 깜빡이고, 켜지면 화구 폭 × 0.8 높이 불꽃이 일렁인다(캐릭터보다 앞, 반투명).
    //  - 겨울 강풍: 흰 바람 줄이 바람 방향으로 흐른다(예고 때는 몇 개만 옅게).
    //  - 창고 컨베이어: 벨트 위 화살 무늬가 늘 보이고, 예고 때 노랗게 깜빡이고, 켜지면 방향으로 흐른다.
    //  - 디저트 시럽: 예고 때 그 절반 바닥이 갈색으로 깜빡이고, 켜지면 갈색 반투명 웅덩이.
    // 끝나기 20틱 전부터 옅어진다.
    // 맵 비주얼 세션 그림(docs/work/map-visual.md 6장)이 있으면 같은 자리·같은 깜빡임에 그림을 쓴다:
    //  화구 링 달아오름(burner_glow) + 4프레임 불꽃(burner_flame, 3.0 × 1.0), 바람 줄(wind_streak) + 켜진 동안 눈송이,
    //  벨트 무늬(conveyor_belt, 1.0 × 0.25 반복을 x 6~19 마스크 안에서 흘림), 시럽 웅덩이(syrup 9-slice) + 예고 때 흘러내리는 줄기.
    public sealed class MapEventView : MonoBehaviour
    {
        static readonly Color FireGlow = new Color(1f, 0.25f, 0.05f);
        static readonly Color FlameOuter = new Color(1f, 0.45f, 0.05f, 0.85f);
        static readonly Color FlameInner = new Color(1f, 0.85f, 0.25f, 0.9f);
        static readonly Color Streak = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color Chevron = new Color(0.85f, 0.72f, 0.20f);
        static readonly Color Syrup = new Color(0.55f, 0.28f, 0.08f);

        MapEventKind _kind;
        // 불: 화구마다 [빛, 불꽃 바깥 3개, 불꽃 안 2개]
        readonly List<SpriteRenderer[]> _burners = new List<SpriteRenderer[]>();
        readonly List<FloorSpan> _burnerSpans = new List<FloorSpan>();
        // 바람
        readonly List<SpriteRenderer> _streaks = new List<SpriteRenderer>();
        readonly List<Vector2> _streakSpeed = new List<Vector2>();
        // 벨트
        readonly List<SpriteRenderer> _chevrons = new List<SpriteRenderer>();
        FloorSpan _belt;
        // 시럽: 자리 묶음마다 하나
        readonly List<SpriteRenderer> _syrup = new List<SpriteRenderer>();
        readonly List<FloorSpan> _syrupSpans = new List<FloorSpan>();
        float _width;
        float _beltOffset;
        // 그림 쪽
        Sprite[] _flameFrames;                                        // 있으면 _burners[i] = [링 빛, 불꽃]
        readonly List<SpriteRenderer> _flakes = new List<SpriteRenderer>();
        readonly List<Vector2> _flakeSpeed = new List<Vector2>();
        readonly List<SpriteRenderer> _beltTiles = new List<SpriteRenderer>();
        bool _syrupArt;
        readonly List<SpriteRenderer> _drips = new List<SpriteRenderer>();   // 시럽 자리마다 5개
        float _warnStart = -1f;

        public void Build(StageLayout layout)
        {
            MapEventSpec spec = layout.Source.EventSpec;
            _kind = spec != null ? spec.Kind : MapEventKind.None;
            _width = layout.Width;
            switch (_kind)
            {
                case MapEventKind.Fire:
                    var seen = new HashSet<int>();
                    foreach (var set in spec.SpanSets)
                        foreach (var f in set)
                        {
                            if (!seen.Add(f.Left)) continue;
                            float l = StageLayout.ToWorld(f.Left), r = StageLayout.ToWorld(f.Right), w = r - l, c = (l + r) / 2f;
                            _flameFrames = ArtSprites.PropFrames("map", "burner_flame", 4, 192, 0);
                            Sprite glow = ArtSprites.Prop("map", "burner_glow", 192, 0);
                            if (_flameFrames != null && glow != null)
                            {
                                _burners.Add(new[]
                                {
                                    Art("Glow", glow, c, 0f, w / 3f, 1f, -7),
                                    Art("Flame", _flameFrames[0], c, 0f, w / 3f, 1f, 13),
                                });
                                _burnerSpans.Add(f);
                                continue;
                            }
                            _flameFrames = null;
                            var parts = new SpriteRenderer[6];
                            parts[0] = Rect("Glow", FireGlow, c, 0.08f, w, 0.16f, -7);
                            for (int i = 0; i < 3; i++) parts[1 + i] = Rect("Flame", FlameOuter, l + w * (i + 0.5f) / 3f, 0.4f, w / 3f * 1.15f, 0.8f, 13);
                            for (int i = 0; i < 2; i++) parts[4 + i] = Rect("Core", FlameInner, l + w * (i + 1) / 3f, 0.25f, w / 3f * 0.9f, 0.5f, 14);
                            _burners.Add(parts);
                            _burnerSpans.Add(f);
                        }
                    break;
                case MapEventKind.Wind:
                    var rng = new System.Random(7);
                    Sprite streak = ArtSprites.Prop("map_winter", "wind_streak", 128, 6);
                    Sprite flake = ArtSprites.Prop("map_winter", "snowflake", 8, 8);
                    for (int i = 0; i < 30; i++)
                    {
                        float len = 1.2f + (float)rng.NextDouble() * 1.6f;
                        float x = (float)rng.NextDouble() * _width, y = 0.4f + (float)rng.NextDouble() * 11.5f;
                        var sr = streak != null ? Art("Wind", streak, x, y, len / 2f, 1f, 20) : Rect("Wind", Streak, x, y, len, 0.06f, 20);
                        _streaks.Add(sr);
                        _streakSpeed.Add(new Vector2(10f + (float)rng.NextDouble() * 8f, (float)rng.NextDouble()));
                    }
                    if (flake != null)
                        for (int i = 0; i < 24; i++)
                        {
                            float k = 0.8f + (float)rng.NextDouble() * 0.8f;
                            _flakes.Add(Art("Flake", flake, (float)rng.NextDouble() * _width, 0.5f + (float)rng.NextDouble() * 11.5f, k, k, 20));
                            _flakeSpeed.Add(new Vector2(5f + (float)rng.NextDouble() * 4f, 0.3f + (float)rng.NextDouble() * 0.5f));
                        }
                    break;
                case MapEventKind.Conveyor:
                    _belt = spec.SpanSets[0][0];
                    Sprite tile = ArtSprites.MapSprite("map_warehouse", "conveyor_belt", Vector2.zero);
                    if (tile != null)
                    {
                        // 타일을 한 칸 더 깔고 벨트 자리(마스크) 밖은 잘라 낸다
                        float bl = StageLayout.ToWorld(_belt.Left), br = StageLayout.ToWorld(_belt.Right);
                        var mask = new GameObject("BeltMask").AddComponent<SpriteMask>();
                        mask.transform.SetParent(transform, false);
                        mask.sprite = Shapes.White;
                        mask.transform.localPosition = new Vector3((bl + br) / 2f, -0.125f, 0);
                        mask.transform.localScale = new Vector3(br - bl, 0.25f, 1f);
                        for (int i = 0; i <= Mathf.CeilToInt(br - bl); i++)
                        {
                            var t = Art("Belt", tile, bl - 1f + i, -0.25f, 1f, 1f, -9);
                            t.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                            _beltTiles.Add(t);
                        }
                    }
                    for (float x = StageLayout.ToWorld(_belt.Left) + 0.5f; x < StageLayout.ToWorld(_belt.Right); x += 1f)
                        _chevrons.Add(Rect("Chevron", Chevron, x, -0.07f, 0.45f, 0.07f, -8));
                    break;
                case MapEventKind.Syrup:
                    foreach (var set in spec.SpanSets)
                        foreach (var f in set)
                        {
                            float l = StageLayout.ToWorld(f.Left), r = StageLayout.ToWorld(f.Right);
                            Sprite pool = ArtSprites.MapSprite("map_dessert", "syrup", new Vector2(0.5f, 0.5f), new Vector4(36, 0, 36, 0));
                            Sprite drip = ArtSprites.Prop("map_dessert", "syrup_drip", 32, 128);
                            _syrupArt = pool != null && drip != null;
                            if (_syrupArt)
                            {
                                var sr = Art("Syrup", pool, (l + r) / 2f, 0.1f, 1f, 1f, -7);
                                sr.drawMode = SpriteDrawMode.Sliced;
                                sr.size = new Vector2(r - l, 0.3f);
                                _syrup.Add(sr);
                                for (int k = 0; k < 5; k++) _drips.Add(Art("Drip", drip, l + (r - l) * (k + 0.5f) / 5f, 13.5f, 1f, 1f, -6));
                            }
                            else _syrup.Add(Rect("Syrup", Syrup, (l + r) / 2f, 0.07f, r - l, 0.22f, -7));
                            _syrupSpans.Add(f);
                        }
                    break;
            }
            Refresh(layout, layout.Source);
        }

        public void Refresh(StageLayout layout, Stage at)
        {
            MapEventState e = at.Event;
            bool warning = e.IsWarning, active = e.IsActive;
            // 예고 깜빡임: 끝날수록 빨라진다(낙하 예고와 같은 느낌)
            float blink = warning ? 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(18f, 6f, e.TicksLeft / 40f)) : 0f;
            float fade = active ? Mathf.Clamp01(e.TicksLeft / 20f) : 0f;

            switch (_kind)
            {
                case MapEventKind.Fire:
                    for (int i = 0; i < _burners.Count; i++)
                    {
                        bool lit = (warning || active) && Contains(e.Spans, _burnerSpans[i]);
                        var parts = _burners[i];
                        if (_flameFrames != null)
                        {
                            SetAlpha(parts[0], Color.white, lit ? (active ? 0.9f : 0.3f + 0.7f * blink) : 0f);
                            bool on = lit && active;
                            parts[1].enabled = on;
                            if (!on) continue;
                            parts[1].sprite = _flameFrames[(Mathf.FloorToInt(Time.time * 12f) + i) % _flameFrames.Length];
                            parts[1].color = new Color(1f, 1f, 1f, Mathf.Max(0.25f, fade));
                            continue;
                        }
                        SetAlpha(parts[0], FireGlow, lit ? (active ? 0.8f : 0.25f + 0.6f * blink) : 0f);
                        for (int k = 1; k < parts.Length; k++)
                        {
                            bool on = lit && active;
                            parts[k].enabled = on;
                            if (!on) continue;
                            float h = (k < 4 ? 0.8f : 0.5f) * (0.8f + 0.25f * Mathf.PerlinNoise(Time.time * 6f, i * 3.1f + k));
                            var tr = parts[k].transform;
                            tr.localScale = new Vector3(tr.localScale.x, h, 1f);
                            tr.localPosition = new Vector3(tr.localPosition.x, h / 2f, 0);
                            SetAlpha(parts[k], k < 4 ? FlameOuter : FlameInner, (k < 4 ? FlameOuter.a : FlameInner.a) * Mathf.Max(0.25f, fade));
                        }
                    }
                    break;
                case MapEventKind.Wind:
                    for (int i = 0; i < _streaks.Count; i++)
                    {
                        var sr = _streaks[i];
                        bool on = active || (warning && i % 5 == 0);
                        sr.enabled = on;
                        if (!on) continue;
                        float speed = _streakSpeed[i].x * (active ? 1f : 0.4f);
                        var p = sr.transform.localPosition;
                        p.x += e.Dir * speed * Time.deltaTime;
                        if (p.x > _width + 2f) p.x -= _width + 4f;
                        if (p.x < -2f) p.x += _width + 4f;
                        p.y += Mathf.Sin(Time.time * 2f + i) * 0.2f * Time.deltaTime;
                        sr.transform.localPosition = p;
                        SetAlpha(sr, Streak, Streak.a * (active ? Mathf.Max(0.3f, fade) : 0.35f * blink));
                    }
                    for (int i = 0; i < _flakes.Count; i++)
                    {
                        var sr = _flakes[i];
                        sr.enabled = active;
                        if (!active) continue;
                        var p = sr.transform.localPosition;
                        p.x += e.Dir * _flakeSpeed[i].x * Time.deltaTime;
                        p.y += (Mathf.Sin(Time.time * 3f + i) * 0.6f - _flakeSpeed[i].y) * Time.deltaTime;
                        if (p.x > _width + 1f) p.x -= _width + 2f;
                        if (p.x < -1f) p.x += _width + 2f;
                        if (p.y < 0.3f) p.y += 11.5f;
                        sr.transform.localPosition = p;
                        SetAlpha(sr, Color.white, 0.8f * Mathf.Max(0.3f, fade));
                    }
                    break;
                case MapEventKind.Conveyor:
                    if (active) _beltOffset += e.Dir * e.Strength * GameConstants.TickRate / 1000f * Time.deltaTime;
                    _beltOffset = Mathf.Repeat(_beltOffset, 1f);
                    float l0 = StageLayout.ToWorld(_belt.Left), r0 = StageLayout.ToWorld(_belt.Right);
                    for (int i = 0; i < _beltTiles.Count; i++)
                        _beltTiles[i].transform.localPosition = new Vector3(l0 - 1f + i + _beltOffset, -0.25f, 0);
                    for (int i = 0; i < _chevrons.Count; i++)
                    {
                        var sr = _chevrons[i];
                        float x = l0 + Mathf.Repeat(i + 0.5f + _beltOffset, r0 - l0);
                        sr.transform.localPosition = new Vector3(x, -0.07f, 0);
                        sr.transform.localRotation = Quaternion.Euler(0, 0, active ? -e.Dir * 25f : 0f);
                        Color c = warning ? Color.Lerp(Chevron * 0.5f, Color.yellow, blink) : active ? Color.yellow : Chevron * 0.45f;
                        c.a = 1f;
                        sr.color = c;
                    }
                    break;
                case MapEventKind.Syrup:
                    for (int i = 0; i < _syrup.Count; i++)
                    {
                        bool on = (warning || active) && Contains(e.Spans, _syrupSpans[i]);
                        SetAlpha(_syrup[i], _syrupArt ? Color.white : Syrup, on ? (active ? 0.9f * Mathf.Max(0.3f, fade) : 0.2f + 0.5f * blink) : 0f);
                        if (!_syrupArt) continue;
                        // 예고 2초 동안 그 절반 위에서 시럽 줄기가 흘러내린다(장식)
                        if (warning && _warnStart < 0f) _warnStart = Time.time;
                        float t = warning ? (Time.time - _warnStart) / 2f : 0f;
                        for (int k = 0; k < 5; k++)
                        {
                            var d = _drips[i * 5 + k];
                            bool show = on && warning;
                            d.enabled = show;
                            if (!show) continue;
                            float q = Mathf.Clamp01(t * 1.25f - (k % 3) * 0.12f);
                            var dp = d.transform.localPosition;
                            d.transform.localPosition = new Vector3(dp.x, Mathf.Lerp(13.5f, 1.0f, q * q), 0);
                            d.color = new Color(1f, 1f, 1f, 0.85f * (1f - Mathf.Clamp01((q - 0.85f) / 0.15f)));
                        }
                    }
                    if (!warning) _warnStart = -1f;
                    break;
            }
        }

        static bool Contains(IReadOnlyList<FloorSpan> spans, FloorSpan f)
        {
            foreach (var s in spans)
                if (s.Left == f.Left && s.Right == f.Right) return true;
            return false;
        }

        static void SetAlpha(SpriteRenderer sr, Color c, float a)
        {
            c.a = a;
            sr.color = c;
            sr.enabled = a > 0.001f;
        }

        SpriteRenderer Art(string name, Sprite sprite, float x, float y, float sx, float sy, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(x, y, 0);
            go.transform.localScale = new Vector3(sx, sy, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        SpriteRenderer Rect(string name, Color color, float cx, float cy, float w, float h, int order)
        {
            var sr = Shapes.Rect(transform, name, color, new Vector2(w, h), order);
            sr.transform.localPosition = new Vector3(cx, cy, 0);
            return sr;
        }
    }
}
