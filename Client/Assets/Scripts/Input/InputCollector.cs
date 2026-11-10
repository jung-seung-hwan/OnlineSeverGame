using OnlineSeverGame.Shared.Protocol;
using UnityEngine;

namespace OnlineSeverGame.Client.Input
{
    // 한 틱(50ms) 동안 모은 입력. 점프·대시는 누른 순간을 놓치지 않도록 다음 소비까지 유지한다.
    public struct InputFrame
    {
        public int MoveX;        // -1, 0, +1
        public bool JumpHeld;
        public bool JumpPressed;
        public bool DashPressed;
        public bool DownHeld;     // 누르고 있으면 엎드림, 이때 점프를 누르면 발판 아래로

        public override string ToString() =>
            $"move={MoveX} jumpHeld={JumpHeld} jump={JumpPressed} dash={DashPressed} down={DownHeld}";
    }

    // 매 프레임 키보드를 읽고, 틱마다 Consume() 으로 한 번에 가져간다.
    // 조작: ←/→ 또는 A/D 이동, ↑·W·Space 점프, Shift·J 대시, ↓·S 엎드리기(↓ + 점프 = 발판 아래로),
    // Z 인사·X 놀리기·C 기뻐하기·V 슬퍼하기 이모트(숫자 1~4와 R 은 방장의 맵 선택이 쓴다).
    public sealed class InputCollector : MonoBehaviour
    {
        bool _jumpLatch;
        bool _dashLatch;

        public bool Enabled { get; set; } = true;

        // 시험용: 키보드 대신 무작위로 움직이고 점프·대시한다(-autoplay).
        public bool AutoPlay { get; set; }
        // 시험용: 가장 가까운 상대에게 다가가 가까우면 대시한다(-autoplay chase). 상대 위치는 매 프레임 바깥에서 넣는다.
        public bool AutoChase { get; set; }
        public bool HasChaseTarget { get; set; }
        // 시험용: 사람이 대시 키를 연타하듯 0.1초마다 대시를 눌렀다 뗀다(-autoplay mash).
        public bool AutoMash { get; set; }
        // 시험용: 상대를 쫓다 가까우면 뛰어올라 머리를 밟으려 한다(-autoplay stomp). AutoChase 와 같이 켠다.
        public bool AutoStomp { get; set; }
        float _mashAt;
        // 시험용: 동작을 차례로 보여 준다(-autoplay motion): 대기 → 오른쪽·왼쪽 달리기(방향 전환) → 점프·착지 →
        // 엎드리기 → 이모트 4종 → 대시. 스크린샷으로 각 동작 그림을 확인하는 데 쓴다.
        public bool AutoMotion { get; set; }
        // 시험용: 점프 발판을 밀고 타기를 되풀이한다(-autoplay pad): 1.5초 옆면을 밀기 → 0.6초 물러나기 → 달려가 점프해
        // 윗면 가운데에 내려앉기(튀어 오름) → 2초 뒤 다시. 발판 위치는 GameBootstrap 이 ChaseDelta 로 넣는다.
        public bool AutoPad { get; set; }
        public bool PadGrounded { get; set; }
        public float PadHalfWidth { get; set; } = 0.6f;
        float _padPhaseAt;
        int _padPhase;
        float _motionAt;
        int _motionStep = -1;
        bool _autoDown;
        EmoteKind _emoteLatch;
        public Vector2 ChaseDelta { get; set; }
        readonly System.Random _rng = new System.Random();
        int _autoMove = 1;
        float _autoTurnAt;

