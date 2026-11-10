using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Shared.Protocol;
using UnityEngine;

namespace OnlineSeverGame.Client.View
{
    // 젤리 캐릭터 스프라이트(ArtSprites). 상태로 동작을 고르고 프레임을 넘긴다(docs/work/char-motion.md).
    // - 기본 동작: 관전·사망·승리·납작 기절·밀려남·대시·점프/낙하·엎드림·이모트·달리기·대기(우선순위 순).
    // - 덧동작: 대기·달리기 위에 잠깐 착지(찌그러짐)·방향 전환·눈 깜빡임을 덮어 그린다.
    // - 출렁임: 그림 프레임과 따로, 몸이 이동·착지·밀려남을 뒤늦게 따라 흔들리게 스프링으로 늘였다 줄이고 기울인다.
    //   그리기만 바뀌고 판정·예측과는 상관없다(판정은 Shared/Simulation/HitShapes.cs).
    // 대시 잔상을 가진다(내 캐릭터 표시는 UI 이름표의 ▼). 시트는 오른쪽을 보므로 왼쪽은 뒤집는다.
    // 엎드림·납작 기절 전용 행이 없는 예전 시트면 예전처럼 발밑을 기준으로 세로로 눌러 그린다.
    public sealed class PlayerView : MonoBehaviour
    {
        static readonly Vector2 CrouchScale = new Vector2(1.15f, 0.6f);   // 판정 높이 60%에 맞춤(전용 행이 없을 때만)
        static readonly Vector2 StompedScale = new Vector2(1.35f, 0.4f);
        const float SquashSpeed = 18f;                                    // 클수록 빨리 눌리고 빨리 펴진다

        // 출렁임 스프링. 감쇠가 약해서 두세 번 출렁이다 멈춘다(약 0.5초).
        const float Stiffness = 220f;
        const float Damping = 9f;
        const float MaxStretch = 0.3f;     // 세로 늘임·눌림 최대 30%
        const float MaxLean = 14f;         // 기울기 최대(도)
        const float LeanPerSpeed = 16f;    // 가로 속도 변화 1 유닛/초당 기울기 속도(도/초)

        Vector2 _scale = Vector2.one;
        Transform _jiggle;                 // 몸·잔상을 담고 출렁임 배율·기울기를 받는다(원점 = 발밑)
        SpriteRenderer _body;
        SpriteRenderer _trail;
        string _anim;
        float _animTime;

        // 덧동작(land / turn / blink)
        string _overlay;
        float _overlayTime;
        int _turnFrom;
        float _blinkAt;

        // 지난 프레임 상태(출렁임·덧동작 계기)
        bool _hasLast;
        Vector2 _lastPos;
        float _vx;
        int _lastFacing;
        bool _wasGrounded;
        float _lastVy;
        bool _wasDashing, _wasStunned, _wasCrouching, _wasStomped;

        float _stretch, _stretchVel;       // + 세로로 늘어남, − 눌림
        float _lean, _leanVel;             // 도, + 는 위가 왼쪽으로

        public PlayerRender State { get; private set; }

        public static PlayerView Create(Transform parent, Vector2 size)
        {
            var go = new GameObject("Player");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<PlayerView>();
            v._jiggle = new GameObject("Jiggle").transform;
            v._jiggle.SetParent(go.transform, false);
            v._trail = NewRenderer(v._jiggle, "Trail", 9);
            v._body = NewRenderer(v._jiggle, "Body", 11);
            v._blinkAt = Time.time + Random.Range(1.5f, 4f);
            return v;
        }

        static SpriteRenderer NewRenderer(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            return sr;
        }

        public static string EmoteAnimation(EmoteKind e)
        {
            switch (e)
            {
                case EmoteKind.Wave: return "emote_wave";
                case EmoteKind.Taunt: return "emote_taunt";
                case EmoteKind.Cheer: return "victory";
                case EmoteKind.Sad: return "emote_sad";
                default: return null;
            }
        }

        static string PickAnimation(PlayerRender s)
        {
            if (s.Spectator) return "idle";
            if (!s.Alive) return "death";
            if (s.Winner) return "victory";
            if (s.Stomped) return ArtSprites.HasCharacter("stun") ? "stun" : "knockback";
            if (s.Stunned) return "knockback";
            if (s.Dashing) return "dash";
            if (!s.Grounded) return s.VelocityY > 0f ? "jump" : "fall";
            if (s.Crouching && ArtSprites.HasCharacter("crouch")) return "crouch";
            if (s.Emote != 0 && !s.Moving && !s.Crouching)
            {
                string e = EmoteAnimation(s.Emote);
                if (e != null && ArtSprites.HasCharacter(e)) return e;
            }
            return s.Moving ? "run" : "idle";
        }

