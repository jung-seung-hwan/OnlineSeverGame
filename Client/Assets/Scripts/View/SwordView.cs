using OnlineSeverGame.Client.Model;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 칼(ArtSprites.Knife, 피벗 = 칼끝)과 낙하 예고. 예고 중에는 낙하 지점 기둥과 위쪽 경고 표시를 깜빡이며 보여준다.
    public sealed class SwordView : MonoBehaviour
    {
        static readonly Color WarnColor = new Color(1f, 0.25f, 0.2f);

        SpriteRenderer _knife;
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
            var knife = new GameObject("Knife");
            knife.transform.SetParent(go.transform, false);
            v._knife = knife.AddComponent<SpriteRenderer>();
            v._knife.sprite = ArtSprites.Knife;
            v._knife.sortingOrder = 15;
            return v;
        }

        public void Apply(SwordRender s)
        {
            bool warning = s.Phase == SwordPhase.Warning;
            _warnColumn.enabled = warning;
            _warnMark.enabled = warning;
            _warnGround.enabled = warning;
            _knife.enabled = !warning;

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

            // 칼끝이 Position 이고 손잡이가 위쪽이다. 꽂힌 칼은 해가 없으므로 흐리게.
            transform.localPosition = new Vector3(s.Position.x, s.Position.y, 0);
            _knife.color = new Color(1f, 1f, 1f, s.Phase == SwordPhase.Landed ? 0.6f : 1f);
        }
    }
}
