using System.Collections.Generic;
using OnlineSeverGame.Client.Input;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.Game
{
    // 서버 없이 그래픽·UI·입력을 확인하는 오프라인 미리보기.
    // 물리와 판정은 대충 흉내만 낸다. 실제 판정은 서버, 예측은 Shared/ 물리 스텝을 쓴다.
    public sealed class PreviewSession : IGameSession
    {
        const float Gravity = -30f;
        const float MoveSpeed = 7f;
        const float JumpSpeed = 13f;
        const float DashSpeed = 18f;
        const float DashTime = 0.18f;
        const float DashCooldown = GameConstants.DashCooldownTicks * GameConstants.TickMilliseconds / 1000f; // 서버와 같은 2초
        const float CrouchSpeedFactor = GameConstants.CrouchMoveSpeed / (float)GameConstants.MoveSpeed;
        const float WarnTime = 1.0f;
        const float FallSpeed = 16f;
        const float LandLinger = 0.5f;

        sealed class Body
        {
            public int Id;
            public Vector2 Pos;
            public Vector2 Vel;
            public int Facing = 1;
            public float DashLeft;
            public float DashCool;      // 남은 대시 쿨다운(초). 예전 미리보기에는 없어서 연타하면 계속 대시됐다.
            public bool Crouching;
            public float DropLeft;      // ↓+점프로 발판을 통과하는 중
            public bool Alive = true;
            public bool Grounded;
            public float BotTurn;
            public float Stun;          // 밀려나는 중(그리기용)
        }

        sealed class Sword
        {
            public int Id;
            public float X;
            public float Y;
            public float Warn;
            public float Linger;
            public SwordPhase Phase;
        }

        readonly StageLayout _layout;
        readonly List<Body> _bodies = new List<Body>();
        readonly List<Sword> _swords = new List<Sword>();
        float _spawnTimer;
        int _nextSwordId = 1;

        public SessionState State { get; } = new SessionState();
        public WorldRender World { get; } = new WorldRender();

        public PreviewSession(StageLayout layout, string localName)
        {
            _layout = layout;
            State.LocalId = 0;
            string[] names = { localName, "봇-1", "봇-2", "봇-3", "봇-4", "봇-5", "봇-6", "봇-7" };
            for (int i = 0; i < names.Length; i++)
            {
                State.Roster.Add(new RosterEntry { Id = i, Name = names[i], ColorIndex = i, Alive = true, InRound = true, Connected = true });
            }
            EnterLobby();
        }

        void EnterLobby()
        {
            State.Screen = ClientScreen.Lobby;
            State.CountdownSeconds = 3f;
            State.RoundSeconds = 0f;
            State.RoundNumber++;
            State.WinnerIds.Clear();
            State.WinnerName = "";
            _swords.Clear();
            _bodies.Clear();
            for (int i = 0; i < State.Roster.Count; i++)
            {
                var r = State.Roster[i];
                r.Alive = true;
                r.SurvivedSeconds = 0;
                r.Place = 0;
                r.Survived = false;
                State.Roster[i] = r;
                float x = Mathf.Lerp(1.5f, _layout.Width - 1.5f, i / 7f);
                _bodies.Add(new Body { Id = r.Id, Pos = new Vector2(x, 0), Facing = x < 0 ? 1 : -1, BotTurn = Random.Range(0.5f, 2f) });
            }
        }

        public void Update(float dt, InputCollector input)
        {
            switch (State.Screen)
            {
                case ClientScreen.Lobby:
                    State.Screen = ClientScreen.Countdown;
                    goto case ClientScreen.Countdown;
                case ClientScreen.Countdown:
                    State.CountdownSeconds -= dt;
                    if (State.CountdownSeconds <= 0f)
                    {
                        State.Screen = ClientScreen.Playing;
                        input.ResetLatches();
                    }
                    break;
                case ClientScreen.Playing:
                case ClientScreen.Spectating:
                    Simulate(dt, input);
                    break;
                case ClientScreen.Result:
                    State.CountdownSeconds -= dt;
                    if (State.CountdownSeconds <= 0f) EnterLobby();
                    break;
            }
            Fill();
        }

        void Simulate(float dt, InputCollector input)
        {
            State.RoundSeconds += dt;
            var frame = input.Consume();

            foreach (var b in _bodies)
            {
                if (!b.Alive) continue;
                if (b.Stun > 0f) b.Stun -= dt;
                int move;
                bool jump, dash, down = false;
                if (b.Id == State.LocalId)
                {
                    move = frame.MoveX;
                    jump = frame.JumpPressed;
                    dash = frame.DashPressed;
                    down = frame.DownHeld;
                }
                else
                {
                    b.BotTurn -= dt;
                    if (b.BotTurn <= 0f) { b.Facing = Random.value < 0.5f ? -1 : 1; b.BotTurn = Random.Range(0.4f, 1.6f); }
                    move = b.Facing;
                    jump = Random.value < dt * 0.8f;
                    dash = Random.value < dt * 0.3f;
                }

                if (move != 0) b.Facing = move;
                b.DashCool -= dt;
                b.DropLeft -= dt;
                if (dash && b.DashLeft <= 0f && b.DashCool <= 0f)
                {
                    b.DashLeft = DashTime;
                    b.DashCool = DashCooldown;
                }
                b.Crouching = b.Grounded && down && b.DashLeft <= 0f;
                if (b.DashLeft > 0f)
                {
                    b.DashLeft -= dt;
                    b.Vel.x = b.Facing * DashSpeed;
                }
                else
                {
                    b.Vel.x = move * MoveSpeed * (b.Crouching ? CrouchSpeedFactor : 1f);
                }
                if (jump && b.Grounded)
                {
                    if (down && b.Pos.y > 0.01f) { b.DropLeft = 0.15f; b.Crouching = false; }
                    else b.Vel.y = JumpSpeed;
                }
                b.Vel.y += Gravity * dt;

                float prevY = b.Pos.y;
                b.Pos += b.Vel * dt;
                float minX = _layout.PlayerSize.x / 2f, maxX = _layout.Width - minX;
                if (b.Pos.x < minX) { b.Pos.x = minX; if (b.Id != State.LocalId) b.Facing = 1; }
                if (b.Pos.x > maxX) { b.Pos.x = maxX; if (b.Id != State.LocalId) b.Facing = -1; }

                b.Grounded = false;
                if (b.Vel.y <= 0f)
                {
                    foreach (var s in _layout.Solids)
                    {
                        if (b.DropLeft > 0f && s.TopY > 0f) continue;
                        bool overX = Mathf.Abs(b.Pos.x - s.CenterX) <= s.Width / 2f + _layout.PlayerSize.x / 2f - 0.05f;
                        if (overX && prevY >= s.TopY - 0.001f && b.Pos.y <= s.TopY)
                        {
                            b.Pos.y = s.TopY;
                            b.Vel.y = 0f;
                            b.Grounded = true;
                            break;
                        }
                    }
                }
            }

            // 대시 밀치기 흉내
            foreach (var a in _bodies)
            {
                if (!a.Alive || a.DashLeft <= 0f) continue;
                foreach (var o in _bodies)
                {
                    if (o == a || !o.Alive) continue;
                    if (Mathf.Abs(a.Pos.x - o.Pos.x) < _layout.PlayerSize.x && Mathf.Abs(a.Pos.y - o.Pos.y) < _layout.PlayerSize.y)
                    {
                        o.Pos.x += a.Facing * 1.5f;
                        o.Vel.y = 6f;
                        o.Stun = 0.3f;
                    }
                }
            }

            UpdateSwords(dt);

            int alive = 0;
            Body last = null;
            foreach (var b in _bodies) if (b.Alive) { alive++; last = b; }
            if (alive <= 1)
            {
                State.Screen = ClientScreen.Result;
                State.WinnerIds.Clear();
                if (last != null) State.WinnerIds.Add(last.Id);
                State.WinnerName = last != null && State.TryGet(last.Id, out var w) ? w.Name : "";
                State.CountdownSeconds = 5f;
                AssignPlaces();
            }
        }

        void UpdateSwords(float dt)
        {
            // 시간이 갈수록 자주 떨어진다.
            _spawnTimer -= dt;
            if (_spawnTimer <= 0f)
            {
                _spawnTimer = Mathf.Max(0.25f, 1.2f - State.RoundSeconds * 0.03f);
                _swords.Add(new Sword { Id = _nextSwordId++, X = Random.Range(0.5f, _layout.Width - 0.5f), Y = _layout.SwordSpawnY, Warn = WarnTime, Phase = SwordPhase.Warning });
            }

            for (int i = _swords.Count - 1; i >= 0; i--)
            {
                var s = _swords[i];
                if (s.Phase == SwordPhase.Warning)
                {
                    s.Warn -= dt;
                    if (s.Warn <= 0f) s.Phase = SwordPhase.Falling;
                }
                else if (s.Phase == SwordPhase.Falling)
                {
                    float prev = s.Y;
                    s.Y -= FallSpeed * dt;
                    foreach (var solid in _layout.Solids)
                    {
                        if (Mathf.Abs(s.X - solid.CenterX) <= solid.Width / 2f && prev >= solid.TopY && s.Y <= solid.TopY)
                        {
                            s.Y = solid.TopY;
                            s.Phase = SwordPhase.Landed;
                            s.Linger = LandLinger;
                            break;
                        }
                    }
                    foreach (var b in _bodies)
                    {
                        if (!b.Alive) continue;
                        bool hitX = Mathf.Abs(b.Pos.x - s.X) < (_layout.PlayerSize.x + _layout.SwordSize.x) / 2f;
                        bool hitY = s.Y <= b.Pos.y + _layout.PlayerSize.y && s.Y + _layout.SwordSize.y >= b.Pos.y;
                        if (hitX && hitY) Kill(b);
                    }
                }
                else
                {
                    s.Linger -= dt;
                    if (s.Linger <= 0f) _swords.RemoveAt(i);
                }
            }
        }

        void Kill(Body b)
        {
            b.Alive = false;
            b.Pos.y = _layout.SurfaceBelow(b.Pos.x, b.Pos.y);   // 웅덩이는 아래 면 위에
            for (int i = 0; i < State.Roster.Count; i++)
            {
                if (State.Roster[i].Id != b.Id) continue;
                var r = State.Roster[i];
                r.Alive = false;
                r.SurvivedSeconds = State.RoundSeconds;
                State.Roster[i] = r;
            }
            if (b.Id == State.LocalId) State.Screen = ClientScreen.Spectating;
        }

        void Fill()
        {
            World.Clear();
            foreach (var b in _bodies)
            {
                State.TryGet(b.Id, out var r);
                World.Players.Add(new PlayerRender
                {
                    Id = b.Id,
                    Name = r.Name,
                    ColorIndex = r.ColorIndex,
                    Position = b.Pos,
                    Alive = b.Alive,
                    Dashing = b.DashLeft > 0f,
                    Facing = b.Facing,
                    IsLocal = b.Id == State.LocalId,
                    Grounded = b.Grounded,
                    VelocityY = b.Vel.y,
                    Moving = Mathf.Abs(b.Vel.x) > 0.01f,
                    Stunned = b.Stun > 0f,
                    Crouching = b.Crouching && b.Grounded,
                    DashCooldown = b.Id == State.LocalId ? Mathf.Clamp01(b.DashCool / DashCooldown) : 0f,
                    Winner = State.Screen == ClientScreen.Result && State.WinnerIds.Contains(b.Id),
                });
            }
            foreach (var s in _swords)
            {
                World.Swords.Add(new SwordRender
                {
                    Id = s.Id,
                    Phase = s.Phase,
                    Position = new Vector2(s.X, s.Y),
                    WarningProgress = 1f - s.Warn / WarnTime,
                    LandingY = _layout.SwordLandingY(s.X),
                });
            }
        }

        void AssignPlaces()
        {
            // 승자 1위, 나머지는 오래 버틴 순
            for (int i = 0; i < State.Roster.Count; i++)
            {
                var r = State.Roster[i];
                if (State.WinnerIds.Contains(r.Id)) { r.Place = 1; r.Survived = r.Alive; r.SurvivedSeconds = State.RoundSeconds; State.Roster[i] = r; continue; }
                int place = 1 + State.WinnerIds.Count;
                foreach (var o in State.Roster)
                    if (!State.WinnerIds.Contains(o.Id) && o.SurvivedSeconds > r.SurvivedSeconds) place++;
                r.Place = place;
                State.Roster[i] = r;
            }
        }

        public void Close() { }
    }
}
