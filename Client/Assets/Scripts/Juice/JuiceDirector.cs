using System;
using System.Collections.Generic;
using OnlineSeverGame.Client.Game;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Client.View;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.Juice
{
    // 타격감 연출 총괄(docs/work/juice-sound.md 2장). 서버·프로토콜·예측은 건드리지 않는다.
    //
    // 1) 보여 주는 시계: 세션이 매 프레임 만든 WorldRender 를 복사해 두고, '보여 주는 시각'의 것을 골라(사이는 위치만 보간)
    //    같은 WorldRender 에 덮어쓴다. 평소에는 보여 주는 시각 = 지금이라 아무것도 바뀌지 않는다.
    //    대시 충돌 멈춤(0.05초) 동안은 시계를 세우고, 마지막 사망 슬로모션 동안은 0.25배로 흘린 뒤 2.2배로 따라잡는다.
    //    세션(입력·예측·보정)은 실제 시간으로 계속 돈다(GameBootstrap 이 unscaledDeltaTime 을 넘긴다). 화면 표시만 늦는다.
    //    Time.timeScale 도 같이 바꿔 캐릭터 동작·장식·파티클이 같이 멈추고 느려진다.
    // 2) 사건 찾기: 보여 주는 WorldRender 를 지난 프레임과 비교해 점프·착지·대시·대시 충돌·밟기·엎드림·기절·면역·사망,
    //    위험물 예고·꽂힘·옆 위험물 예고·지나감, 카운트다운·시작·승리·패배를 찾아 소리·파티클·흔들림을 낸다.
    //    소리와 그림이 같은 시각에 맞는다(보여 주는 것 기준이므로).
    // 3) 카메라: GameBootstrap.FitCamera 가 정한 화면 위에 LateUpdate 로 덧붙인다. 생존자 둘이면 0.9배로 당겨 둘 사이를 보고,
    //    마지막 사망 슬로모션 동안 0.84배로 사망 자리를 본다. 당겨도 원래 화면 밖은 보이지 않게 한다. 흔들림은 설정으로 끌 수 있다.
    // 실행 인자: -juicelog (사건마다 로그), -juiceshot <경로{n}> (사건 직후 스크린샷, 종류별 2장·최대 30장)
    public sealed class JuiceDirector : MonoBehaviour
    {
        const float HitstopSeconds = 0.05f;
        const float HitstopCooldown = 0.3f;
        const float SlowRate = 0.25f;
        const float SlowSeconds = 0.9f;
        const float CatchUpRate = 2.2f;
        const float MaxLag = 1.0f;
        const float LastTwoZoom = 0.9f;
        const float SlowZoom = 0.84f;

        GameBootstrap _game;
        WorldView _view;
        JuiceAudio _audio;
        JuiceParticles _particles;

        // ------------------------------------------------ 보여 주는 시계
        sealed class Frame
        {
            public double Time;
            public readonly WorldRender World = new WorldRender();
        }

        readonly List<Frame> _history = new List<Frame>();
        readonly Stack<Frame> _free = new Stack<Frame>();
        double _real, _display;
        float _hitstopLeft;
        double _hitstopReadyAt;
        float _slowLeft;
        bool _focusDeath;
        Vector2 _deathFocus;

        public double Lag => _real - _display;

        // ------------------------------------------------ 사건 찾기
        IGameSession _session;
        int _layoutVersion = -1;
        bool _baseline;
        Dictionary<int, PlayerRender> _prevPlayers = new Dictionary<int, PlayerRender>();
        Dictionary<int, PlayerRender> _curPlayers = new Dictionary<int, PlayerRender>();
        Dictionary<int, SwordRender> _prevSwords = new Dictionary<int, SwordRender>();
        Dictionary<int, SwordRender> _curSwords = new Dictionary<int, SwordRender>();
        Dictionary<int, SideKnifeRender> _prevSide = new Dictionary<int, SideKnifeRender>();
        Dictionary<int, SideKnifeRender> _curSide = new Dictionary<int, SideKnifeRender>();
        readonly Dictionary<int, double> _lastCrouch = new Dictionary<int, double>();
        readonly List<PlayerRender> _deaths = new List<PlayerRender>();
        readonly Dictionary<int, Vector2> _dead = new Dictionary<int, Vector2>(); // 이번 라운드에 죽은 캐릭터와 죽은 자리
        ClientScreen _lastScreen = ClientScreen.Connect;
        ClientScreen _heldScreen = ClientScreen.Playing;
        bool _rawResult;
        double _resultHoldUntil;
        int _lastCount = -1;
        bool _lastTwo;
        Vector2 _lastTwoMid;

        // 한 프레임에 같은 종류가 여러 개면 가장 큰 것 하나만 낸다(칼 4개가 같이 떨어질 때 등)
        readonly float[] _frameVol = new float[Enum.GetValues(typeof(Sfx)).Length];
        readonly float[] _framePan = new float[Enum.GetValues(typeof(Sfx)).Length];
        readonly bool[] _frameLocal = new bool[Enum.GetValues(typeof(Sfx)).Length];

        // ------------------------------------------------ 카메라
        float _zoom = 1f;
        Vector2 _focus;
        bool _focusInit;
        float _shake;
        float _shakeSeed;

        // ------------------------------------------------ 시험
        bool _log;
        string _shotPath;
        int _shotCount;
        readonly Dictionary<string, int> _shotsByTag = new Dictionary<string, int>();
        readonly List<KeyValuePair<double, string>> _pendingShots = new List<KeyValuePair<double, string>>();
        int _hitstops, _slowmos, _shakes, _lastTwoCount, _maxVoices;
        double _statsAt = 10;

        public void Init(GameBootstrap game, WorldView view)
        {
            _game = game;
            _view = view;
            _audio = JuiceAudio.Create(gameObject);
            _particles = new GameObject("JuiceParticles").AddComponent<JuiceParticles>();
            JuiceSettings.Register(gameObject); // 설정 창 소리 칸 + 버튼 클릭 소리(UiHooks)
            _shakeSeed = UnityEngine.Random.value * 100f;

            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-juicelog") _log = true;
                if (args[i] == "-juiceshot" && i + 1 < args.Length) _shotPath = args[i + 1];
            }
        }

        // GameBootstrap.Update 가 Session.Update 뒤, WorldView.Apply 앞에 부른다.
        public void Present(IGameSession session)
        {
            float dt = Time.unscaledDeltaTime;
            if (session != _session || _game.Layout.Version != _layoutVersion) ResetState(session);

            _real += dt;
            Record(session.World);

            float rate;
            if (_hitstopLeft > 0f) { _hitstopLeft -= dt; rate = 0f; }
            else if (_slowLeft > 0f) { _slowLeft -= dt; rate = SlowRate; }
            else rate = Lag > 1e-4 ? CatchUpRate : 1f;
            _display = Math.Min(_real, _display + dt * rate);
            if (_real - _display > MaxLag) _display = _real - MaxLag;
            if (_slowLeft <= 0f && Lag < 1e-4) _focusDeath = false;

            Time.timeScale = _hitstopLeft > 0f ? 0f : _slowLeft > 0f ? SlowRate : 1f;
            _audio.BgmPitch = Mathf.MoveTowards(_audio.BgmPitch, _slowLeft > 0f ? 0.84f : 1f, dt * 1.5f);

            if (Lag > 1e-4) Sample(session.World, _display);
            Prune();
            HoldResultScreen(session);
            Detect(session);
        }

        void ResetState(IGameSession session)
        {
            _session = session;
            _layoutVersion = _game.Layout.Version;
            foreach (var f in _history) _free.Push(f);
            _history.Clear();
            _display = _real;
            _hitstopLeft = _slowLeft = 0f;
            _focusDeath = false;
            Time.timeScale = 1f;
            _prevPlayers.Clear(); _prevSwords.Clear(); _prevSide.Clear();
            _baseline = true;
            _lastTwo = false;
            _dead.Clear();
            _particles.Clear();
        }

        // ------------------------------------------------ 보여 주는 시계

        void Record(WorldRender w)
        {
            var f = _free.Count > 0 ? _free.Pop() : new Frame();
            f.Time = _real;
            f.World.Clear();
            f.World.Players.AddRange(w.Players);
            f.World.Swords.AddRange(w.Swords);
            f.World.Boxes.AddRange(w.Boxes);
            f.World.SideKnives.AddRange(w.SideKnives);
            _history.Add(f);
        }

        void Prune()
        {
            while (_history.Count >= 2 && _history[1].Time <= _display)
            {
                _free.Push(_history[0]);
                _history.RemoveAt(0);
            }
        }

        // t 시각의 그림을 w 에 덮어쓴다. 상태는 앞 프레임 것, 위치만 다음 프레임과 보간.
        void Sample(WorldRender w, double t)
        {
            int i = 0;
            while (i + 1 < _history.Count && _history[i + 1].Time <= t) i++;
            var a = _history[i].World;
            var bf = i + 1 < _history.Count ? _history[i + 1] : null;
            float u = bf == null ? 0f : (float)((t - _history[i].Time) / Math.Max(1e-6, bf.Time - _history[i].Time));
            var b = bf?.World;

            w.Clear();
            foreach (var p in a.Players)
            {
                var q = p;
                if (b != null)
                    foreach (var n in b.Players)
                        if (n.Id == p.Id && n.Alive == p.Alive) { q.Position = Vector2.Lerp(p.Position, n.Position, u); break; }
                w.Players.Add(q);
            }
            foreach (var s in a.Swords)
            {
                var q = s;
                if (b != null)
                    foreach (var n in b.Swords)
                        if (n.Id == s.Id && n.Phase == s.Phase) { q.Position = Vector2.Lerp(s.Position, n.Position, u); break; }
                w.Swords.Add(q);
            }
            foreach (var k in a.SideKnives)
            {
                var q = k;
                if (b != null)
                    foreach (var n in b.SideKnives)
                        if (n.Id == k.Id && n.Warning == k.Warning) { q.TipX = Mathf.Lerp(k.TipX, n.TipX, u); break; }
                w.SideKnives.Add(q);
            }
            foreach (var box in a.Boxes)
            {
                var q = box;
                if (b != null)
                    foreach (var n in b.Boxes)
                        if (n.Id == box.Id) { q.Position = Vector2.Lerp(box.Position, n.Position, u); break; }
                w.Boxes.Add(q);
            }
        }

        // 결과 화면은 라운드 끝 패킷(최신 스냅샷) 기준이라, 보간으로 100 ms 늦게 그리는 마지막 사망보다 먼저 온다.
        // 그래서 결과가 처음 오면 0.3초 기다려 마지막 사망(슬로모션)이 먼저 보이게 하고,
        // 슬로모션·따라잡기 중에는 결과 화면(가운데 패널)을 계속 미뤄 마지막 사망이 보이게 한다(네트워크 세션만:
        // 세션이 매 프레임 서버 상태로 화면을 다시 정하므로 다음 프레임에 그대로 돌아온다. 미리보기는 화면 값이 자기 상태라 건드리지 않는다).
        void HoldResultScreen(IGameSession session)
        {
            var st = session.State;
            if (st.Screen == ClientScreen.Playing || st.Screen == ClientScreen.Spectating) _heldScreen = st.Screen;
            bool result = st.Screen == ClientScreen.Result;
            if (result && !_rawResult) _resultHoldUntil = _real + 0.3;
            _rawResult = result;
            if (session is NetworkSession && result && (_real < _resultHoldUntil || _slowLeft > 0f || (_focusDeath && Lag > 1e-4)))
                st.Screen = _heldScreen;
        }

        public void Hitstop()
        {
            if (_real < _hitstopReadyAt || _slowLeft > 0f) return;
            _hitstopLeft = HitstopSeconds;
            _hitstopReadyAt = _real + HitstopCooldown;
            _hitstops++;
        }

        void StartSlowMotion(Vector2 focus)
        {
            _slowLeft = SlowSeconds;
            _hitstopLeft = 0f;
            _focusDeath = true;
            _deathFocus = focus;
            _slowmos++;
            Shot("final", 0.3f);
            Shot("final", 0.75f);
        }

        public void AddShake(float amplitude)
        {
            if (!JuiceAudio.ScreenShake) return;
            _shake = Mathf.Max(_shake, amplitude);
            _shakes++;
        }

        // ------------------------------------------------ 사건 찾기

        void Detect(IGameSession session)
        {
            var world = session.World;
            var state = session.State;
            Array.Clear(_frameVol, 0, _frameVol.Length);

            // 내 캐릭터가 죽으면 예측 그림이 보간 그림(100 ms 과거)으로 바뀌어 0.1초쯤 다시 살아 보인다(NetworkSession 그리기).
            // 라운드 안에서 되살아나는 일은 없으므로, 죽은 캐릭터는 대기·카운트다운 전까지 죽은 자리에 웅덩이로 붙잡아 둔다
            // (화면 표시만. 사망 소리·방울이 두 번 나지 않게).
            if (state.Screen == ClientScreen.Lobby || state.Screen == ClientScreen.Countdown) _dead.Clear();
            for (int i = 0; i < world.Players.Count; i++)
            {
                var p = world.Players[i];
                if (p.Spectator || !_dead.TryGetValue(p.Id, out var at)) continue;
                if (p.Alive)
                {
                    p.Alive = false;
                    p.Dashing = p.Crouching = p.Stomped = p.Stunned = false;
                    p.ImmuneTicks = 0;
                    p.Position = at;
                    world.Players[i] = p;
                }
                else _dead[p.Id] = p.Position;
            }

            bool hasListener = false;
            Vector2 listener = (Vector2)_game.WorldCamera.transform.position;
            foreach (var p in world.Players)
                if (p.IsLocal && p.Alive) { listener = p.Position; hasListener = true; }

            int aliveBefore = 0, aliveNow = 0, participants = 0;
            foreach (var q in _prevPlayers.Values) if (q.Alive && !q.Spectator) aliveBefore++;
            _curPlayers.Clear();
            _deaths.Clear();
            var mid = Vector2.zero;
            foreach (var p in world.Players)
            {
                _curPlayers[p.Id] = p;
                if (!p.Spectator) participants++;
                if (p.Alive && !p.Spectator) { aliveNow++; mid += p.Position; }
                if (_baseline || !_prevPlayers.TryGetValue(p.Id, out var q) || p.Spectator) continue;
                PlayerEvents(p, q, world);
            }

            var screen = state.Screen;
            bool inRound = screen == ClientScreen.Playing || screen == ClientScreen.Spectating || screen == ClientScreen.Result;
            bool final = _deaths.Count > 0 && aliveBefore >= 2 && aliveNow <= 1 && inRound;
            foreach (var d in _deaths)
            {
                Vector2 at = d.Position;
                Queue(Sfx.Death, d.IsLocal ? 1f : 0.9f, Pan(at.x), d.IsLocal || final);
                _particles.Droplets(at, PlayerColors.Get(d.ColorIndex), final);
                if (d.IsLocal) AddShake(0.12f);
                LogEvent($"death #{d.Id}{(final ? " (final)" : "")}");
                Shot("death", 0.12f);
            }
            if (final)
            {
                StartSlowMotion(_deaths[0].Position + new Vector2(0f, 0.6f));
                LogEvent("slowmo");
            }

            bool lastTwo = (screen == ClientScreen.Playing || screen == ClientScreen.Spectating) && aliveNow == 2 && participants >= 3;
            if (lastTwo)
            {
                _lastTwoMid = mid / 2f + new Vector2(0f, 0.6f);
                if (!_lastTwo) { _lastTwoCount++; LogEvent("last two"); Shot("lasttwo", 1.2f); }
            }
            _lastTwo = lastTwo;
            Swap(ref _prevPlayers, ref _curPlayers);

            // 위에서 떨어지는 위험물
            _curSwords.Clear();
            foreach (var s in world.Swords)
            {
                _curSwords[s.Id] = s;
                if (_baseline) continue;
                float dx = Mathf.Abs(s.Position.x - listener.x);
                float near = Mathf.Lerp(1f, 0.25f, Mathf.Clamp01((dx - 1f) / 8f));
                if (!_prevSwords.TryGetValue(s.Id, out var q))
                {
                    if (s.Phase == SwordPhase.Warning) Queue(Sfx.Warn, near, Pan(s.Position.x), dx < 3f);
                    continue;
                }
                if (q.Phase != SwordPhase.Landed && s.Phase == SwordPhase.Landed) HazardLanded(s, listener, hasListener, near);
            }
            Swap(ref _prevSwords, ref _curSwords);

            // 옆 위험물
            _curSide.Clear();
            foreach (var k in world.SideKnives)
            {
                _curSide[k.Id] = k;
                if (_baseline) continue;
                float dy = Mathf.Abs(k.LaneY - (listener.y + 0.6f));
                float near = Mathf.Lerp(1f, 0.3f, Mathf.Clamp01((dy - 0.6f) / 4f));
                if (!_prevSide.TryGetValue(k.Id, out var q))
                {
                    if (k.Warning) { Queue(Sfx.SideWarn, near, k.Dir > 0 ? -0.6f : 0.6f, dy < 1.5f); LogEvent($"side warn lane={k.LaneY:0.0}"); }
                    continue;
                }
                if (!k.Warning && !q.Warning && Mathf.Sign(q.TipX - listener.x) != Mathf.Sign(k.TipX - listener.x))
                {
                    Queue(Sfx.SidePass, near, k.Dir * 0.5f, dy < 1.5f);
                    LogEvent($"side pass lane={k.LaneY:0.0} near={near:0.00}");
                    if (dy < 1.5f) Shot("sidepass", 0.0f);
                }
            }
            Swap(ref _prevSide, ref _curSide);

            ScreenEvents(state);
            Flush();
            _baseline = false;
        }

        void PlayerEvents(PlayerRender p, PlayerRender q, WorldRender world)
        {
            if (q.Alive && !p.Alive) { _deaths.Add(p); _dead[p.Id] = p.Position; return; }
            if (!p.Alive || !q.Alive) return;
            if ((p.Position - q.Position).sqrMagnitude > 6.25f) return; // 순간이동(라운드 시작 배치 등)

            bool local = p.IsLocal;
            float pan = Pan(p.Position.x);
            Vector2 feet = p.Position;

            if (q.Grounded && !p.Grounded && p.VelocityY > 5f && !p.Stunned)
            {
                Queue(Sfx.Jump, 1f, pan, local);
                _particles.Dust(feet, 3, 0.6f);
            }
            if (!q.Grounded && !p.Grounded && q.VelocityY <= 1f && p.VelocityY > 8f && !p.Stunned)
            {
                Queue(Sfx.Stomp, 1f, pan, local);
                _particles.Impact(feet + new Vector2(0f, 0.05f), new Color(1f, 1f, 1f), 1.3f, 5);
                LogEvent($"stomp by #{p.Id}");
                Shot("stomp", 0.05f);
            }
            if (!q.Grounded && p.Grounded)
            {
                float impact = Mathf.Clamp01(-q.VelocityY / 20f);
                Queue(Sfx.Land, 0.35f + 0.65f * impact, pan, local);
                if (impact > 0.3f) _particles.Dust(feet, 2 + Mathf.RoundToInt(impact * 4f), 0.55f + impact * 0.6f);
            }
            if (!q.Dashing && p.Dashing)
            {
                Queue(Sfx.Dash, 1f, pan, local);
                _particles.Dust(feet, 4, 0.9f, -p.Facing);
            }
            if (!q.Stunned && p.Stunned && (p.Moving || (!p.Grounded && p.VelocityY > 0f)))
                DashHit(p, world);
            if (!q.Stomped && p.Stomped)
            {
                Queue(Sfx.Stun, 1f, pan, local);
                LogEvent($"stun #{p.Id}");
                Shot("stun", 0.25f);
            }
            if (q.ImmuneTicks == 0 && p.ImmuneTicks > 0) Queue(Sfx.Immune, 0.8f, pan, local);
            if (!q.Crouching && p.Crouching)
            {
                _lastCrouch.TryGetValue(p.Id, out double last);
                if (_real - last > 0.15)
                {
                    Queue(Sfx.Crouch, 1f, pan, local);
                    _lastCrouch[p.Id] = _real;
                }
            }
        }

        void DashHit(PlayerRender victim, WorldRender world)
        {
            PlayerRender attacker = default;
            bool found = false;
            float best = 2.5f * 2.5f;
            foreach (var o in world.Players)
            {
                if (o.Id == victim.Id || !o.Alive || o.Spectator) continue;
                float d = (o.Position - victim.Position).sqrMagnitude;
                if (d < best) { best = d; attacker = o; found = true; }
            }
            var contact = found ? (victim.Position + attacker.Position) / 2f : victim.Position;
            contact.y += 0.6f;
            bool local = victim.IsLocal || (found && attacker.IsLocal);
            Queue(Sfx.DashHit, 1f, Pan(contact.x), local);
            _particles.Impact(contact, new Color(1f, 0.97f, 0.85f), 1.7f, 7);
            Hitstop();
            if (local) AddShake(0.07f);
            LogEvent($"dash hit #{victim.Id}{(found ? " by #" + attacker.Id : "")}");
            Shot("dashhit", 0.0f);
        }

        void HazardLanded(SwordRender s, Vector2 listener, bool hasListener, float near)
        {
            Sfx sound;
            Color color;
            int count = 7;
            switch (s.Kind)
            {
                case HazardKind.Icicle: sound = Sfx.LandIcicle; color = new Color(0.78f, 0.93f, 1f, 0.95f); count = 10; break;
                case HazardKind.Crate: sound = Sfx.LandCrate; color = new Color(0.66f, 0.47f, 0.28f, 1f); break;
                case HazardKind.Fork: sound = Sfx.LandFork; color = new Color(0.9f, 0.9f, 0.95f, 1f); count = 6; break;
                default: sound = Sfx.LandKnife; color = new Color(0.84f, 0.87f, 0.92f, 1f); break;
            }
            if (s.AsBox) { sound = Sfx.LandCrate; color = new Color(0.66f, 0.47f, 0.28f, 1f); }
            var at = new Vector2(s.Position.x, s.Position.y);
            float dx = Mathf.Abs(at.x - listener.x);
            Queue(sound, near, Pan(at.x), dx < 3f);
            _particles.Shards(at, color, count, s.Kind == HazardKind.Crate || s.AsBox ? 0.7f : 1f);
            if (hasListener && dx < 2.4f && Mathf.Abs(at.y - listener.y) < 2.5f)
            {
                AddShake(s.Kind == HazardKind.Crate || s.AsBox ? 0.14f : 0.1f);
                LogEvent($"near hazard landed x={at.x:0.0} dx={dx:0.0} (shake {(JuiceAudio.ScreenShake ? "on" : "off")})");
                Shot("nearland_" + s.Kind, 0.05f);
            }
            else Shot("land_" + s.Kind, 0.06f);
        }

        void ScreenEvents(SessionState state)
        {
            var screen = state.Screen;
            if (screen != _lastScreen)
            {
                if (screen == ClientScreen.Playing && _lastScreen == ClientScreen.Countdown) { Queue(Sfx.Go, 1f, 0f, true); LogEvent("go"); }
                if (screen == ClientScreen.Result)
                {
                    bool won = state.WinnerIds.Contains(state.LocalId);
                    if (won) Queue(Sfx.Win, 1f, 0f, true);
                    else if (state.LocalInRound) Queue(Sfx.Lose, 1f, 0f, true);
                    LogEvent(won ? "win" : state.LocalInRound ? "lose" : "result (not in round)");
                    Shot("result", 0.6f);
                }
                if (screen == ClientScreen.Countdown) _lastCount = -1;
                _lastScreen = screen;
            }
            if (screen == ClientScreen.Countdown)
            {
                int n = Mathf.CeilToInt(state.CountdownSeconds);
                if (n != _lastCount && n >= 1 && n <= 3) { Queue(Sfx.Count, 1f, 0f, true); LogEvent("count " + n); }
                _lastCount = n;
            }
        }

        void Queue(Sfx s, float volume, float pan, bool local)
        {
            int i = (int)s;
            float key = volume * (local ? 1f : JuiceAudio.OtherPlayerVolume);
            float cur = _frameVol[i] * (_frameLocal[i] ? 1f : JuiceAudio.OtherPlayerVolume);
            if (_frameVol[i] > 0f && key <= cur) return;
            _frameVol[i] = volume;
            _framePan[i] = pan;
            _frameLocal[i] = local;
        }

        void Flush()
        {
            for (int i = 0; i < _frameVol.Length; i++)
            {
                if (_frameVol[i] <= 0f) continue;
                bool played = _audio.Play((Sfx)i, _frameVol[i], _framePan[i], _frameLocal[i]);
                if (_log && played) Debug.Log($"[Juice] sound {JuiceAudio.SoundName(i)} vol={_frameVol[i]:0.00} local={_frameLocal[i]} voices={_audio.ActiveVoices}");
            }
        }

        float Pan(float x)
        {
            var cam = _game.WorldCamera;
            float halfW = cam.orthographicSize * cam.aspect;
            return Mathf.Clamp((x - cam.transform.position.x) / Mathf.Max(1f, halfW), -1f, 1f);
        }

        static void Swap<T>(ref T a, ref T b)
        {
            var t = a; a = b; b = t;
        }

        void LogEvent(string what)
        {
            if (_log) Debug.Log($"[Juice] event {what} t={_real:0.00} lag={Lag:0.000}");
        }

        void Shot(string tag, float delay)
        {
            if (_shotPath == null || _shotCount + _pendingShots.Count >= 30) return;
            _shotsByTag.TryGetValue(tag, out int n);
            if (n >= 2) return;
            _shotsByTag[tag] = n + 1;
            _pendingShots.Add(new KeyValuePair<double, string>(Time.unscaledTimeAsDouble + delay, tag));
        }

        // ------------------------------------------------ 매 프레임(세션 없을 때도)

        void Update()
        {
            if (_game.Session == null && _session != null) ResetState(null);
            if (_game.Session == null) Time.timeScale = 1f;
            _audio.SetMap(_game.Layout.Source != null ? _game.Layout.Source.Id : 1);
            _maxVoices = Mathf.Max(_maxVoices, _audio.ActiveVoices);

            for (int i = _pendingShots.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTimeAsDouble < _pendingShots[i].Key) continue;
                string path = _shotPath.Replace("{n}", (++_shotCount).ToString("00") + "_" + _pendingShots[i].Value);
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"[Juice] shot {path}");
                _pendingShots.RemoveAt(i);
            }

            if (Time.unscaledTimeAsDouble >= _statsAt)
            {
                _statsAt = Time.unscaledTimeAsDouble + 10;
                var sb = new System.Text.StringBuilder("[Juice] stats sounds");
                for (int i = 0; i < _audio.PlayCounts.Length; i++)
                    if (_audio.PlayCounts[i] > 0) sb.Append(' ').Append(JuiceAudio.SoundName(i).Substring(4)).Append('=').Append(_audio.PlayCounts[i]);
                sb.Append($" | maxVoices={_maxVoices} dropped={_audio.Dropped} faded={_audio.Faded} particles={_particles.Emitted} hitstops={_hitstops} slowmos={_slowmos} shakes={_shakes} lastTwo={_lastTwoCount} sfxVol={JuiceAudio.SfxVolume:0.00} bgmVol={JuiceAudio.BgmVolume:0.00} shake={(JuiceAudio.ScreenShake ? "on" : "off")}");
                Debug.Log(sb.ToString());
            }
        }

        void LateUpdate()
        {
            // 연출 컴포넌트를 캐릭터 뷰마다 붙인다(WorldView 는 고치지 않는다)
            foreach (var v in _view.Players.Values)
                if (v.GetComponent<PlayerJuice>() == null)
                    v.gameObject.AddComponent<PlayerJuice>().Init(v, _particles, _particles.transform);

            var cam = _game.WorldCamera;
            if (cam == null) return;
            float udt = Time.unscaledDeltaTime;
            Vector2 basePos = cam.transform.position;
            float baseSize = cam.orthographicSize;
            if (!_focusInit) { _focus = basePos; _focusInit = true; }

            float targetZoom = 1f;
            Vector2 target = basePos;
            if (_focusDeath) { targetZoom = SlowZoom; target = _deathFocus; }
            else if (_lastTwo && _game.Session != null) { targetZoom = LastTwoZoom; target = _lastTwoMid; }
            float k = 1f - Mathf.Exp(-(_focusDeath ? 6f : 2.5f) * udt);
            _zoom = Mathf.Lerp(_zoom, targetZoom, k);
            _focus = Vector2.Lerp(_focus, target, k);

            // 당겨도 원래 화면 밖은 보이지 않게
            var maxOff = new Vector2(baseSize * cam.aspect, baseSize) * (1f - _zoom);
            var off = _focus - basePos;
            off.x = Mathf.Clamp(off.x, -maxOff.x, maxOff.x);
            off.y = Mathf.Clamp(off.y, -maxOff.y, maxOff.y);

            _shake *= Mathf.Exp(-11f * udt);
            if (_shake < 0.002f) _shake = 0f;
            float t = Time.unscaledTime * 32f;
            var shake = new Vector2(Mathf.PerlinNoise(_shakeSeed, t) - 0.5f, Mathf.PerlinNoise(t, _shakeSeed) - 0.5f) * (2f * _shake);
            if (!JuiceAudio.ScreenShake) shake = Vector2.zero;

            cam.orthographicSize = baseSize * _zoom;
            cam.transform.position = new Vector3(basePos.x + off.x + shake.x, basePos.y + off.y + shake.y, cam.transform.position.z);
        }

        void OnDisable() => Time.timeScale = 1f;
    }
}
