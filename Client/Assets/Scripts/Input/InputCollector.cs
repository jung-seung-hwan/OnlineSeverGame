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

        public override string ToString() =>
            $"move={MoveX} jumpHeld={JumpHeld} jump={JumpPressed} dash={DashPressed}";
    }

    // 매 프레임 키보드를 읽고, 틱마다 Consume() 으로 한 번에 가져간다.
    // 조작: ←/→ 또는 A/D 이동, ↑·W·Space 점프, Shift·J 대시.
    public sealed class InputCollector : MonoBehaviour
    {
        bool _jumpLatch;
        bool _dashLatch;

        public bool Enabled { get; set; } = true;

        void Update()
        {
            if (!Enabled) return;
            if (JumpDown()) _jumpLatch = true;
            if (DashDown()) _dashLatch = true;
        }

        public InputFrame Peek()
        {
            if (!Enabled) return default;
            int x = 0;
            if (UnityEngine.Input.GetKey(KeyCode.LeftArrow) || UnityEngine.Input.GetKey(KeyCode.A)) x -= 1;
            if (UnityEngine.Input.GetKey(KeyCode.RightArrow) || UnityEngine.Input.GetKey(KeyCode.D)) x += 1;
            return new InputFrame
            {
                MoveX = x,
                JumpHeld = UnityEngine.Input.GetKey(KeyCode.UpArrow) || UnityEngine.Input.GetKey(KeyCode.W) || UnityEngine.Input.GetKey(KeyCode.Space),
                JumpPressed = _jumpLatch || JumpDown(),
                DashPressed = _dashLatch || DashDown(),
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
