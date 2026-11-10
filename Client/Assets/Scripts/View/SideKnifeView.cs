using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 옆 위험물과 그 높이의 가로줄 예고(docs/work/dash-side-knife.md 3.5, 맵별 종류는 docs/work/side-hazards-i18n.md 3장).
    // 맵마다 그림이 다르다: 주방 칼(낙하 칼 그림을 눕힘), 겨울 눈덩이(굴러가며 돈다), 창고 나무 상자, 디저트 마카롱.
    // 예고 중: 맵 폭 전체에 빨간 띠(위험물 두께 + 0.2), 들어올 쪽 끝에 무엇이 오는지 보이는 흐린 그림.
    // 끝날수록 빠르고 진하게 깜빡인다(낙하 예고 기둥과 같은 박자).
    // 날아가는 중: 위험물 그림, 뒤로 흐린 잔상 두 개(바닥에 놓인 같은 그림의 상자·마카롱·눈덩이와 구별되게), 아직 지나가지 않은 쪽 띠만 옅게.
    public sealed class SideKnifeView : MonoBehaviour
    {
        static readonly Color WarnColor = new Color(1f, 0.25f, 0.2f);

        SpriteRenderer _band;
        SpriteRenderer _body;
        SpriteRenderer _icon;
        SpriteRenderer[] _trail;
        SpriteRenderer _fallback;   // 그림을 못 불러오면 판정 크기 사각형
        float _stageWidth;
        SideHazardKind _kind;
        bool _built;

        public static SideKnifeView Create(Transform parent, float stageWidth)
        {
            var go = new GameObject("SideKnife");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<SideKnifeView>();
            v._stageWidth = stageWidth;
            v._band = Shapes.Rect(go.transform, "Band", WarnColor, new Vector2(stageWidth, 0.45f), -4);
            v._body = NewSprite(go.transform, "Body", 15);
            v._icon = NewSprite(go.transform, "Icon", 19);
            v._trail = new[] { NewSprite(go.transform, "Trail0", 14), NewSprite(go.transform, "Trail1", 13) };
            v._fallback = Shapes.Rect(go.transform, "Fallback", new Color(0.85f, 0.85f, 0.9f), Vector2.one, 15);
            v._fallback.enabled = false;
            return v;
        }

        static SpriteRenderer NewSprite(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            return sr;
        }

        void Build(SideHazardKind kind)
        {
            _kind = kind;
            _built = true;
            Sprite sprite = kind == SideHazardKind.Knife ? ArtSprites.Knife : ArtSprites.SideHazard(kind);
            float scale = ArtSprites.SideHazardScale(kind);
            foreach (var sr in new[] { _body, _icon, _trail[0], _trail[1] })
            {
                sr.sprite = sprite;
                sr.transform.localScale = new Vector3(scale, scale, 1f);
            }
            var spec = SideHazardSpec.Of(kind);
            float thickness = StageLayout.ToWorld(spec.HalfThickness * 2);
            Shapes.Resize(_band, new Vector2(_stageWidth, thickness + 0.2f));
            if (sprite == null) Shapes.Resize(_fallback, new Vector2(StageLayout.ToWorld(spec.Length), thickness));
        }

        public void Apply(SideKnifeRender s)
        {
            if (!_built || s.Kind != _kind) Build(s.Kind);
            var spec = SideHazardSpec.Of(s.Kind);
            float halfLen = StageLayout.ToWorld(spec.Length) / 2f;
            float bandH = StageLayout.ToWorld(spec.HalfThickness * 2) + 0.2f;
            transform.localPosition = new Vector3(0, s.LaneY, 0);
            float dir = s.Dir >= 0 ? 1f : -1f;
            float entryX = dir > 0 ? 0f : _stageWidth;

            if (s.Warning)
            {
                float p = Mathf.Clamp01(s.WarningProgress);
                float blink = Mathf.PingPong(Time.time * Mathf.Lerp(2f, 10f, p), 1f);
                _band.enabled = true;
                Shapes.Resize(_band, new Vector2(_stageWidth, bandH));
                _band.transform.localPosition = new Vector3(_stageWidth / 2f, 0, 0);
                _band.color = new Color(WarnColor.r, WarnColor.g, WarnColor.b, Mathf.Lerp(0.10f, 0.32f, p) * (0.5f + 0.5f * blink));
                // 들어올 쪽 끝에 무엇이 올지 흐리게.
                float iconTip = entryX + dir * (0.35f + 2f * halfLen);
                Place(_icon, iconTip, halfLen, dir, 0f);
                _icon.color = new Color(1f, 1f, 1f, 0.35f + 0.3f * blink);
                _body.enabled = false;
                _fallback.enabled = false;
                foreach (var t in _trail) t.enabled = false;
                return;
            }

            _icon.enabled = false;
            // 아직 지나가지 않은 구간(앞 끝 앞 ~ 반대쪽 벽)만 옅게 남긴다.
            float from = Mathf.Clamp(s.TipX, 0f, _stageWidth);
            float to = dir > 0 ? _stageWidth : 0f;
            float len = Mathf.Abs(to - from);
            _band.enabled = len > 0.05f;
            if (_band.enabled)
            {
                Shapes.Resize(_band, new Vector2(len, bandH));
                _band.transform.localPosition = new Vector3((from + to) / 2f, 0, 0);
                _band.color = new Color(WarnColor.r, WarnColor.g, WarnColor.b, 0.10f);
            }
            // 눈덩이는 굴러가며 돈다(둥근 판정이라 돌아도 맞는 모양이 같다).
            float roll = s.Kind == SideHazardKind.Snowball ? -s.TipX / halfLen * Mathf.Rad2Deg : 0f;
            Place(_body, s.TipX, halfLen, dir, roll);
            _body.color = Color.white;
            // 잔상: 뒤로 길이의 0.6배씩, 흐리게(속도감). 칼은 원래 길어서 하나만.
            for (int i = 0; i < _trail.Length; i++)
            {
                bool show = s.Kind != SideHazardKind.Knife || i == 0;
                float back = (i + 1) * 1.2f * halfLen;
                Place(_trail[i], s.TipX - dir * back, halfLen, dir, roll);
                _trail[i].enabled &= show;
                _trail[i].color = new Color(1f, 1f, 1f, i == 0 ? 0.32f : 0.14f);
            }
            _fallback.enabled = _body.sprite == null;
            if (_fallback.enabled) _fallback.transform.localPosition = new Vector3(s.TipX - dir * halfLen, 0, 0);
        }

        // 앞 끝이 tipX 에 오게 놓는다. 칼 그림은 끝(피벗)이 아래를 향하므로 오른쪽으로 날면 반시계 90도, 왼쪽이면 시계 90도
        // (HitShapes.SideHazardSeparation 과 같은 방향). 다른 그림은 피벗이 가운데이고, 왼쪽으로 날면 좌우를 뒤집는다.
        void Place(SpriteRenderer sr, float tipX, float halfLen, float dir, float roll)
        {
            sr.enabled = sr.sprite != null;
            if (_kind == SideHazardKind.Knife)
            {
                sr.transform.localPosition = new Vector3(tipX, 0, 0);
                sr.transform.localRotation = Quaternion.Euler(0, 0, dir > 0 ? 90f : -90f);
                sr.flipX = false;
                return;
            }
            sr.transform.localPosition = new Vector3(tipX - dir * halfLen, 0, 0);
            sr.transform.localRotation = Quaternion.Euler(0, 0, roll);
            sr.flipX = dir < 0 && _kind != SideHazardKind.Snowball;
        }
    }
}
