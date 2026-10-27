using OnlineSeverGame.Client.Model;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 옆 칼과 그 높이의 가로줄 예고(docs/work/dash-side-knife.md 3.5).
    // 예고 중: 맵 폭 전체에 빨간 띠, 들어올 쪽 끝에 화살표 세 개. 끝날수록 빠르고 진하게 깜빡인다(낙하 예고 기둥과 같은 박자).
    // 날아가는 중: 칼(기존 칼 그림을 눕힘, 피벗 = 칼끝)과, 아직 지나가지 않은 쪽 띠만 옅게.
    public sealed class SideKnifeView : MonoBehaviour
    {
        static readonly Color WarnColor = new Color(1f, 0.25f, 0.2f);
        const float BandHeight = 0.45f;   // 칼 두께 0.25 + 0.2

        SpriteRenderer _band;
        SpriteRenderer[] _arrows;
        SpriteRenderer _knife;
        float _stageWidth;

        public static SideKnifeView Create(Transform parent, float stageWidth)
        {
            var go = new GameObject("SideKnife");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<SideKnifeView>();
            v._stageWidth = stageWidth;
            v._band = Shapes.Rect(go.transform, "Band", WarnColor, new Vector2(stageWidth, BandHeight), -4);
            v._arrows = new SpriteRenderer[3];
            for (int i = 0; i < v._arrows.Length; i++)
            {
                v._arrows[i] = Shapes.Rect(go.transform, "Arrow" + i, WarnColor, new Vector2(0.32f, 0.32f), 20);
                v._arrows[i].transform.localRotation = Quaternion.Euler(0, 0, 45f);
            }
            var k = new GameObject("Knife");
            k.transform.SetParent(go.transform, false);
            v._knife = k.AddComponent<SpriteRenderer>();
            v._knife.sprite = ArtSprites.Knife;
            v._knife.sortingOrder = 15;
            return v;
        }

        public void Apply(SideKnifeRender s)
        {
            transform.localPosition = new Vector3(0, s.LaneY, 0);
            float dir = s.Dir >= 0 ? 1f : -1f;
            float entryX = dir > 0 ? 0f : _stageWidth;

            if (s.Warning)
            {
                float p = Mathf.Clamp01(s.WarningProgress);
                float blink = Mathf.PingPong(Time.time * Mathf.Lerp(2f, 10f, p), 1f);
                _band.enabled = true;
                Shapes.Resize(_band, new Vector2(_stageWidth, BandHeight));
                _band.transform.localPosition = new Vector3(_stageWidth / 2f, 0, 0);
                _band.color = new Color(WarnColor.r, WarnColor.g, WarnColor.b, Mathf.Lerp(0.10f, 0.32f, p) * (0.5f + 0.5f * blink));
                for (int i = 0; i < _arrows.Length; i++)
                {
                    _arrows[i].enabled = true;
                    _arrows[i].transform.localPosition = new Vector3(entryX + dir * (0.35f + 0.45f * i), 0, 0);
                    _arrows[i].color = new Color(WarnColor.r, WarnColor.g, WarnColor.b, 0.5f + 0.5f * blink);
                }
                _knife.enabled = false;
                return;
            }

            foreach (var a in _arrows) a.enabled = false;
            // 아직 지나가지 않은 구간(칼끝 앞 ~ 반대쪽 벽)만 옅게 남긴다.
            float from = Mathf.Clamp(s.TipX, 0f, _stageWidth);
            float to = dir > 0 ? _stageWidth : 0f;
            float len = Mathf.Abs(to - from);
            _band.enabled = len > 0.05f;
            if (_band.enabled)
            {
                Shapes.Resize(_band, new Vector2(len, BandHeight));
                _band.transform.localPosition = new Vector3((from + to) / 2f, 0, 0);
                _band.color = new Color(WarnColor.r, WarnColor.g, WarnColor.b, 0.10f);
            }
            // 칼 그림은 끝이 아래를 향한다. 오른쪽으로 날면 반시계 90도, 왼쪽이면 시계 90도(HitShapes.SideKnifeSeparation 과 같은 방향).
            _knife.enabled = true;
            _knife.transform.localPosition = new Vector3(s.TipX, 0, 0);
            _knife.transform.localRotation = Quaternion.Euler(0, 0, dir > 0 ? 90f : -90f);
        }
    }
}
