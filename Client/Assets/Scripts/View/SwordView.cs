using OnlineSeverGame.Client.Model;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 칼과 낙하 예고. 예고 중에는 낙하 지점 기둥과 위쪽 경고 삼각형(사각형)을 깜빡이며 보여준다.
    public sealed class SwordView : MonoBehaviour
    {
        static readonly Color BladeColor = new Color(0.85f, 0.88f, 0.95f);
        static readonly Color HiltColor = new Color(0.55f, 0.35f, 0.20f);
        static readonly Color WarnColor = new Color(1f, 0.25f, 0.2f);

        SpriteRenderer _blade;
        SpriteRenderer _guard;
        SpriteRenderer _hilt;
        SpriteRenderer _warnColumn;
        SpriteRenderer _warnMark;
        SpriteRenderer _warnGround;
        Vector2 _size;
        float _stageHeight;

        public static SwordView Create(Transform parent, Vector2 size, float stageHeight)
        {
            var go = new GameObject("Sword");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<SwordView>();
            v._size = size;
            v._stageHeight = stageHeight;
            v._warnColumn = Shapes.Rect(go.transform, "WarnColumn", WarnColor, new Vector2(size.x * 2.5f, stageHeight), -5);
            v._warnMark = Shapes.Rect(go.transform, "WarnMark", WarnColor, new Vector2(0.7f, 0.7f), 20);
            v._warnGround = Shapes.Rect(go.transform, "WarnGround", WarnColor, new Vector2(size.x * 4f, 0.12f), 8);
            v._blade = Shapes.Rect(go.transform, "Blade", BladeColor, new Vector2(size.x, size.y * 0.72f), 15);
            v._guard = Shapes.Rect(go.transform, "Guard", HiltColor, new Vector2(size.x * 2.6f, size.y * 0.08f), 16);
            v._hilt = Shapes.Rect(go.transform, "Hilt", HiltColor, new Vector2(size.x * 0.7f, size.y * 0.2f), 16);
            return v;
        }

        public void Apply(SwordRender s)
        {
            bool warning = s.Phase == SwordPhase.Warning;
            _warnColumn.enabled = warning;
            _warnMark.enabled = warning;
            _warnGround.enabled = warning;
            _blade.enabled = _guard.enabled = _hilt.enabled = !warning;

            if (warning)
            {
                // 예고 중에는 x만 의미가 있다. 낙하가 가까울수록 빠르고 진하게 깜빡인다.
                transform.localPosition = new Vector3(s.Position.x, 0, 0);
                float p = Mathf.Clamp01(s.WarningProgress);
                float blink = Mathf.PingPong(Time.time * Mathf.Lerp(2f, 10f, p), 1f);
                _warnColumn.color = new Color(WarnColor.r, WarnColor.g, WarnColor.b, Mathf.Lerp(0.06f, 0.22f, p) * (0.5f + 0.5f * blink));
                // 기둥은 꽂힐 면부터 화면 위까지
                float columnH = Mathf.Max(0.1f, _stageHeight - s.LandingY);
                Shapes.Resize(_warnColumn, new Vector2(_size.x * 2.5f, columnH));
                _warnColumn.transform.localPosition = new Vector3(0, s.LandingY + columnH / 2f, 0);
                _warnGround.color = new Color(WarnColor.r, WarnColor.g, WarnColor.b, 0.4f + 0.6f * blink);
                _warnGround.transform.localPosition = new Vector3(0, s.LandingY + 0.06f, 0);
                _warnMark.color = new Color(WarnColor.r, WarnColor.g, WarnColor.b, 0.5f + 0.5f * blink);
                _warnMark.transform.localPosition = new Vector3(0, _stageHeight - 0.6f, 0);
                _warnMark.transform.localRotation = Quaternion.Euler(0, 0, 45f);
                return;
            }

            // 칼끝이 Position 이고 손잡이가 위쪽이다.
            transform.localPosition = new Vector3(s.Position.x, s.Position.y, 0);
            float bladeH = _size.y * 0.72f;
            _blade.transform.localPosition = new Vector3(0, bladeH / 2f, 0);
            _guard.transform.localPosition = new Vector3(0, bladeH + _size.y * 0.04f, 0);
            _hilt.transform.localPosition = new Vector3(0, bladeH + _size.y * 0.08f + _size.y * 0.1f, 0);
            float alpha = s.Phase == SwordPhase.Landed ? 0.6f : 1f;
            _blade.color = new Color(BladeColor.r, BladeColor.g, BladeColor.b, alpha);
        }
    }
}
