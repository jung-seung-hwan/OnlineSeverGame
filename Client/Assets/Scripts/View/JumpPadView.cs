using OnlineSeverGame.Client.Model;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 강제 점프 발판(docs/work/jump-pad.md 5장). 위치는 아래쪽 가운데, 판정 크기 1.2 × 0.5(서버 PushableState 와 같음).
    // 그림 없이 도형으로 맵 테마를 그린다: 받침판 + 지그재그 스프링 + 윗판.
    //   주방: 무쇠 냄비 받침 + 구리 스프링 + 코르크 윗판 / 겨울: 썰매 나무판 + 푸른 강철 스프링 + 눈 덮인 윗판
    //   창고: 팔레트 + 회색 산업 스프링 + 노랑·검정 경고 띠 / 디저트: 분홍 젤리 받침 + 사탕 줄무늬 스프링 + 크림 윗판
    // 누가 위에 서 있으면 조금 눌리고, 튀어 오르는 순간(스냅샷 Spring 이 새로 올라가거나 내 예측이 튀어 오름)에는
    // 0.05초 동안 꽉 눌렸다가 위로 튀어 늘어난 뒤 출렁이며 돌아온다.
    public sealed class JumpPadView : MonoBehaviour
    {
        const int Coils = 4;
        const float BaseH = 0.11f;
        const float TopH = 0.14f;
        const float Wire = 0.085f;
        const float PressSeconds = 0.05f;
        const float WobbleSeconds = 0.45f;

        Transform _base, _top, _springRoot;
        SpriteRenderer _topStripe;
        readonly SpriteRenderer[] _coils = new SpriteRenderer[Coils];
        Vector2 _size;
        int _lastSpring;
        float _kickAt = -10f;
        float _press;

        struct Theme
        {
            public Color Base, BaseEdge, Spring, SpringAlt, Top, TopStripe;
        }

        static Theme ThemeOf(int stageId)
        {
            switch (stageId)
            {
                case 2: return new Theme   // 겨울: 썰매 스프링
                {
                    Base = new Color(0.55f, 0.37f, 0.22f), BaseEdge = new Color(0.86f, 0.92f, 1f),
                    Spring = new Color(0.62f, 0.80f, 0.95f), SpringAlt = new Color(0.50f, 0.68f, 0.86f),
                    Top = new Color(0.55f, 0.37f, 0.22f), TopStripe = new Color(0.96f, 0.98f, 1f),
                };
                case 3: return new Theme   // 창고: 팔레트 스프링
                {
                    Base = new Color(0.70f, 0.52f, 0.30f), BaseEdge = new Color(0.98f, 0.80f, 0.10f),
                    Spring = new Color(0.80f, 0.82f, 0.85f), SpringAlt = new Color(0.66f, 0.68f, 0.72f),
                    Top = new Color(0.20f, 0.20f, 0.20f), TopStripe = new Color(0.98f, 0.80f, 0.10f),
                };
                case 4: return new Theme   // 디저트: 젤리 스프링
                {
                    Base = new Color(0.93f, 0.50f, 0.66f), BaseEdge = new Color(1f, 0.86f, 0.92f),
                    Spring = new Color(1f, 0.95f, 0.97f), SpringAlt = new Color(0.98f, 0.45f, 0.62f),
                    Top = new Color(1f, 0.93f, 0.80f), TopStripe = new Color(0.96f, 0.45f, 0.62f),
                };
                default: return new Theme  // 주방: 냄비 받침 스프링
                {
                    Base = new Color(0.36f, 0.36f, 0.40f), BaseEdge = new Color(0.72f, 0.72f, 0.76f),
                    Spring = new Color(0.98f, 0.62f, 0.30f), SpringAlt = new Color(0.86f, 0.48f, 0.22f),
                    Top = new Color(0.85f, 0.64f, 0.42f), TopStripe = new Color(0.92f, 0.24f, 0.20f),
                };
            }
        }

        public static JumpPadView Create(Transform parent, int stageId)
        {
            var go = new GameObject("JumpPad");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<JumpPadView>();
            var th = ThemeOf(stageId);
            v._base = new GameObject("Base").transform;
            v._base.SetParent(go.transform, false);
            Shapes.Rect(v._base, "Edge", th.BaseEdge, Vector2.one, 6);
            Shapes.Rect(v._base, "Fill", th.Base, Vector2.one, 7).transform.localScale = new Vector3(0.94f, 0.7f, 1f);
            v._springRoot = new GameObject("Spring").transform;
            v._springRoot.SetParent(go.transform, false);
            for (int i = 0; i < v._coils.Length; i++)
                v._coils[i] = Shapes.Rect(v._springRoot, "Coil" + i, i % 2 == 0 ? th.Spring : th.SpringAlt, Vector2.one, 7);
            v._top = new GameObject("Top").transform;
            v._top.SetParent(go.transform, false);
            Shapes.Rect(v._top, "Edge", th.BaseEdge, Vector2.one, 8);
            Shapes.Rect(v._top, "Fill", th.Top, Vector2.one, 9).transform.localScale = new Vector3(0.95f, 0.66f, 1f);
            v._topStripe = Shapes.Rect(v._top, "Stripe", th.TopStripe, Vector2.one, 10);
            v._topStripe.transform.localScale = new Vector3(0.95f, 0.22f, 1f);
            v._topStripe.transform.localPosition = new Vector3(0f, 0.33f, 0f);
            return v;
        }

        // pressed: 누가 위에 서 있음. launching: 이번 프레임에 누가 위에서 튀어 올랐음(그림을 바로 반응시키려고).
        public void Apply(BoxRender b, bool pressed, bool launching)
        {
            transform.localPosition = new Vector3(b.Position.x, b.Position.y, 0);
            _size = b.Size;
            float now = Time.time;
            if ((b.Spring > _lastSpring || launching) && now - _kickAt > 0.4f) _kickAt = now;
            _lastSpring = b.Spring;
            _press = Mathf.MoveTowards(_press, pressed ? 0.25f : 0f, Time.deltaTime * 4f);

            // c > 0: 눌림(높이가 줄어듦), c < 0: 늘어남.
            float c = _press;
            float since = now - _kickAt;
            if (since < PressSeconds) c = Mathf.Max(c, since / PressSeconds * 0.75f);
            else if (since < PressSeconds + WobbleSeconds)
            {
                float u = (since - PressSeconds) / WobbleSeconds;
                c = -0.55f * Mathf.Sin(u * Mathf.PI * 3f) * (1f - u);
            }
            Layout(Mathf.Clamp(c, -0.6f, 0.8f));
        }

        void Layout(float c)
        {
            float w = _size.x, h = _size.y;
            _base.localPosition = new Vector3(0f, BaseH / 2f, 0f);
            _base.localScale = new Vector3(w * 0.92f, BaseH, 1f);
            float springH = Mathf.Max(0.04f, (h - BaseH - TopH) * (1f - c));
            float topY = BaseH + springH;
            _top.localPosition = new Vector3(0f, topY + TopH / 2f, 0f);
            _top.localScale = new Vector3(w, TopH, 1f);

            // 지그재그 스프링: 칸마다 막대 하나가 왼쪽 아래 → 오른쪽 위, 다음 칸은 반대로(옆에서 본 코일).
            float coilW = w * 0.66f;
            float seg = springH / Coils;
            float len = Mathf.Sqrt(coilW * coilW + seg * seg) + Wire * 0.5f;
            float ang = Mathf.Atan2(seg, coilW) * Mathf.Rad2Deg;
            for (int i = 0; i < Coils; i++)
            {
                var t = _coils[i].transform;
                t.localPosition = new Vector3(0f, BaseH + seg * (i + 0.5f), 0f);
                t.localScale = new Vector3(len, Wire, 1f);
                t.localRotation = Quaternion.Euler(0f, 0f, i % 2 == 0 ? ang : -ang);
            }
        }
    }
}