        // 이번 프레임에 누른 이모트(없으면 0). 화면이 이모트를 받을 수 있을 때 GameBootstrap 이 가져간다.
        public EmoteKind TakeEmote()
        {
            var e = _emoteLatch;
            _emoteLatch = 0;
            if (e != 0 || AutoPlay) return e;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Z)) return EmoteKind.Wave;
            if (UnityEngine.Input.GetKeyDown(KeyCode.X)) return EmoteKind.Taunt;
            if (UnityEngine.Input.GetKeyDown(KeyCode.C)) return EmoteKind.Cheer;
            if (UnityEngine.Input.GetKeyDown(KeyCode.V)) return EmoteKind.Sad;
            return 0;
        }

        void UpdateMotionScript()
        {
            if (Time.time < _motionAt) return;
            _motionStep = (_motionStep + 1) % 12;
            _autoMove = 0;
            _autoDown = false;
            float hold = 1.6f;
            switch (_motionStep)
            {
                case 0: hold = 3f; break;                                   // 대기(눈 깜빡임)
                case 1: _autoMove = 1; hold = 1.2f; break;                  // 달리기
                case 2: _autoMove = -1; hold = 1.2f; break;                 // 방향 전환 + 달리기
                case 3: _jumpLatch = true; hold = 1.4f; break;              // 점프 → 낙하 → 착지
                case 4: _autoDown = true; hold = 2f; break;                 // 엎드리기
                case 5: _emoteLatch = EmoteKind.Wave; hold = 2f; break;
                case 6: _emoteLatch = EmoteKind.Taunt; hold = 2f; break;
                case 7: _emoteLatch = EmoteKind.Cheer; hold = 2f; break;
                case 8: _emoteLatch = EmoteKind.Sad; hold = 2f; break;
                case 9: _autoMove = 1; _dashLatch = true; hold = 0.8f; break;
                case 10: _autoMove = -1; _jumpLatch = true; hold = 1.2f; break;
                case 11: hold = 1f; break;
            }
            _motionAt = Time.time + hold;
        }

        void UpdatePadScript()
        {
            _autoMove = 0;
            if (!HasChaseTarget) return;
            float dx = ChaseDelta.x, dy = ChaseDelta.y;
            int toward = dx > 0 ? 1 : -1;
            if (Time.time >= _padPhaseAt)
            {
                _padPhase = (_padPhase + 1) % 3;
                _padPhaseAt = Time.time + (_padPhase == 0 ? 1.5f : _padPhase == 1 ? 0.6f : 4f);
            }
            switch (_padPhase)
            {
                case 0: _autoMove = toward; break;                          // 옆면으로 걸어가 민다
                case 1: _autoMove = -toward; break;                         // 물러나기
                default:                                                    // 달려가 점프해 가운데에 내려앉기
                    if (PadGrounded && dy > 0.2f)
                    {
                        _autoMove = toward;
                        if (Mathf.Abs(dx) < PadHalfWidth + 1.6f) _jumpLatch = true;
                    }
                    else if (!PadGrounded && dy < -0.1f) _autoMove = Mathf.Abs(dx) > 0.2f ? toward : 0;
                    break;
            }
        }

        void Update()
        {
            if (!Enabled) return;
            if (AutoPlay && AutoMotion)
            {
                UpdateMotionScript();
                return;
            }
            if (AutoPlay && AutoPad)
            {
                UpdatePadScript();
                return;
            }
            if (AutoPlay && AutoChase)
            {
                _autoMove = 0;
                if (HasChaseTarget)
                {
                    if (Mathf.Abs(ChaseDelta.x) > 0.5f) _autoMove = ChaseDelta.x > 0 ? 1 : -1;
                    if (AutoStomp)
                    {
                        if (Mathf.Abs(ChaseDelta.x) < 1.6f && Mathf.Abs(ChaseDelta.y) < 0.5f && _rng.NextDouble() < Time.deltaTime * 6) _jumpLatch = true;
                    }
                    else if (Mathf.Abs(ChaseDelta.x) < 3f && Mathf.Abs(ChaseDelta.y) < 0.8f && _rng.NextDouble() < Time.deltaTime * 4) _dashLatch = true;
                    if (ChaseDelta.y > 1.5f && _rng.NextDouble() < Time.deltaTime * 3) _jumpLatch = true;
                }
                return;
            }
            if (AutoPlay && AutoMash && Time.time >= _mashAt)
            {
                _dashLatch = true;
                _mashAt = Time.time + 0.1f;
            }
            if (AutoPlay)
            {
                if (Time.time >= _autoTurnAt)
                {
                    _autoMove = _rng.Next(3) - 1;
                    _autoTurnAt = Time.time + 0.4f + (float)_rng.NextDouble() * 1.2f;
                }
                if (_rng.NextDouble() < Time.deltaTime * 1.2) _jumpLatch = true;
                if (!AutoMash && _rng.NextDouble() < Time.deltaTime * 0.6) _dashLatch = true;
                return;
            }
            if (JumpDown()) _jumpLatch = true;
            if (DashDown()) _dashLatch = true;
        }

        public InputFrame Peek()
        {
            if (!Enabled) return default;
            if (AutoPlay)
                return new InputFrame { MoveX = _autoMove, JumpPressed = _jumpLatch, DashPressed = _dashLatch, DownHeld = _autoDown };
            int x = 0;
            if (UnityEngine.Input.GetKey(KeyCode.LeftArrow) || UnityEngine.Input.GetKey(KeyCode.A)) x -= 1;
            if (UnityEngine.Input.GetKey(KeyCode.RightArrow) || UnityEngine.Input.GetKey(KeyCode.D)) x += 1;
            return new InputFrame
            {
                MoveX = x,
                JumpHeld = UnityEngine.Input.GetKey(KeyCode.UpArrow) || UnityEngine.Input.GetKey(KeyCode.W) || UnityEngine.Input.GetKey(KeyCode.Space),
                JumpPressed = _jumpLatch || JumpDown(),
                DashPressed = _dashLatch || DashDown(),
                DownHeld = UnityEngine.Input.GetKey(KeyCode.DownArrow) || UnityEngine.Input.GetKey(KeyCode.S),
            };
        }

        public InputFrame Consume()
        {
            var f = Peek();
            _jumpLatch = false;
            _dashLatch = false;
            return f;
        }

        public void ResetLatches()
        {
            _jumpLatch = false;
            _dashLatch = false;
        }

        static bool JumpDown() =>
            UnityEngine.Input.GetKeyDown(KeyCode.UpArrow) || UnityEngine.Input.GetKeyDown(KeyCode.W) || UnityEngine.Input.GetKeyDown(KeyCode.Space);

        static bool DashDown() =>
            UnityEngine.Input.GetKeyDown(KeyCode.LeftShift) || UnityEngine.Input.GetKeyDown(KeyCode.RightShift) || UnityEngine.Input.GetKeyDown(KeyCode.J);
    }
}