        // 기절 면역 중 깜빡임: 2틱(0.1초)마다 진하게/흐리게. 위치는 보여야 하므로 완전히 지우지 않는다.
        public static float BlinkAlpha(int immuneTicks) =>
            immuneTicks <= 0 || (immuneTicks / 2) % 2 == 0 ? 1f : 0.3f;

        public void Apply(PlayerRender s)
        {
            State = s;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            transform.localPosition = new Vector3(s.Position.x, s.Position.y, 0);
            int facing = s.Facing < 0 ? -1 : 1;

            string anim = PickAnimation(s);
            if (anim != _anim)
            {
                _anim = anim;
                _animTime = 0f;
            }
            else
            {
                _animTime += Time.deltaTime;
            }

            bool alive = s.Alive && !s.Spectator;
            UpdateOverlay(s, anim, facing, alive, dt);
            UpdateWobble(s, anim, alive, dt);

            // 그릴 프레임과 좌우
            Sprite sprite;
            bool flip = facing < 0;
            var clip = ArtSprites.Character(s.ColorIndex, anim);
            if (_overlay == "turn")
            {
                // 옛 방향으로 몸을 카메라 쪽으로 돌림(0, 1) → 새 방향에서 다시 옆으로(0)
                var turn = ArtSprites.Character(s.ColorIndex, "turn");
                int step = Mathf.Min(2, Mathf.FloorToInt(_overlayTime * turn.Fps));
                sprite = turn.Frames[step == 1 ? Mathf.Min(1, turn.Frames.Length - 1) : 0];
                flip = (step < 2 ? _turnFrom : facing) < 0;
            }
            else if (_overlay != null)
            {
                sprite = ArtSprites.Character(s.ColorIndex, _overlay).At(_overlayTime);
            }
            else
            {
                sprite = clip.At(_animTime);
            }

            // 예전 시트(전용 행 없음)면 눌러 그리기, 새 시트면 행마다 정한 배율(납작 기절은 x 1.1)
            Vector2 target = clip.DrawScale;
            if (alive && s.Stomped && anim == "knockback") target = StompedScale;
            else if (alive && s.Crouching && anim != "crouch" && s.Grounded && !s.Dashing && !s.Stunned) target = CrouchScale;
            _scale = Vector2.Lerp(_scale, target, 1f - Mathf.Exp(-SquashSpeed * Time.deltaTime));
            transform.localScale = new Vector3(_scale.x, _scale.y, 1f);

            float st = Mathf.Clamp(_stretch, -MaxStretch, MaxStretch);
            _jiggle.localScale = new Vector3(1f - 0.5f * st, 1f + st, 1f);
            _jiggle.localRotation = Quaternion.Euler(0f, 0f, Mathf.Clamp(_lean, -MaxLean, MaxLean));

            _body.sprite = sprite;
            _body.flipX = flip;
            // 내 캐릭터를 다른 캐릭터 위에 그린다. 도중 입장 관전자는 흐리게, 죽은 웅덩이는 조금 어둡게.
            _body.sortingOrder = s.IsLocal ? 12 : 11;
            if (s.Spectator) _body.color = new Color(1f, 1f, 1f, 0.35f);
            else if (!s.Alive) _body.color = new Color(0.8f, 0.8f, 0.8f, 0.9f);
            else _body.color = new Color(1f, 1f, 1f, BlinkAlpha(s.ImmuneTicks));

            _trail.enabled = s.Alive && s.Dashing;
            _trail.sprite = sprite;
            _trail.flipX = flip;
            _trail.color = new Color(1f, 1f, 1f, 0.35f);
            _trail.transform.localPosition = new Vector3(-facing * 0.45f, 0, 0);

            _hasLast = true;
            _lastPos = s.Position;
            _lastFacing = facing;
            _wasGrounded = s.Grounded;
            _lastVy = s.VelocityY;
            _wasDashing = s.Dashing;
            _wasStunned = s.Stunned;
            _wasCrouching = s.Crouching;
            _wasStomped = s.Stomped;
        }

