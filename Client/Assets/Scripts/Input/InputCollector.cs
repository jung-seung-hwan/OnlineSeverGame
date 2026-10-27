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
    // 조작: ←/→ 또는 A/D 이동, ↑·W·Space 점프, Shift·J 대시, ↓·S 엎드리기(↓ + 점프 = 발판 아래로).
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
        public Vector2 ChaseDelta { get; set; }
        readonly System.Random _rng = new System.Random();
        int _autoMove = 1;
        float _autoTurnAt;

        void Update()
        {
            if (!Enabled) return;
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
                return new InputFrame { MoveX = _autoMove, JumpPressed = _jumpLatch, DashPressed = _dashLatch };
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
