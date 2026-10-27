using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using OnlineSeverGame.Client.Input;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Client.Net;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OnlineSeverGame.Client.Game
{
    // 서버 접속 세션 (docs/protocol.md 8장 규칙).
    //   - 고정 20Hz 클라이언트 틱마다 Input(seq, buttons) 전송
    //   - 내 캐릭터: Shared PlayerPhysics.Step 으로 예측, 스냅샷의 lastInputSeq 이후 입력을 재시뮬레이션해 보정
    //   - 다른 플레이어·칼·밀리는 상자: 스냅샷 두 개 사이 보간 (InterpDelayTicks 만큼 늦게)
    //   - 예측은 최신 스냅샷의 상자 위치를 고정된 벽·발판으로 쓴다. 내가 남의 머리를 밟아 튕기는 것도 예측한다:
    //     최신 스냅샷의 다른 플레이어를 마지막으로 누른 버튼 그대로 내 예측과 같은 틱만큼 함께 굴려(서버도 새 입력이
    //     없으면 그렇게 한다) 그 위치로 검사한다. 상대 기절·밀치기·상자 이동은 서버만 판정하고 보정으로 따라간다.
    public sealed class NetworkSession : IGameSession
    {
        const float TickSeconds = GameConstants.TickMilliseconds / 1000f;
        const double InterpDelayTicks = 2.0;       // 100ms 늦게 그린다
        const int MaxCatchUpTicks = 5;             // 프레임이 늦어도 한 번에 이만큼만 따라잡는다
        const int MaxPendingInputs = 64;
        const int SnapshotBufferSize = 32;
        const float ServerSilenceSeconds = 5f;     // 이 시간 동안 패킷이 없으면 끊김으로 본다
        const float SnapCorrectionUnits = 2.5f;    // 이보다 크게 어긋나면 부드럽게 말고 바로 옮긴다
        const float CorrectionDecayPerSecond = 15f;

        readonly NetClient _net = new NetClient();
        readonly StageLayout _layout;
        readonly string _name;
        readonly Stopwatch _clock = Stopwatch.StartNew();

        // 접속·방 정보
        bool _welcomed;
        byte _localId;
        Stage _stage;
        readonly SortedDictionary<byte, string> _names = new SortedDictionary<byte, string>();
        readonly Dictionary<byte, string> _departedNames = new Dictionary<byte, string>(); // 결과 화면에 남길 이름
        readonly HashSet<byte> _participants = new HashSet<byte>();
        RoundEndPacket _lastRoundEnd;
        ushort _lastRoundStartNumber;
        double _lastPacketTime;

        // 스냅샷 보간
        readonly List<SnapshotPacket> _snaps = new List<SnapshotPacket>();
        SnapshotPacket _latest;
        double _latestRecvTime;
        double _renderTick;
        bool _renderTickInit;

        // 예측
        struct PendingInput
        {
            public uint Seq;
            public InputButtons Buttons;
        }

        readonly List<PendingInput> _pending = new List<PendingInput>();
        // 밟기 예측용: 살아 있는 다른 플레이어를 내 예측과 같은 틱까지 굴린 상태(틱 전/후)
        readonly List<PlayerState> _others = new List<PlayerState>();
        readonly List<PlayerState> _othersBefore = new List<PlayerState>();
        // 시험용(-nostomppredict): 밟기 튕김 예측을 끄고 비교한다
        public static bool StompPrediction = true;
        uint _seq;
        float _tickAccum;
        bool _predicting;
        bool _teleportNext;
        PlayerState _predicted;
        PlayerState _predictedPrev;
        Vector2 _correction;

        // 통계(로그용)
        int _reconcileCount;
        int _mispredictCount;   // 보정 거리가 0.01 유닛을 넘은 횟수
        float _maxCorrection;
        // 대시 연타 진단: 예측이 시작한 대시, 서버 스냅샷이 확인한 대시, 화면에 대시 동작이 새로 시작된 횟수
        int _predDashes;
        int _predBounces;   // 예측(재시뮬레이션 포함)이 밟기 튕김을 적용한 횟수
        int _snapCorrections; // 너무 커서(SnapCorrectionUnits 초과) 부드럽게 말고 바로 옮긴 보정
        int _serverDashes;
        int _shownDashes;
        int _remoteShownDashes;
        readonly HashSet<byte> _shownDashing = new HashSet<byte>();
        double _statsTimer = 5.0;
        double _pingTimer;

        public SessionState State { get; } = new SessionState();
        public WorldRender World { get; } = new WorldRender();

        double Now => _clock.Elapsed.TotalSeconds;

        public NetworkSession(StageLayout layout, string name, string address)
        {
            _layout = layout;
            _name = TrimName(name);
            State.Screen = ClientScreen.Connecting;
            if (!TryParseAddress(address, out string host, out int port))
            {
                Fail("서버 주소 형식이 잘못되었습니다 (예: 127.0.0.1:7777)");
                return;
            }
            Debug.Log($"[Net] connecting {host}:{port} as '{_name}'");
            _net.Connect(host, port);
        }

        public static bool TryParseAddress(string address, out string host, out int port)
        {
            host = null;
            port = ProtocolConstants.DefaultPort;
            if (string.IsNullOrWhiteSpace(address)) return false;
            address = address.Trim();
            int colon = address.LastIndexOf(':');
            if (colon >= 0)
            {
                if (!int.TryParse(address.Substring(colon + 1), out port) || port <= 0 || port > 65535) return false;
                address = address.Substring(0, colon);
            }
            host = address;
            return host.Length > 0;
        }

        static string TrimName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) name = "Player";
            while (Encoding.UTF8.GetByteCount(name) > ProtocolConstants.MaxNameBytes)
                name = name.Substring(0, name.Length - 1);
            return name;
        }

        public void Update(float dt, InputCollector input)
        {
            DrainNetwork();
            if (State.Screen == ClientScreen.Connect) return; // 끊김

            if (_welcomed)
            {
                if (Now - _lastPacketTime > ServerSilenceSeconds)
                {
                    Fail("서버 응답이 없습니다");
                    return;
                }

                _tickAccum += dt;
                int ticks = 0;
                while (_tickAccum >= TickSeconds)
                {
                    _tickAccum -= TickSeconds;
                    if (ticks++ < MaxCatchUpTicks) ClientTick(input);
                }

                _pingTimer -= dt;
                if (_pingTimer <= 0)
                {
                    _pingTimer = 1.0;
                    _net.Send(new PingPacket { ClientTime = (uint)_clock.ElapsedMilliseconds });
                }

                _statsTimer -= dt;
                if (_statsTimer <= 0)
                {
                    _statsTimer = 5.0;
                    Debug.Log($"[Net] stats room={_latest?.RoomState} tick={_latest?.ServerTick} seq={_seq} pending={_pending.Count} " +
                              $"ping={State.PingMs}ms reconciles={_reconcileCount} mispredicts={_mispredictCount} " +
                              $"maxCorrection={_maxCorrection:0.000} rx={_net.BytesReceived} tx={_net.BytesSent} " +
                              $"dashes pred={_predDashes} server={_serverDashes} shown={_shownDashes} remoteShown={_remoteShownDashes} " +
                              $"snaps={_snapCorrections} predBounces={_predBounces}");
                }
            }

            float decay = Mathf.Exp(-CorrectionDecayPerSecond * dt);
            _correction *= decay;
            if (_correction.sqrMagnitude < 1e-6f) _correction = Vector2.zero;

            UpdateScreen();
            BuildWorld(dt);
        }

        // ---------------------------------------------------------------- 수신

        void DrainNetwork()
        {
            while (_net.TryDequeue(out NetEvent e))
            {
                switch (e.Kind)
                {
                    case NetEventKind.Connected:
                        Debug.Log("[Net] connected, sending Hello");
                        _lastPacketTime = Now;
                        _net.Send(new HelloPacket { Name = _name });
                        break;
                    case NetEventKind.Disconnected:
                        Fail(e.Reason);
                        return;
                    case NetEventKind.Packet:
                        _lastPacketTime = Now;
                        Handle(e.Packet);
                        if (State.Screen == ClientScreen.Connect) return;
                        break;
                }
            }
        }

        void Handle(IPacket packet)
        {
            switch (packet)
            {
                case WelcomePacket w: OnWelcome(w); break;
                case RejectPacket r: OnReject(r); break;
                case PlayerJoinedPacket j:
                    _names[j.PlayerId] = j.Name;
                    _departedNames.Remove(j.PlayerId);
                    Debug.Log($"[Net] joined #{j.PlayerId} {j.Name}");
                    break;
                case PlayerLeftPacket l:
                    if (_names.TryGetValue(l.PlayerId, out string leftName)) _departedNames[l.PlayerId] = leftName;
                    _names.Remove(l.PlayerId);
                    Debug.Log($"[Net] left #{l.PlayerId} ({l.Reason})");
                    break;
                case SnapshotPacket s: OnSnapshot(s); break;
                case RoundStartPacket rs: OnRoundStart(rs); break;
                case RoundEndPacket re: OnRoundEnd(re); break;
                case PongPacket p:
                    State.PingMs = (int)((uint)_clock.ElapsedMilliseconds - p.ClientTime);
                    break;
            }
        }

        void OnWelcome(WelcomePacket w)
        {
            if (w.ProtocolVersion != ProtocolConstants.Version)
            {
                Fail($"프로토콜 버전이 다릅니다 (서버 {w.ProtocolVersion}, 클라이언트 {ProtocolConstants.Version})");
                return;
            }
            _stage = Stage.ById(w.StageId);
            if (_stage == null)
            {
                Fail($"모르는 스테이지 id {w.StageId}");
                return;
            }
            _layout.SetStage(_stage);
            State.MapName = StageLayout.DisplayName(_stage.Id);
            _welcomed = true;
            _localId = w.PlayerId;
            State.LocalId = w.PlayerId;
            State.Capacity = w.MaxPlayers;
            State.Screen = ClientScreen.Lobby;
            Debug.Log($"[Net] welcome id={w.PlayerId} tick={w.ServerTick} stage={w.StageId}");
        }

        void OnReject(RejectPacket r)
        {
            string why;
            switch (r.Reason)
            {
                case RejectReason.RoomFull: why = "방이 가득 찼습니다 (최대 8명)"; break;
                case RejectReason.VersionMismatch: why = "서버와 클라이언트 버전이 다릅니다"; break;
                case RejectReason.BadHello: why = "서버가 접속 요청을 거부했습니다 (이름 확인)"; break;
                default: why = "접속이 거부되었습니다 (" + r.Reason + ")"; break;
            }
            Fail(why);
        }

        void OnRoundStart(RoundStartPacket rs)
        {
            _participants.Clear();
            foreach (var id in rs.Participants) _participants.Add(id);
            State.RoundNumber = rs.RoundNumber;
            // 맵은 라운드 시작 때만 바뀐다. 화면(GameBootstrap)은 레이아웃 Version 을 보고 다시 만든다.
            Stage stage = Stage.ById(rs.StageId);
            if (stage != null && stage != _stage)
            {
                _stage = stage;
                _layout.SetStage(stage);
                Debug.Log($"[Net] map {stage.Id} '{stage.Name}'");
            }
            State.MapName = StageLayout.DisplayName(_stage.Id);
            // Countdown 중 누가 들어오면 같은 번호로 다시 온다. 그때는 참가자 목록만 갱신한다.
            if (rs.RoundNumber == _lastRoundStartNumber)
            {
                Debug.Log($"[Net] round {rs.RoundNumber} participants updated: {rs.Participants.Count}");
                return;
            }
            _lastRoundStartNumber = rs.RoundNumber;
            _lastRoundEnd = null;
            _departedNames.Clear();
            // 서버가 스폰 지점으로 순간이동시키므로 보정 연출 없이 바로 옮긴다.
            _teleportNext = true;
            Debug.Log($"[Net] round {rs.RoundNumber} countdown, {rs.Participants.Count} participants");
        }

        void OnRoundEnd(RoundEndPacket re)
        {
            _lastRoundEnd = re;
            var names = new List<string>();
            foreach (var id in re.Winners) names.Add(NameOf(id));
            Debug.Log($"[Net] round {re.RoundNumber} end, winners=[{string.Join(", ", names)}] " +
                      $"reconciles={_reconcileCount} mispredicts={_mispredictCount} maxCorrection={_maxCorrection:0.000}");
        }

        void OnSnapshot(SnapshotPacket s)
        {
            if (_latest != null && s.ServerTick <= _latest.ServerTick) return;
            _latest = s;
            _latestRecvTime = Now;
            _snaps.Add(s);
            if (_snaps.Count > SnapshotBufferSize) _snaps.RemoveAt(0);

            State.RoundNumber = s.RoundNumber;
            if (s.RoomState == RoomState.Playing) State.RoundSeconds = s.RoundTick * TickSeconds;

            if (TryFindPlayer(s, _localId, out PlayerSnapshot me) && me.Alive)
            {
                if (me.State.DashTicks == GameConstants.DashTicks) _serverDashes++;
                Reconcile(me);
            }
            else
                _predicting = false;
        }

        // ---------------------------------------------------------------- 예측·보정

        void ClientTick(InputCollector input)
        {
            InputFrame f = input.Consume();
            InputButtons buttons = ToButtons(f);
            _seq++;
            _net.Send(new InputPacket { Seq = _seq, Buttons = buttons });

            _pending.Add(new PendingInput { Seq = _seq, Buttons = buttons });
            if (_pending.Count > MaxPendingInputs) _pending.RemoveAt(0);

            if (_predicting)
            {
                _predictedPrev = _predicted;
                _predRoundTick = NextRoundTick(_predRoundTick);
                PredictStep(ref _predicted, buttons, _predRoundTick);
                if (_predicted.DashTicks == GameConstants.DashTicks) _predDashes++;
            }
        }

        // 예측에 쓰는 상자: 최신 스냅샷 그대로(서버보다 조금 과거지만 멈춰 있는 상자는 정확하다)
        IReadOnlyList<PushableState> PredictBoxes => _latest?.Pushables;

        // 예측한 마지막 스텝의 라운드 틱(녹는 발판 모양). Playing 이 아니면 0 그대로.
        uint _predRoundTick;

        uint NextRoundTick(uint roundTick) =>
            _latest != null && _latest.RoomState == RoomState.Playing ? roundTick + 1 : 0;

        // 내 캐릭터 한 스텝 예측: 다른 플레이어를 마지막 버튼으로 한 스텝 굴리고, 나를 공용 물리로 굴린 뒤
        // 남의 머리에 떨어졌으면 튕긴다(서버 StompCollision 과 같은 조건)
        void PredictStep(ref PlayerState state, InputButtons buttons, uint roundTick)
        {
            Stage stage = _stage.AtRoundTick(roundTick);
            _othersBefore.Clear();
            for (int i = 0; i < _others.Count; i++)
            {
                PlayerState o = _others[i];
                _othersBefore.Add(o);
                PlayerPhysics.Step(ref o, o.PrevButtons, stage, PredictBoxes, out _);
                _others[i] = o;
            }
            PlayerState before = state;
            PlayerPhysics.Step(ref state, buttons, stage, PredictBoxes, out _);
            if (StompPrediction && StompCollision.PredictBounce(before, ref state, _othersBefore, _others)) _predBounces++;
        }

        static InputButtons ToButtons(InputFrame f)
        {
            InputButtons b = InputButtons.None;
            if (f.MoveX < 0) b |= InputButtons.Left;
            if (f.MoveX > 0) b |= InputButtons.Right;
            // 점프·대시는 서버가 이전 틱과 비교해 눌림을 판정한다. 틱 사이에 짧게 누른 것도 한 틱은 켜서 보낸다.
            if (f.JumpHeld || f.JumpPressed) b |= InputButtons.Jump;
            if (f.DashPressed) b |= InputButtons.Dash;
            if (f.DownHeld) b |= InputButtons.Down;
            return b;
        }

        void Reconcile(PlayerSnapshot me)
        {
            Vector2 before = LocalRenderPosition();
            bool wasPredicting = _predicting;

            PlayerState state = me.State;
            PlayerState prev = state;
            // 다른 플레이어도 이 스냅샷 시점에서 다시 굴리기 시작한다.
            _others.Clear();
            foreach (var p in _latest.Players)
                if (p.PlayerId != _localId && p.Alive) _others.Add(p.State);
            int write = 0;
            uint roundTick = _latest.RoomState == RoomState.Playing ? _latest.RoundTick : 0;
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].Seq <= me.LastInputSeq) continue;
                _pending[write++] = _pending[i];
                prev = state;
                roundTick = NextRoundTick(roundTick);
                PredictStep(ref state, _pending[i].Buttons, roundTick);
            }
            _predRoundTick = roundTick;
            _pending.RemoveRange(write, _pending.Count - write);

            _predicted = state;
            _predictedPrev = prev;
            _predicting = true;
            _reconcileCount++;

            Vector2 after = LocalRenderPosition();
            Vector2 diff = before - after; // 이전 화면 위치를 유지하도록 보정 오프셋에 더한다
            if (!wasPredicting || _teleportNext || (diff + _correction).magnitude > SnapCorrectionUnits)
            {
                if (wasPredicting && !_teleportNext)
                {
                    _snapCorrections++;
                    Debug.Log($"[Net] correction snap {(diff + _correction).magnitude:0.00}u server={me.State} pending={_pending.Count}");
                }
                _correction = Vector2.zero;
                _teleportNext = false;
            }
            else
            {
                if (diff.magnitude > _maxCorrection) _maxCorrection = diff.magnitude;
                // 큰 보정은 원인을 볼 수 있게 남긴다(밟기·밀치기·상자 등 서버만 아는 일)
                if (diff.magnitude > 0.3f)
                    Debug.Log($"[Net] correction {diff.magnitude:0.00}u server={me.State} pending={_pending.Count}");
                if (diff.magnitude > 0.01f) _mispredictCount++;
                _correction += diff;
            }
        }

        // 틱 사이를 보간한 예측 위치 + 보정 오프셋 빼기 전 값
        Vector2 LocalRenderPosition()
        {
            float alpha = Mathf.Clamp01(_tickAccum / TickSeconds);
            var a = new Vector2(StageLayout.ToWorld(_predictedPrev.X), StageLayout.ToWorld(_predictedPrev.Y));
            var b = new Vector2(StageLayout.ToWorld(_predicted.X), StageLayout.ToWorld(_predicted.Y));
            return Vector2.Lerp(a, b, alpha) + _correction;
        }

        // ---------------------------------------------------------------- 화면 상태

        void UpdateScreen()
        {
            if (!_welcomed)
            {
                State.Screen = ClientScreen.Connecting;
                return;
            }

            // 명단: 접속자(PlayerJoined) + 최신 스냅샷의 생존·참가 여부 + 결과
            State.Roster.Clear();
            foreach (var kv in _names)
            {
                var entry = new RosterEntry
                {
                    Id = kv.Key,
                    Name = kv.Value,
                    ColorIndex = kv.Key - 1,
                    Connected = true,
                    InRound = _participants.Contains(kv.Key),
                    Alive = true,
                };
                if (_latest != null && TryFindPlayer(_latest, kv.Key, out PlayerSnapshot p))
                {
                    entry.Alive = p.Alive;
                    entry.InRound = p.InRound;
                }
                State.Roster.Add(entry);
            }
            if (_lastRoundEnd != null) ApplyResults(_lastRoundEnd);

            State.LocalInRound = true;
            if (_latest != null && TryFindPlayer(_latest, _localId, out PlayerSnapshot me))
                State.LocalInRound = me.InRound;

            RoomState room = _latest?.RoomState ?? RoomState.Waiting;
            switch (room)
            {
                case RoomState.Waiting:
                    State.Screen = ClientScreen.Lobby;
                    State.CountdownSeconds = 0f;
                    break;
                case RoomState.Countdown:
                    State.Screen = ClientScreen.Countdown;
                    State.CountdownSeconds = _latest.StateTimer * TickSeconds;
                    State.RoundSeconds = 0f;
                    break;
                case RoomState.Playing:
                    bool alive = _latest != null && TryFindPlayer(_latest, _localId, out me) && me.Alive && me.InRound;
                    State.Screen = alive ? ClientScreen.Playing : ClientScreen.Spectating;
                    break;
                case RoomState.Ended:
                    State.Screen = ClientScreen.Result;
                    State.CountdownSeconds = _latest.StateTimer * TickSeconds;
                    break;
            }
        }

        void ApplyResults(RoundEndPacket re)
        {
            State.WinnerIds.Clear();
            var names = new List<string>();
            foreach (var id in re.Winners)
            {
                State.WinnerIds.Add(id);
                names.Add(NameOf(id));
            }
            State.WinnerName = string.Join(", ", names);

            uint longest = 0;
            foreach (var res in re.Results)
            {
                if (res.SurvivedTicks > longest) longest = res.SurvivedTicks;
                int idx = State.Roster.FindIndex(r => r.Id == res.PlayerId);
                RosterEntry entry;
                if (idx < 0)
                {
                    // 이미 나간 사람도 결과에는 남긴다.
                    entry = new RosterEntry { Id = res.PlayerId, Name = NameOf(res.PlayerId), ColorIndex = res.PlayerId - 1 };
                    State.Roster.Add(entry);
                    idx = State.Roster.Count - 1;
                }
                entry = State.Roster[idx];
                entry.Place = res.Place;
                entry.SurvivedSeconds = res.SurvivedTicks * TickSeconds;
                entry.LeftGame = res.Cause == EliminationCause.Disconnected;
                entry.Survived = res.Cause == EliminationCause.None;
                entry.InRound = true;
                State.Roster[idx] = entry;
            }
            State.RoundSeconds = longest * TickSeconds;
        }

        string NameOf(byte id) =>
            _names.TryGetValue(id, out string n) ? n : _departedNames.TryGetValue(id, out n) ? n : "#" + id;

        // ---------------------------------------------------------------- 그리기

        void BuildWorld(float dt)
        {
            World.Clear();
            if (_latest == null) return;

            // 서버 틱 시계를 추정해 InterpDelayTicks 만큼 과거를 그린다.
            double target = _latest.ServerTick + (Now - _latestRecvTime) / TickSeconds - InterpDelayTicks;
            if (!_renderTickInit || Math.Abs(target - _renderTick) > 4.0)
            {
                _renderTick = target;
                _renderTickInit = true;
            }
            else
            {
                _renderTick += dt / TickSeconds;
                _renderTick += (target - _renderTick) * Math.Min(1.0, dt * 2.0);
            }
            if (_renderTick > _latest.ServerTick) _renderTick = _latest.ServerTick; // 외삽하지 않는다

            SnapshotPacket s0 = _snaps[0], s1 = null;
            for (int i = 0; i < _snaps.Count; i++)
            {
                if (_snaps[i].ServerTick <= _renderTick) s0 = _snaps[i];
                else { s1 = _snaps[i]; break; }
            }
            if (s1 != null && s1.ServerTick <= s0.ServerTick) s1 = null;
            float t = s1 == null ? 0f : (float)((_renderTick - s0.ServerTick) / (s1.ServerTick - s0.ServerTick));
            t = Mathf.Clamp01(t);

            // 플레이어 목록은 최신 스냅샷 기준(입장·퇴장이 바로 보이게)
            foreach (var p in _latest.Players)
            {
                bool isLocal = p.PlayerId == _localId;
                PlayerSnapshot a = p, b = p;
                bool hasA = TryFindPlayer(s0, p.PlayerId, out a);
                bool hasB = s1 != null && TryFindPlayer(s1, p.PlayerId, out b);
                if (!hasA && !hasB) { a = b = p; }
                else if (!hasA) a = b;
                else if (!hasB) b = a;

                var render = new PlayerRender
                {
                    Id = p.PlayerId,
                    Name = NameOf(p.PlayerId),
                    ColorIndex = p.PlayerId - 1,
                    IsLocal = isLocal,
                };

                if (isLocal && _predicting)
                {
                    render.Position = LocalRenderPosition();
                    render.Alive = true;
                    render.Dashing = _predicted.IsDashing;
                    render.Facing = _predicted.Facing;
                    render.DashCooldown = _predicted.DashCooldown / (float)GameConstants.DashCooldownTicks;
                    SetMotion(ref render, _predicted);
                }
                else
                {
                    render.Position = Vector2.Lerp(
                        new Vector2(StageLayout.ToWorld(a.State.X), StageLayout.ToWorld(a.State.Y)),
                        new Vector2(StageLayout.ToWorld(b.State.X), StageLayout.ToWorld(b.State.Y)), t);
                    // 생존 여부는 그리는 시점 기준(죽는 순간이 칼과 맞게)
                    render.Alive = (t < 1f ? a : b).Alive;
                    render.Dashing = a.State.IsDashing;
                    render.Facing = a.State.Facing;
                    SetMotion(ref render, a.State);
                }
                // 라운드 도중 들어온 관전자는 흐리게
                if (!p.InRound && (_latest.RoomState == RoomState.Playing || _latest.RoomState == RoomState.Ended))
                {
                    render.Alive = false;
                    render.Spectator = true;
                }
                render.Winner = State.Screen == ClientScreen.Result && State.WinnerIds.Contains(p.PlayerId);
                // 서버는 죽은 캐릭터를 죽은 자리(공중일 수 있음)에 멈춰 둔다. 웅덩이와 이름표는 아래 면 위에 그린다.
                if (!render.Alive && !render.Spectator)
                    render.Position.y = _layout.SurfaceBelow(render.Position.x, render.Position.y);
                CountShownDash(render);
                World.Players.Add(render);
            }

            // 그리는 시점의 지형(녹는 아이스크림 발판의 폭)
            _layout.RoundTick = s0.RoomState == RoomState.Playing ? s0.RoundTick : 0;

            foreach (var k in s0.Knives)
            {
                KnifeState a = k, b = k;
                if (s1 != null && TryFindKnife(s1, k.Id, out KnifeState kb)) b = kb;
                float x = StageLayout.ToWorld(a.X);
                // Warning → Falling 경계에서는 칼이 출발점에서부터 보이도록 b 단계로 바로 넘어간다.
                KnifePhase phase = a.Phase;
                float y;
                if (a.Phase == KnifePhase.Warning && b.Phase != KnifePhase.Warning && t > 0.5f)
                {
                    phase = b.Phase;
                    y = StageLayout.ToWorld(b.Y);
                }
                else if (a.Phase == KnifePhase.Warning)
                {
                    y = StageLayout.ToWorld(a.Y);
                }
                else
                {
                    y = Mathf.Lerp(StageLayout.ToWorld(a.Y), StageLayout.ToWorld(b.Y), t);
                }

                float timer = Mathf.Lerp(a.Timer, b.Phase == a.Phase ? b.Timer : 0, t);
                World.Swords.Add(new SwordRender
                {
                    Id = k.Id,
                    Kind = k.Kind,
                    SpawnY = _layout.SwordSpawnY,
                    Phase = phase == KnifePhase.Warning ? SwordPhase.Warning
                        : phase == KnifePhase.Falling ? SwordPhase.Falling : SwordPhase.Landed,
                    Position = new Vector2(x, y),
                    WarningProgress = 1f - timer / k.Spec.WarningTicks,
                    LandingY = _layout.SwordLandingY(k.Kind, x, _latest.Pushables),
                });
            }

            foreach (var box in s0.Pushables)
            {
                PushableState a = box, b = box;
                if (s1 != null && TryFindBox(s1, box.Id, out PushableState bb)) b = bb;
                World.Boxes.Add(new BoxRender
                {
                    Id = box.Id,
                    Position = Vector2.Lerp(
                        new Vector2(StageLayout.ToWorld(a.X), StageLayout.ToWorld(a.Y)),
                        new Vector2(StageLayout.ToWorld(b.X), StageLayout.ToWorld(b.Y)), t),
                    Size = new Vector2(StageLayout.ToWorld(a.Width), StageLayout.ToWorld(a.Height)),
                    Crumbling = a.Crumble > 0,
                });
            }
        }

        void CountShownDash(PlayerRender r)
        {
            byte id = (byte)r.Id;
            bool dashing = r.Alive && r.Dashing;
            if (dashing && _shownDashing.Add(id))
            {
                if (r.IsLocal) _shownDashes++;
                else _remoteShownDashes++;
            }
            else if (!dashing) _shownDashing.Remove(id);
        }

        // 동작 고르기에 쓰는 물리 상태(PlayerView 가 대기·달리기·점프·낙하·대시·밀려남을 고른다)
        static void SetMotion(ref PlayerRender r, PlayerState s)
        {
            r.Grounded = s.Grounded;
            r.VelocityY = StageLayout.ToWorld(s.Vy) / TickSeconds;
            r.Moving = s.Vx != 0;
            r.Stunned = s.StunTicks > 0;
            r.Crouching = s.Crouching;
            r.Stomped = s.Stomped && s.StunTicks > 0;
        }

        static bool TryFindBox(SnapshotPacket s, ushort id, out PushableState b)
        {
            var list = s.Pushables;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == id) { b = list[i]; return true; }
            }
            b = default;
            return false;
        }

        static bool TryFindPlayer(SnapshotPacket s, byte id, out PlayerSnapshot p)
        {
            var list = s.Players;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].PlayerId == id) { p = list[i]; return true; }
            }
            p = default;
            return false;
        }

        static bool TryFindKnife(SnapshotPacket s, ushort id, out KnifeState k)
        {
            var list = s.Knives;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == id) { k = list[i]; return true; }
            }
            k = default;
            return false;
        }

        // ---------------------------------------------------------------- 종료

        void Fail(string reason)
        {
            Debug.Log("[Net] disconnected: " + reason);
            State.Screen = ClientScreen.Connect;
            State.Status = string.IsNullOrEmpty(reason) ? "연결이 끊어졌습니다" : reason;
            _net.Dispose();
        }

        public void Close()
        {
            _net.Dispose();
        }
    }
}
