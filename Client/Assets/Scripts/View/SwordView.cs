using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 떨어지는 위험물과 낙하 예고. 예고 중에는 낙하 지점 기둥(위험물 폭 + 0.6)과 위쪽 경고 표시를 깜빡이며 보여준다.
    // 그림은 ArtSprites.Hazard(피벗 = 끝, 아래 가운데). 착지 뒤에는 HazardLanded(고드름 조각, 금 간 상자).
    // 그림이 없는 종류는 단색 도형으로 그린다.
    // 고드름은 예고 중에도 처마 아래 제자리에 매달려 점점 세게 떨린다.
    public sealed class SwordView : MonoBehaviour
    {
        static readonly Color WarnColor = new Color(1f, 0.25f, 0.2f);

        Transform _body;
        SpriteRenderer[] _bodyParts;
        SpriteRenderer _warnColumn;
        SpriteRenderer _warnMark;
        SpriteRenderer _warnGround;
        HazardKind _kind;
        SpriteRenderer _art;   // 그림 한 장으로 그릴 때
        bool _showingLanded;
        float _columnWidth;
        float _stageHeight;

        public static SwordView Create(Transform parent, HazardKind kind, float stageHeight)
        {
            var go = new GameObject("Sword");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<SwordView>();
            Vector2 size = StageLayout.HazardSize(kind);
            v._kind = kind;
            v._stageHeight = stageHeight;
            v._columnWidth = size.x + 0.6f;
            v._warnColumn = Shapes.Rect(go.transform, "WarnColumn", WarnColor, new Vector2(v._columnWidth, stageHeight), -5);
            v._warnMark = Shapes.Rect(go.transform, "WarnMark", WarnColor, new Vector2(0.7f, 0.7f), 20);
            v._warnGround = Shapes.Rect(go.transform, "WarnGround", WarnColor, new Vector2(size.x + 1.2f, 0.12f), 8);
            v._body = new GameObject("Body").transform;
            v._body.SetParent(go.transform, false);
            var art = ArtSprites.Hazard(kind);
            if (art != null)
            {
                var go2 = new GameObject("Art");
                go2.transform.SetParent(v._body, false);
                v._art = go2.AddComponent<SpriteRenderer>();
                v._art.sprite = art;
                v._art.sortingOrder = 15;
                v._bodyParts = new[] { v._art };
            }
            else v._bodyParts = BuildBody(v._body, kind, size);
            return v;
        }

        static SpriteRenderer[] BuildBody(Transform body, HazardKind kind, Vector2 size)
        {
            switch (kind)
            {
                case HazardKind.Icicle:
                {
                    // 위가 넓고 아래로 갈수록 가늘어지는 얼음 기둥(세 토막)
                    var c = new Color(0.78f, 0.93f, 1f);
                    return new[]
                    {
                        Part(body, "Top", c, 0f, size.y * 0.80f, size.x, size.y * 0.4f, 15),
                        Part(body, "Mid", c, 0f, size.y * 0.45f, size.x * 0.65f, size.y * 0.35f, 15),
                        Part(body, "Tip", Color.white, 0f, size.y * 0.13f, size.x * 0.3f, size.y * 0.26f, 15),
                    };
                }
                case HazardKind.Crate:
                {
                    var edge = new Color(0.30f, 0.19f, 0.10f);
                    var wood = new Color(0.66f, 0.46f, 0.26f);
                    return new[]
                    {
                        Part(body, "Edge", edge, 0f, size.y / 2f, size.x, size.y, 15),
                        Part(body, "Wood", wood, 0f, size.y / 2f, size.x - 0.12f, size.y - 0.12f, 16),
                        Part(body, "Plank", edge, 0f, size.y / 2f, size.x - 0.12f, 0.08f, 17),
                        Part(body, "Brace", edge, 0f, size.y / 2f, 0.08f, size.y - 0.12f, 17),
                    };
                }
                case HazardKind.Fork:
                {
                    var steel = new Color(0.86f, 0.88f, 0.92f);
                    return new[]
                    {
                        Part(body, "Tine0", steel, -size.x * 0.4f, 0.2f, 0.08f, 0.4f, 15),
                        Part(body, "Tine1", steel, 0f, 0.2f, 0.08f, 0.4f, 15),
                        Part(body, "Tine2", steel, size.x * 0.4f, 0.2f, 0.08f, 0.4f, 15),
                        Part(body, "Head", steel, 0f, 0.46f, size.x, 0.14f, 15),
                        Part(body, "Handle", new Color(0.70f, 0.72f, 0.78f), 0f, 0.83f, 0.14f, 0.62f, 15),
                    };
                }
                default:
                {
                    var knife = new GameObject("Knife");
                    knife.transform.SetParent(body, false);
                    var sr = knife.AddComponent<SpriteRenderer>();
                    sr.sprite = ArtSprites.Knife;
                    sr.sortingOrder = 15;
                    return new[] { sr };
                }
            }
        }

        static SpriteRenderer Part(Transform parent, string name, Color color, float x, float y, float w, float h, int order)
        {
            var sr = Shapes.Rect(parent, name, color, new Vector2(w, h), order);
            sr.transform.localPosition = new Vector3(x, y, 0);
            return sr;
        }

        public void Apply(SwordRender s)
        {
            bool warning = s.Phase == SwordPhase.Warning;
            _warnColumn.enabled = warning;
            _warnMark.enabled = warning;
            _warnGround.enabled = warning;
            bool hanging = warning && _kind == HazardKind.Icicle;
            _body.gameObject.SetActive(!warning || hanging);

            if (warning)
            {
                // 예고 중에는 x만 의미가 있다. 낙하가 가까울수록 빠르고 진하게 깜빡인다.
                transform.localPosition = new Vector3(s.Position.x, 0, 0);
                float p = Mathf.Clamp01(s.WarningProgress);
                float blink = Mathf.PingPong(Time.time * Mathf.Lerp(2f, 10f, p), 1f);
                _warnColumn.color = new Color(WarnColor.r, WarnColor.g, WarnColor.b, Mathf.Lerp(0.06f, 0.22f, p) * (0.5f + 0.5f * blink));
                // 기둥은 꽂힐 면부터 화면 위까지
                float columnH = Mathf.Max(0.1f, _stageHeight - s.LandingY);
                Shapes.Resize(_warnColumn, new Vector2(_columnWidth, columnH));
                _warnColumn.transform.localPosition = new Vector3(0, s.LandingY + columnH / 2f, 0);
                _warnGround.color = new Color(WarnColor.r, WarnColor.g, WarnColor.b, 0.4f + 0.6f * blink);
                _warnGround.transform.localPosition = new Vector3(0, s.LandingY + 0.06f, 0);
                _warnMark.color = new Color(WarnColor.r, WarnColor.g, WarnColor.b, 0.5f + 0.5f * blink);
                _warnMark.transform.localPosition = new Vector3(0, hanging ? s.SpawnY - 0.6f : _stageHeight - 0.6f, 0);
                _warnMark.transform.localRotation = Quaternion.Euler(0, 0, 45f);
                if (hanging)
                {
                    // 처마 아래에 매달려 떨린다(낙하가 가까울수록 세게).
                    float shake = Mathf.Sin(Time.time * 40f) * Mathf.Lerp(0.01f, 0.07f, p);
                    _body.localPosition = new Vector3(shake, s.SpawnY, 0);
                    _warnMark.enabled = false;
                    SetAlpha(1f);
                }
                return;
            }

            // 끝이 Position 이다. 꽂히거나 부서진 것은 해가 없으므로 흐리게.
            transform.localPosition = new Vector3(s.Position.x, s.Position.y, 0);
            _body.localPosition = Vector3.zero;
            bool landed = s.Phase == SwordPhase.Landed && !s.AsBox;
            if (_art != null && landed != _showingLanded)
            {
                _showingLanded = landed;
                _art.sprite = landed ? ArtSprites.HazardLanded(_kind) ?? _art.sprite : ArtSprites.Hazard(_kind);
            }
            SetAlpha(landed ? 0.6f : 1f);
        }

        void SetAlpha(float a)
        {
            foreach (var sr in _bodyParts)
            {
                var c = sr.color;
                if (!Mathf.Approximately(c.a, a)) sr.color = new Color(c.r, c.g, c.b, a);
            }
        }
    }
}
