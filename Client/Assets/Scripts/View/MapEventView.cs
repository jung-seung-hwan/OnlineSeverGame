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
                    for (int i = 0; i < 30; i++)
                    {
                        float len = 1.2f + (float)rng.NextDouble() * 1.6f;
                        var sr = Rect("Wind", Streak, (float)rng.NextDouble() * _width, 0.4f + (float)rng.NextDouble() * 11.5f, len, 0.06f, 20);
                        _streaks.Add(sr);
                        _streakSpeed.Add(new Vector2(10f + (float)rng.NextDouble() * 8f, (float)rng.NextDouble()));
                    }
                    break;
                case MapEventKind.Conveyor:
                    _belt = spec.SpanSets[0][0];
                    for (float x = StageLayout.ToWorld(_belt.Left) + 0.5f; x < StageLayout.ToWorld(_belt.Right); x += 1f)
                        _chevrons.Add(Rect("Chevron", Chevron, x, -0.07f, 0.45f, 0.07f, -8));
                    break;
                case MapEventKind.Syrup:
                    foreach (var set in spec.SpanSets)
                        foreach (var f in set)
                        {
                            float l = StageLayout.ToWorld(f.Left), r = StageLayout.ToWorld(f.Right);
                            _syrup.Add(Rect("Syrup", Syrup, (l + r) / 2f, 0.07f, r - l, 0.22f, -7));
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
                    break;
                case MapEventKind.Conveyor:
                    if (active) _beltOffset += e.Dir * e.Strength * GameConstants.TickRate / 1000f * Time.deltaTime;
                    _beltOffset = Mathf.Repeat(_beltOffset, 1f);
                    float l0 = StageLayout.ToWorld(_belt.Left), r0 = StageLayout.ToWorld(_belt.Right);
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
                        SetAlpha(_syrup[i], Syrup, on ? (active ? 0.9f * Mathf.Max(0.3f, fade) : 0.2f + 0.5f * blink) : 0f);
                    }
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

        SpriteRenderer Rect(string name, Color color, float cx, float cy, float w, float h, int order)
        {
            var sr = Shapes.Rect(transform, name, color, new Vector2(w, h), order);
            sr.transform.localPosition = new Vector3(cx, cy, 0);
            return sr;
        }
    }
}
