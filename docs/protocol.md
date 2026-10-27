# OnlineSeverGame 프로토콜 v1

서버(C# .NET 10)와 Unity 클라이언트가 같은 `Shared/` 소스를 쓴다. 이 문서는 그 소스의 설명이며, 바이트 배치가 바뀌면 `ProtocolConstants.Version`을 올리고 이 문서를 함께 고친다.

- 패킷 정의·직렬화: `Shared/Protocol/` (`Packets.cs`, `PacketCodec.cs`, `FrameDecoder.cs`)
- 물리·스테이지·상수: `Shared/Simulation/` (`GameConstants.cs`, `PlayerPhysics.cs`, `Stage.cs`, `Knife.cs`, `DashCollision.cs`)

## 1. 전송과 프레이밍

- TCP 하나. 서버 기본 포트 `7777`. 양쪽 모두 `NoDelay`(Nagle 끔)를 권장한다.
- 프레임 = `[uint16 LE 길이][payload]`. 길이는 payload 바이트 수(헤더 2바이트 제외).
- payload = `[uint8 패킷 타입][본문]`. 길이 0 또는 8192 초과는 프로토콜 오류 → 연결을 끊는다.
- 모든 정수는 **리틀 엔디언**. 문자열 = `[uint8 바이트 길이][UTF-8]`, 이름은 최대 32바이트.
- 본문을 다 읽고 남는 바이트가 있으면 오류(`PacketFormatException`).
- 수신 측은 읽은 바이트를 `FrameDecoder.Push` 하고 `TryReadFrame`이 false가 될 때까지 꺼낸다. 프레임이 쪼개져 오거나 여러 개가 한 번에 와도 된다. 꺼낸 payload는 `PacketCodec.DecodePayload`로 해석한다. 송신은 `PacketCodec.EncodeFrame(packet)`이 헤더까지 붙인 바이트 배열을 준다.

## 2. 단위

| 항목 | 단위 |
|---|---|
| 틱 | 초당 20틱, 1틱 = 50ms. 서버 틱 번호(`ServerTick`)는 서버 시작부터 0,1,2… |
| 좌표 | 정수 **밀리유닛**. 1 월드 유닛 = 1000. Unity 좌표 = 값 / 1000f |
| 축 | Y 위쪽이 +. 바닥 윗면이 y = 0. x는 0(왼쪽 벽) ~ 스테이지 너비(오른쪽 벽) |
| 속도 | 밀리유닛/틱 |
| 플레이어 위치 | 몸 상자의 **아래쪽 가운데** |
| 칼 위치 | 칼끝(아래쪽 가운데) |

서버와 클라이언트가 정수 연산만 쓰므로 같은 입력이면 결과가 비트 단위로 같다. 부동소수점 결정성 걱정이 없다.

## 3. 패킷 목록

C = 클라이언트→서버, S = 서버→클라이언트. 크기는 타입 바이트를 뺀 본문 기준.

| 타입 | 이름 | 방향 | 본문 |
|---|---|---|---|
| 1 | Hello | C | `u16 protocolVersion`, `string name` |
| 2 | Welcome | S | `u16 protocolVersion`, `u8 playerId`, `u8 tickRate(20)`, `u32 serverTick`, `u8 stageId`, `u8 maxPlayers(8)` |
| 3 | Reject | S | `u8 reason` (1 방 가득 참, 2 버전 불일치, 3 잘못된 Hello). 보낸 뒤 서버가 연결을 닫는다 |
| 4 | PlayerJoined | S | `u8 playerId`, `string name` |
| 5 | PlayerLeft | S | `u8 playerId`, `u8 reason` (1 연결 끊김, 2 시간 초과, 3 프로토콜 오류) |
| 10 | Input | C | `u32 seq`, `u8 buttons` |
| 20 | Snapshot | S | 아래 참고 |
| 30 | RoundStart | S | `u16 roundNumber`, `u16 countdownTicks`, `u32 playStartTick`, `u8 n`, `u8 participantId × n` |
| 31 | RoundEnd | S | `u16 roundNumber`, `u32 endTick`, `u8 w`, `u8 winnerId × w`, `u8 r`, 결과 × r |
| 40 | Ping | C | `u32 clientTime` (아무 클라이언트 시계 값) |
| 41 | Pong | S | `u32 clientTime`(그대로 반사), `u32 serverTick` |

### buttons (Input)

| 비트 | 값 | 의미 |
|---|---|---|
| 0 | 1 | Left (누르고 있는 동안) |
| 1 | 2 | Right |
| 2 | 4 | Jump (눌린 순간에만 발동) |
| 3 | 8 | Dash (눌린 순간에만 발동) |
| 4 | 16 | Down (누르고 있으면 발판을 통과해 떨어짐) |

"눌린 순간"은 이전 틱 버튼(`PlayerState.PrevButtons`)과 비교해 판정한다. 클라이언트는 매 틱 **현재 누르고 있는 상태**를 그대로 보내면 된다.

### Snapshot (20)

```
u32 serverTick
u8  roomState        0 Waiting, 1 Countdown, 2 Playing, 3 Ended
u16 roundNumber      첫 라운드 1, 아직 없으면 0
u32 roundTick        Playing 시작 후 지난 틱 (그 외 0)
u16 stateTimer       Countdown/Ended 남은 틱 (그 외 0)
u8  playerCount
    플레이어 × playerCount (각 26바이트)
u8  knifeCount
    칼 × knifeCount (각 13바이트)
```

플레이어 항목 (26바이트):

```
u8  playerId
u8  flags            bit0 Alive, bit1 InRound(이번 라운드 참가자), bit2 Grounded, bit3 FacingRight
i32 x, i32 y         밀리유닛
i32 vx, i32 vy       밀리유닛/틱
u8  dashTicks        남은 대시 틱(이번 틱 포함). >0이면 대시 중
u8  dashCooldown
u8  stunTicks        >0이면 밀려나는 중(입력 무시)
u8  prevButtons      마지막으로 적용된 버튼 (눌림 판정용)
u32 lastInputSeq     서버가 이 플레이어에게 마지막으로 적용한 입력 seq (없으면 0)
```

FacingRight가 0이면 facing = -1(왼쪽). 이 필드들이 `PlayerState` 전체이므로 클라이언트는 스냅샷 값으로 상태를 그대로 복원할 수 있다.

칼 항목 (13바이트):

```
u16 knifeId
u8  phase            1 Warning(예고), 2 Falling(낙하), 3 Landed(착지)
i32 x                칼 가운데
i32 y                칼끝. Warning 중에는 Stage.KnifeSpawnY
u16 timer            Warning: 낙하까지 남은 틱 / Landed: 사라질 때까지 남은 틱 / Falling: 0
```

### RoundEnd 결과 항목 (7바이트)

```
u8  playerId
u8  place            1 = 승자. 같은 틱에 탈락한 사람끼리 같은 순위
u32 survivedTicks    Playing 시작부터 탈락(또는 종료)까지 틱
u8  cause            0 생존, 1 칼, 2 연결 끊김
```

결과는 순위순으로 정렬되어 있다.

## 4. 접속 순서

1. 클라이언트가 연결하고 **5초 안에** Hello를 보낸다. 늦으면 서버가 끊는다.
2. 서버가 버전·인원을 확인한다. 실패하면 Reject 후 종료.
3. 성공하면 서버는 새 클라이언트에게 `Welcome` → 현재 접속자 전원(자신 포함)의 `PlayerJoined`를 보내고, 기존 접속자에게는 새 사람의 `PlayerJoined`를 보낸다. 플레이어 id는 1~8 중 비어 있는 가장 작은 번호이며, 나간 사람의 id는 재사용된다.
4. Countdown 중에 들어오면 이번 라운드 참가자가 되고, 참가자 목록이 바뀐 `RoundStart`가 **같은 roundNumber로 모두에게 다시** 간다(countdownTicks는 남은 틱). 클라이언트는 같은 번호의 RoundStart를 갱신으로 처리한다. Playing/Ended 중에 들어오면 다음 라운드까지 관전자(InRound=0, Alive=0)다.
5. 이후 서버는 매 틱 Snapshot을 모두에게 보낸다. 클라이언트는 매 틱 Input을 보낸다.
6. 서버는 10초 동안 아무 패킷도 받지 못하면 끊는다(Input을 매 틱 보내면 문제없음).
7. 연결 종료 = 퇴장. 라운드 중이면 **즉시 탈락**(cause 2)하고 모두에게 `PlayerLeft`가 간다. 이후 스냅샷에서 그 플레이어는 빠진다.

## 5. 방과 라운드 규칙 (서버)

- 방은 하나, 최대 8명. 상태는 Waiting → Countdown → Playing → Ended → (Countdown 또는 Waiting).
- **Waiting**: 접속자가 최소 인원(기본 2, 서버 옵션 `--min-players`) 미만. 모두 살아 있고 자유롭게 움직인다. 칼 없음.
- **Countdown**(3초 = 60틱): 최소 인원이 되면 시작. 현재 접속자 전원이 참가자로 정해지고 스폰 지점으로 옮겨진다(`RoundStart` 전송). 움직임은 가능, 칼 없음. 도중에 인원이 최소 미만으로 줄면 Waiting으로 돌아간다.
- **Playing**: 칼이 떨어진다. 아래 종료 조건까지.
- **Ended**(5초 = 100틱): `RoundEnd` 전송, 칼 제거. 끝나면 인원이 충분하면 다음 Countdown, 아니면 Waiting(모두 부활).
- 사망자는 관전 상태다. 스냅샷에 Alive=0으로 남고 서버는 그 입력을 적용하지 않는다(seq는 소비해 lastInputSeq는 계속 오른다).

### 종료 조건과 승자

- 참가자가 2명 이상으로 시작했으면 생존자가 1명 이하가 될 때, 1명으로 시작했으면 0명이 될 때 끝난다.
- 제한 시간 180초(3600틱)가 지나면 그 시점 생존자 전원이 공동 승자.
- 생존자가 있으면 생존자 = 승자.
- **동시 사망**: 마지막 생존자들이 같은 틱에 모두 탈락하면, 그 틱에 칼로 탈락한 사람 **전원이 공동 승자**(place 1)다.
- 연결 끊김은 다음 틱을 처리하기 전에 반영되므로 직전 틱에 탈락한 것으로 기록된다. 따라서 그 틱의 칼 탈락보다 항상 먼저 탈락한 것으로 친다.
- 연결 끊김으로 탈락한 사람은 승자가 될 수 없다. 마지막 생존자들이 모두 연결을 끊으면 승자 없음(winner 0명).
- 순위: 승자 1위, 나머지는 늦게 탈락한 순. 같은 틱 탈락은 같은 순위(1, 2, 2, 4 방식).

### 칼

- 스폰 간격(틱) = `max(4, 30 - roundTick / 100)` → 처음 1.5초마다, 5초마다 1틱씩 짧아져 최소 0.2초.
- 한 번에 떨어지는 개수 = `min(4, 1 + roundTick / 600)` → 30초마다 1개씩 늘어 최대 4개.
- x는 `[KnifeHalfWidth, Width - KnifeHalfWidth]`에서 무작위.
- Warning 20틱(1초) → Falling: `KnifeSpawnY`(14.0)에서 매 틱 700씩 낙하 → 처음 닿는 면(발판 윗면 또는 바닥)에 꽂혀 Landed 10틱 후 제거.
- 판정: Falling인 칼이 **그 틱에 지나간 영역 전체**(이전 위치 칼 윗부분 ~ 새 칼끝, 폭 0.4)와 살아 있는 플레이어 몸 상자가 겹치면 그 플레이어 사망. Warning/Landed는 무해. 칼은 처음 닿는 면에 꽂히므로 **발판 아래에 있으면 그 칼에 맞지 않는다**.
- 클라이언트는 `Stage.KnifeLandingY(x, KnifeSpawnY)`로 칼이 꽂힐 높이를 미리 그릴 수 있다.

### 대시 밀치기

모든 플레이어가 한 스텝 움직인 뒤 `DashCollision.Resolve`가 처리한다.

- 대시 중인 플레이어의 몸이 다른 살아 있는 플레이어와 겹치면, 상대는 대시 방향으로 `vx = ±900`, `vy = +500`을 받고 8틱 동안 조작 불가(stun). 공격자는 대시가 끝나고 `vx = 0`.
- 둘 다 대시 중이면 둘 다 서로 반대쪽으로 튕겨 나간다.
- 한 틱에 한 사람은 한 번만 밀린다.
- 서버만 판정한다. 클라이언트는 예측하지 않고 다음 스냅샷으로 보정된다.

### 한 틱의 처리 순서 (서버)

1. 네트워크 이벤트(접속, 입력, 끊김)를 큐에서 꺼내 반영
2. 살아 있는 플레이어마다 입력 하나를 꺼내 `PlayerPhysics.Step`
3. `DashCollision.Resolve`
4. (Playing) 칼 생성 → `KnifePhysics.Step` → 칼 판정 → 사망 처리 → 종료 검사
5. 상태 타이머 진행(Countdown/Ended)
6. Snapshot 전송

## 6. 물리 상수 (`GameConstants`)

| 이름 | 값 | 환산 |
|---|---|---|
| PlayerWidth × PlayerHeight | 800 × 1200 | 0.8 × 1.2 유닛 |
| MoveSpeed | 400 /틱 | 8 u/s, 가속 없이 즉시 |
| Gravity | 100 /틱² | 40 u/s² |
| MaxFallSpeed | 1000 /틱 | 20 u/s |
| JumpVelocity | 800 /틱 | 최고 높이 2.8 유닛 (낮은 발판 2.4에 오를 수 있음) |
| DashSpeed / DashTicks | 1200 /틱, 4틱 | 0.2초 동안 4.8 유닛, 중력 없음 |
| DashCooldownTicks | 20 | 대시 시작부터 1초 |
| KnockbackSpeed / Lift / StunTicks | 900 / 500 / 8 | 수평 속도는 매 틱 1/4씩 줄어듦 |
| KnifeWidth × KnifeHeight | 400 × 1000 | |
| KnifeWarningTicks / FallSpeed / LandedTicks | 20 / 700 / 10 | |

### `PlayerPhysics.Step(ref state, buttons, stage)` 순서

1. `pressed = buttons & ~PrevButtons`. dashTicks, dashCooldown 1 감소.
2. stun 중이면: stun 1 감소, 대시 취소, `vx -= vx / 4`. 입력 무시.
3. 아니면: Left/Right로 방향(dir) 계산, 대시 중이 아니면 facing 갱신. Dash가 눌렸고 대시·쿨다운이 0이면 대시 시작(dashTicks=4, cooldown=20). 대시 중이면 `vx = facing × 1200`, `vy = 0`. 아니면 `vx = dir × 400`이고, Jump가 눌렸고 땅 위면 `vy = 800`.
4. 대시 중이 아니면 `vy -= 100`, 최소 -1000.
5. `x += vx`, 벽(0.4 ~ 너비-0.4)에서 멈춤(stun 중 벽에 닿으면 vx = 0).
6. `y += vy`. 내려가는 중(vy ≤ 0)이면 이전 y ~ 새 y 사이에 있는 가장 높은 면에 착지: 바닥은 항상, 발판은 Down을 누르지 않았고 몸이 가로로 겹칠 때만. 착지하면 `vy = 0`, Grounded. 발판은 아래에서 위로 통과한다.
7. `PrevButtons = buttons`.

## 7. 스테이지 1 (`Stage.Default`, id 1)

- 너비 24000(24 유닛), 카메라용 높이 13500, 칼 시작 높이 14000.
- 바닥: y = 0, 0 ~ 24000, 단단함. 양 끝은 벽.
- 발판(한 방향, 위에서만 착지):

| 발판 | x 범위 | 윗면 y |
|---|---|---|
| 왼쪽 낮은 | 3000 ~ 9000 | 2400 |
| 오른쪽 낮은 | 15000 ~ 21000 | 2400 |
| 가운데 높은 | 9000 ~ 15000 | 4800 |

- 스폰 지점(참가자 순서대로): x = 1500, 22500, 4500, 19500, 7500, 16500, 10500, 13500 (모두 y = 0, 왼쪽 절반은 오른쪽을, 오른쪽 절반은 왼쪽을 봄).

## 8. 입력 시퀀스와 예측·보정 규칙

### 클라이언트

1. 자신의 고정 20Hz 틱을 돈다. 매 틱:
   - 현재 버튼을 읽고 `seq`를 1 올린다(연결마다 1부터 시작, 0은 "없음").
   - `Input(seq, buttons)`을 보낸다.
   - 내가 살아 있으면 `PlayerPhysics.Step(ref predicted, buttons, stage)`로 내 캐릭터를 바로 움직인다.
   - `(seq, buttons)`를 미확인 목록에 넣는다.
2. 스냅샷을 받으면 내 항목에서:
   - `predicted = snapshot.State`
   - 미확인 목록에서 `seq <= lastInputSeq`를 버린다.
   - 남은 입력을 순서대로 다시 `Step` 한다(재시뮬레이션).
   - 화면 위치는 이전 예측 위치에서 새 예측 위치로 짧게 보정해 튀지 않게 한다.
3. 다른 플레이어와 칼은 스냅샷 두 개 사이를 보간해서 그린다(예: 2~3틱 = 100~150ms 늦게).
4. 죽었거나(Alive=0) 관전자면 예측을 멈추고 스냅샷만 그린다. 입력은 계속 보내도 된다.

### 서버

- 플레이어별 입력 큐(FIFO). 매 틱 살아 있는 플레이어마다 **최대 하나**를 꺼내 적용하고 `lastInputSeq`를 그 seq로 둔다.
- 큐가 비어 있으면 마지막 버튼을 한 번 더 적용한다(`lastInputSeq`는 그대로). 눌림 판정은 PrevButtons 비교라 점프·대시가 반복 발동하지 않는다. 이 경우 클라이언트와 한 틱 어긋나지만 다음 스냅샷에서 보정된다.
- seq가 직전 받은 값보다 크지 않으면 무시한다.
- 큐에 8개 넘게 쌓이면(클라이언트가 너무 빨리 보냄) 오래된 것부터 버리고 `lastInputSeq`를 버린 seq까지 올린다. 버린 입력은 적용되지 않았으므로 클라이언트 예측이 잠깐 어긋나고 스냅샷으로 보정된다.
- 죽은 플레이어의 입력도 같은 방식으로 소비만 한다.
- Countdown 시작 시 서버가 참가자를 스폰 지점으로 순간이동시킨다. 클라이언트는 보통의 보정으로 따라오지만, 화면 보정 연출 없이 바로 옮기는 편이 자연스럽다(`RoundStart` 수신 시).

## 9. Unity에서 Shared 참조하기

`Shared/`는 `package.json`(이름 `com.onlinesevergame.shared`)과 `OnlineSeverGame.Shared.asmdef`(`noEngineReferences: true`, `autoReferenced: true`)를 가진 로컬 패키지다.

`Client/Packages/manifest.json`의 `dependencies`에 추가:

```json
"com.onlinesevergame.shared": "file:../../Shared"
```

- 경로는 `Client/Packages/` 기준 상대 경로다.
- 클라이언트 asmdef가 있다면 `references`에 `OnlineSeverGame.Shared`를 넣는다(없으면 Assembly-CSharp가 자동 참조).
- 네임스페이스: `OnlineSeverGame.Shared.Protocol`, `OnlineSeverGame.Shared.Simulation`.
- `dotnet build`의 bin/obj는 루트 `Directory.Build.props` 설정으로 `artifacts/`에 생기므로 `Shared/` 안에는 소스만 있다.
- Unity가 `Shared/` 안에 `.meta` 파일을 만든다. 이 파일들은 Shared와 함께 커밋한다(Shared 소유 세션이 커밋, 클라이언트 세션은 알려만 주면 됨).
- `Shared/`는 C# 9 / netstandard2.1 API만 쓴다. `record`, `init`, 전역 using, 파일 범위 네임스페이스를 쓰지 않는다.
- 소켓 수신은 별도 스레드에서 `FrameDecoder` → `PacketCodec.DecodePayload`로 하고, 해석된 패킷을 스레드 안전 큐로 메인 스레드에 넘기는 구성을 권장한다(Unity API는 메인 스레드 전용).