        // 착지·방향 전환·깜빡임은 대기·달리기 위에만 덮는다. 다른 동작이 되면 바로 끝난다.
        void UpdateOverlay(PlayerRender s, string anim, int facing, bool alive, float dt)
        {
            bool calm = alive && (anim == "idle" || anim == "run");
            if (_overlay != null)
            {
                _overlayTime += dt;
                bool done = !calm || _overlayTime >= OverlayDuration(s.ColorIndex, _overlay);
                if (done) _overlay = null;
            }
            if (!calm || !_hasLast) return;

            if (facing != _lastFacing && ArtSprites.HasCharacter("turn"))
                BeginOverlay("turn", _lastFacing);
            else if (!_wasGrounded && s.Grounded && _lastVy < -2f && ArtSprites.HasCharacter("land"))
                BeginOverlay("land", 0);
            else if (_overlay == null && anim == "idle" && Time.time >= _blinkAt && ArtSprites.HasCharacter("blink"))
                BeginOverlay("blink", 0);
            if (Time.time >= _blinkAt) _blinkAt = Time.time + Random.Range(2f, 5f);
        }

        void BeginOverlay(string overlay, int turnFrom)
        {
            _overlay = overlay;
            _overlayTime = 0f;
            _turnFrom = turnFrom;
        }

        static float OverlayDuration(int colorIndex, string overlay)
        {
            var c = ArtSprites.Character(colorIndex, overlay);
            // 방향 전환은 2프레임을 3단계(옛 방향 0·1 → 새 방향 0)로 쓴다
            return overlay == "turn" ? 3f / c.Fps : c.Duration;
        }

        // 몸이 움직임을 뒤늦게 따라오는 출렁임. 스냅샷·예측 값의 변화만 보고 그림에만 쓴다.
        void UpdateWobble(PlayerRender s, string anim, bool alive, float dt)
        {
            if (!alive || !_hasLast)
            {
                _stretch = _stretchVel = _lean = _leanVel = 0f;
                _vx = 0f;
                return;
            }

            // 가로 속도(위치 차이, 순간이동·큰 보정은 무시하고 살짝 고름)
            Vector2 d = s.Position - _lastPos;
            if (d.sqrMagnitude < 4f)
            {
                float vx = d.x / dt;
                float smoothed = Mathf.Lerp(_vx, vx, 1f - Mathf.Exp(-25f * dt));
                float dv = Mathf.Clamp(smoothed - _vx, -12f, 12f);
                _vx = smoothed;
                // 가로로 빨라지면 위가 뒤로 처지고, 멈추면 앞으로 쏠린다. 대시·기절 그림은 이미 기울어 있어 약하게.
                float k = s.Dashing || s.Stunned ? 0.3f : 1f;
                if (s.Grounded || anim == "jump" || anim == "fall") _leanVel += LeanPerSpeed * dv * k;
            }

            // 계기별 튕김
            if (!_wasGrounded && s.Grounded)
                _stretchVel -= Mathf.Clamp(-_lastVy * 0.18f, 0.6f, 3f);        // 착지: 눌렸다 튀어 오름
            if (_wasGrounded && !s.Grounded && s.VelocityY > 0f && !s.Stunned)
                _stretchVel += 2.5f;                                            // 점프: 길게 늘어남
            if (s.Dashing && !_wasDashing) _stretchVel -= 1.8f;
            if (s.Stunned && !_wasStunned)
            {
                _stretchVel -= 3.5f;
                _leanVel += (_vx >= 0f ? 1f : -1f) * 90f;
            }
            if (s.Stomped && !_wasStomped) _stretchVel -= 3f;
            if (s.Crouching != _wasCrouching && s.Grounded) _stretchVel += s.Crouching ? -2.2f : 2.2f;

            // 반 암시적 오일러(작은 dt 여러 번으로 나눠 프레임이 길어도 튀지 않게)
            int n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 8);
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                _stretchVel += (-Stiffness * _stretch - Damping * _stretchVel) * h;
                _stretch += _stretchVel * h;
                _leanVel += (-Stiffness * _lean - Damping * _leanVel) * h;
                _lean += _leanVel * h;
            }
            if (Mathf.Abs(_stretch) > 1f || float.IsNaN(_stretch)) _stretch = _stretchVel = 0f;
            if (Mathf.Abs(_lean) > 45f || float.IsNaN(_lean)) _lean = _leanVel = 0f;
        }
    }
}
