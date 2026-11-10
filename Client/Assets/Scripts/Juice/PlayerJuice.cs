using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Client.View;
using UnityEngine;

namespace OnlineSeverGame.Client.Juice
{
    // PlayerView 옆에 붙는 연출 컴포넌트(PlayerView 는 고치지 않는다: 캐릭터 모션 세션과 겹치지 않게).
    // - 늘어남·찌그러짐: PlayerView 가 루트에 거는 엎드림·납작 배율 위에, 몸(Body)·잔상(Trail) 자식의 배율로 덧붙인다.
    //   점프 순간 세로로 늘어나고, 착지하면 떨어진 속도만큼 찌그러졌다가 스프링처럼 출렁이며 돌아온다. 공중에서는 빠를수록 조금 늘어난다.
    //   대시 시작에는 가로로 늘어난다. 피벗이 발밑이라 발이 땅에서 뜨지 않는다.
    // - 납작 기절 중 머리 위를 도는 별 세 개(월드 좌표에 따로 그린다: 루트가 납작하게 눌려 있어 자식으로 두면 별도 눌린다).
    // - 기절 면역 중 가끔 반짝임.
    // 화면 표시만 바꾼다(판정·예측과 무관). 시간은 Time.deltaTime 이라 멈춤·슬로모션을 따른다.
    public sealed class PlayerJuice : MonoBehaviour
    {
        const float Stiffness = 380f;   // 출렁임 빠르기(약 3 Hz)
        const float Damping = 11f;      // 작을수록 오래 출렁인다
        const float MaxDeform = 0.42f;

        PlayerView _view;
        Transform _body, _trail;
        PlayerRender _prev;
        bool _hasPrev;
        float _def, _vel;        // + 세로로 늘어남, - 찌그러짐
        float _air;
        float _dash;             // 대시 가로 늘어남(0..1, 줄어든다)
        SpriteRenderer[] _stars;
        float _starAngle;
        float _sparkleTimer;
        JuiceParticles _particles;

        public float Deform => _def + _air;

        public void Init(PlayerView view, JuiceParticles particles, Transform starParent)
        {
            _view = view;
            _particles = particles;
            _body = view.transform.Find("Body");
            _trail = view.transform.Find("Trail");
            _stars = new SpriteRenderer[3];
            for (int i = 0; i < _stars.Length; i++)
            {
                var go = new GameObject("StunStar");
                go.transform.SetParent(starParent, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = JuiceParticles.Star;
                sr.color = new Color(1f, 0.93f, 0.45f, 1f);
                sr.sortingOrder = 14;
                sr.enabled = false;
                _stars[i] = sr;
            }
        }

        void OnDestroy()
        {
            if (_stars == null) return;
            foreach (var s in _stars) if (s != null) Destroy(s.gameObject);
        }

        void LateUpdate()
        {
            if (_view == null) return;
            var s = _view.State;
            float dt = Time.deltaTime;

            if (!s.Alive || s.Spectator)
            {
                _def = _vel = _air = _dash = 0f;
                ApplyScale(1f, 1f);
                ShowStars(false, s);
                _prev = s;
                _hasPrev = true;
                return;
            }

            if (_hasPrev && _prev.Alive && (s.Position - _prev.Position).sqrMagnitude < 6f)
            {
                if (_prev.Grounded && !s.Grounded && s.VelocityY > 5f && !s.Stunned)
                {
                    _def = 0.24f; _vel = 1.5f;       // 점프: 쭉 늘어남
                }
                else if (!_prev.Grounded && !s.Grounded && _prev.VelocityY <= 1f && s.VelocityY > 8f && !s.Stunned)
                {
                    _def = -0.3f; _vel = 0f;         // 밟고 튕김: 발이 눌렸다가 튄다
                }
                if (!_prev.Grounded && s.Grounded)
                {
                    float impact = Mathf.Clamp01(-_prev.VelocityY / 20f);
                    _def = -(0.12f + 0.3f * impact); // 착지: 떨어진 속도만큼 찌그러짐
                    _vel = 0f;
                }
                if (!_prev.Dashing && s.Dashing) _dash = 1f;
            }

            // 스프링(질량 1): 출렁이며 0 으로
            if (dt > 0f)
            {
                float acc = -Stiffness * _def - Damping * _vel;
                _vel += acc * dt;
                _def += _vel * dt;
                _def = Mathf.Clamp(_def, -MaxDeform, MaxDeform);
                float airTarget = s.Grounded || s.Stomped ? 0f : Mathf.Clamp(Mathf.Abs(s.VelocityY) / 20f, 0f, 1f) * 0.12f;
                _air = Mathf.Lerp(_air, airTarget, 1f - Mathf.Exp(-14f * dt));
                _dash = Mathf.MoveTowards(_dash, 0f, dt / 0.2f);
            }

            float d = _def + _air;
            float sy = 1f + d;
            float sx = 1f - d * 0.55f;          // 부피를 대략 유지
            sx *= 1f + 0.22f * _dash;
            sy *= 1f - 0.12f * _dash;
            ApplyScale(sx, sy);

            ShowStars(s.Stomped, s);

            if (s.ImmuneTicks > 0 && dt > 0f)
            {
                _sparkleTimer -= dt;
                if (_sparkleTimer <= 0f)
                {
                    _sparkleTimer = 0.14f;
                    _particles.Sparkle(s.Position + new Vector2(Random.Range(-0.45f, 0.45f), Random.Range(0.2f, 1.2f)));
                }
            }

            _prev = s;
            _hasPrev = true;
        }

        void ApplyScale(float sx, float sy)
        {
            var v = new Vector3(sx, sy, 1f);
            if (_body != null) _body.localScale = v;
            if (_trail != null) _trail.localScale = v;
        }

        void ShowStars(bool on, PlayerRender s)
        {
            if (_stars == null) return;
            if (!on)
            {
                foreach (var st in _stars) st.enabled = false;
                return;
            }
            _starAngle += Time.deltaTime * 360f;
            var center = s.Position + new Vector2(0f, 0.75f);
            for (int i = 0; i < _stars.Length; i++)
            {
                float a = (_starAngle + i * 120f) * Mathf.Deg2Rad;
                float depth = Mathf.Sin(a);                       // 뒤로 돌 때 작고 흐리게
                var st = _stars[i];
                st.enabled = true;
                st.transform.localPosition = new Vector3(center.x + Mathf.Cos(a) * 0.42f, center.y + depth * 0.1f, 0f);
                float size = 0.2f + 0.07f * depth;
                st.transform.localScale = new Vector3(size, size, 1f);
                st.transform.localRotation = Quaternion.Euler(0f, 0f, _starAngle * 0.7f + i * 40f);
                st.sortingOrder = depth > 0f ? 14 : 10;
                st.color = new Color(1f, 0.93f, 0.45f, 0.75f + 0.25f * depth);
            }
        }
    }
}
