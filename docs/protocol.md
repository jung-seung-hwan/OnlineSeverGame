# OnlineSeverGame 프로토콜 v4

서버(C# .NET 10)와 Unity 클라이언트가 같은 `Shared/` 소스를 쓴다. 이 문서는 그 소스의 설명이며, 바이트 배치가 바뀌면 `ProtocolConstants.Version`을 올리고 이 문서를 함께 고친다.

- 패킷 정의·직렬화: `Shared/Protocol/` (`Packets.cs`, `PacketCodec.cs`, `FrameDecoder.cs`)
- 물리·스테이지·상수: `Shared/Simulation/` (`GameConstants.cs`, `PlayerPhysics.cs`, `Stage.cs`, `Knife.cs`, `HitShapes.cs`, `DashCollision.cs`, `StompCollision.cs`, `Pushable.cs`)
- 버전 기록: v1 첫 버전 → **v2**(2026-10-27) 스냅샷 플레이어 flags에 Crouching·Stomped 비트, 스냅샷 끝에 밀리는 상자 목록 추가. 대시 쿨다운 40틱, 아래키 의미 변경(엎드리기, 아래+점프 = 발판 통과). → **v3**(2026-10-27, 다중 맵) `RoundStart`에 `stageId`, 스냅샷 위험물 항목에 `kind`, 상자 항목에 `crumble` 추가. 맵 4개(주방·겨울 처마·창고·디저트 가게)와 라운드마다 맵 교체, 위험물 4종, 얼음·녹는 발판. 설계 문서는 `docs/work/maps.md`. → **v4**(2026-10-27, 맵 선택·칼 판정) `RoomInfo`(32)·`MapRequest`(50) 추가: 방장이 다음 맵과 교체 방식을 바꾼다. 위험물 판정이 이동 상자 대신 그림 모양(`HitShapes`)으로 바뀜(바이트 배치 변화 없음). 클라이언트는 위험물을 내 예측 시각에 그린다. 작업 문서는 `docs/work/map-select-hit.md`.

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
| 30 | RoundStart | S | `u16 roundNumber`, `u16 countdownTicks`, `u32 playStartTick`, `u8 stageId`, `u8 n`, `u8 participantId × n`. 맵은 여기서만 바뀐다(Welcome의 stageId는 접속 시점의 맵) |
| 31 | RoundEnd | S | `u16 roundNumber`, `u32 endTick`, `u8 w`, `u8 winnerId × w`, `u8 r`, 결과 × r |
| 32 | RoomInfo | S | `u8 hostId`(0 = 아무도 없음), `u8 nextStageId`, `u8 rotation`(1 순환, 2 무작위, 3 고정), `u8 flags`(bit0 = 방장이 직접 고른 다음 맵). 새로 들어온 사람에게는 PlayerJoined 목록 뒤에 바로, 모두에게는 값이 바뀐 틱 끝(스냅샷 앞)에 |
| 40 | Ping | C | `u32 clientTime` (아무 클라이언트 시계 값) |
| 41 | Pong | S | `u32 clientTime`(그대로 반사), `u32 serverTick` |
| 50 | MapRequest | C | `u8 stageId`(0 = 그대로), `u8 rotation`(0 = 그대로, 1 순환, 2 무작위, 3 고정). 방장만 유효(아래 '맵 교체') |

### buttons (Input)

| 비트 | 값 | 의미 |
|---|---|---|
| 0 | 1 | Left (누르고 있는 동안) |
| 1 | 2 | Right |
| 2 | 4 | Jump (눌린 순간에만 발동) |
| 3 | 8 | Dash (눌린 순간에만 발동) |
| 4 | 16 | Down (땅·발판·상자 위에서 누르고 있으면 엎드림. 발판 위에서 Down을 누른 채 Jump를 누르면 그 발판 아래로 떨어짐) |

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
    위험물 × knifeCount (각 14바이트)
u8  boxCount
    밀리는 상자 × boxCount (각 23바이트)
```

플레이어 항목 (26바이트):

```
u8  playerId
u8  flags            bit0 Alive, bit1 InRound(이번 라운드 참가자), bit2 Grounded, bit3 FacingRight,
                     bit4 Crouching(엎드림), bit5 Stomped(지금 스턴이 밟기 때문 = 납작하게 그림)
i32 x, i32 y         밀리유닛
i32 vx, i32 vy       밀리유닛/틱
u8  dashTicks        남은 대시 틱(이번 틱 포함). >0이면 대시 중
u8  dashCooldown
u8  stunTicks        >0이면 밀려나거나 밟혀서 기절 중(입력 무시)
u8  prevButtons      마지막으로 적용된 버튼 (눌림 판정용)
u32 lastInputSeq     서버가 이 플레이어에게 마지막으로 적용한 입력 seq (없으면 0)
```

FacingRight가 0이면 facing = -1(왼쪽). 이 필드들이 `PlayerState` 전체이므로 클라이언트는 스냅샷 값으로 상태를 그대로 복원할 수 있다.

위험물 항목 (14바이트, 코드 이름은 첫 맵에서 온 `KnifeState`):

```
u16 knifeId
u8  kind             1 식칼, 2 고드름, 3 화물 상자, 4 포크 (HazardKind, 0은 식칼로 읽음)
u8  phase            1 Warning(예고), 2 Falling(낙하), 3 Landed(착지·부서짐)
i32 x                가운데
i32 y                끝(아래쪽 가운데). Warning 중에는 Stage.KnifeSpawnY
u16 timer            Warning: 낙하까지 남은 틱 / Landed: 사라질 때까지 남은 틱 / Falling: 떨어진 틱 수(고드름 가속에 씀)
```

밀리는 상자 항목 (23바이트):

```
u16 boxId            스테이지 정의 순서 + 1. 창고에서 착지해 상자가 된 화물 상자는 그 뒤 번호
i32 x, i32 y         아래쪽 가운데, 밀리유닛
i32 vx, i32 vy       밀리유닛/틱
u16 width, u16 height
u8  crumble          0 = 보통. >0 = 개수 제한으로 사라지기까지 남은 틱(그동안도 단단함, 클라이언트는 깜빡임)
```

크기가 상자 항목에 들어 있으므로 클라이언트는 스테이지 정의 없이도 상자를 그릴 수 있다. Grounded는 보내지 않는다(디코더는 `vy == 0`으로 채운다, 그리기 전용).

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

### 맵 교체

- 맵 4개: 1 주방, 2 겨울 처마, 3 창고, 4 디저트 가게(`Stage.All`, `Stage.ById`). 모두 24 × 13.5 유닛.
- 맵은 **새 라운드 번호의 Countdown이 시작될 때** 바뀐다(`RoundStart.stageId`). 같은 번호 RoundStart 재전송에서는 그대로. Countdown이 인원 부족으로 취소되면 다음 Countdown도 같은 맵(방장이 그사이 고른 맵이 있으면 그 맵). Waiting 중에는 마지막 맵.
- **다음 맵**(`RoomInfo.nextStageId`): 다음 새 Countdown이 쓸 맵. Countdown 중에는 지금 맵(방장이 고르지 않았다면), Playing이 시작되는 틱에 교체 방식으로 정해진다(순환 = 목록의 다음, 무작위 = 지금 맵을 뺀 무작위, 고정 = 지금 맵). 무작위도 이때 미리 뽑으므로 대기·결과 화면에 보일 수 있다.
- **방장** = 지금 접속자 중 가장 먼저 들어온 사람(id가 아니라 들어온 순서. 나간 사람의 id를 새 사람이 받아도 방장이 되지 않는다). 방장이 나가면 그다음 사람.
- `MapRequest`: 방장이 아니면 무시(서버 로그만). 목록에 없는 맵, 1~3이 아닌 rotation도 무시. 받아들이면 `stageId`가 있으면 다음 맵을 그것으로 정하고 "방장 지정"으로 표시(교체 방식이 덮어쓰지 않음), `rotation`이 있으면 교체 방식을 바꾼다(방장 지정이 없으면 다음 맵을 새 방식으로 다시 계산). **진행 중인 라운드(Countdown 포함)는 바뀌지 않고 다음 새 라운드부터** 적용된다. 그 라운드가 시작되면 방장 지정 표시는 꺼지고, 그다음부터는 교체 방식대로 그 맵에서 이어진다(예: 순환 중 4를 고르면 4 → 1 → 2 …).
- 서버 옵션은 시작값이다: `--maps cycle`(기본, 1→2→3→4→1…, 서버 시작 후 첫 라운드는 주방) / `--maps random`(직전 맵 제외 무작위) / `--map N`(N으로 시작, 고정). 방장이 바꾸면 그 뒤로는 방장 설정을 따른다. `GameRoom` 기본값(테스트)은 주방 고정.

### 위험물 (칼·고드름·화물 상자·포크)

맵마다 한 종류(`Stage.Hazard`). 크기·움직임은 `HazardSpec`.

| kind | 판정 폭 × 높이 | 예고 | 낙하 | 착지 |
|---|---|---|---|---|
| 1 식칼 | 400 × 1000 | 20틱 | 등속 700/틱 | 처음 닿는 면에 꽂혀 10틱 뒤 제거 |
| 2 고드름 | 400 × 1200 | 25틱 | 0에서 매 틱 +70, 최대 1000 (timer = 떨어진 틱 수, 속도 = min(1000, 70 × timer)) | **얼음 발판은 통과**, 다른 면에서 부서져 6틱 뒤 제거 |
| 3 화물 상자 | 1400 × 1200 | 25틱 | 등속 600/틱 | 멈춘 자리에서 밀리는 상자가 됨(아래 '창고 상자'). 이미 2단인 상자 위면 부서져 6틱 뒤 제거 |
| 4 포크 | 500 × 1200 | 20틱 | 등속 700/틱 | 꽂혀 10틱 뒤 제거 |

- 스폰: 간격(틱) = `max(IntervalMin, IntervalStart - roundTick / IntervalStepTicks)`, 한 번에 개수 = `min(CountMax, 1 + roundTick / CountStepTicks)` (`Stage.Schedule`). 첫 위험물은 Playing 1초 뒤.

| 맵 | Start | Min | Step | CountMax | CountStep |
|---|---|---|---|---|---|
| 주방 | 30 | 4 | 100 | 4 | 600 |
| 겨울 처마 | 30 | 5 | 100 | 4 | 600 |
| 창고 | 40 | 10 | 60 | 2 | 900 |
| 디저트 가게 | 30 | 5 | 100 | 4 | 600 |

- x: `Stage.HazardSlots`가 있으면 그중 하나(겨울 처마: 0.5, 1.5, …, 23.5), 없으면 `[반폭, Width - 반폭]`에서 무작위.
- 낙하 시작 높이 `Stage.KnifeSpawnY`: 14.0, 겨울 처마만 11.8(처마 아랫면 13.0에 매달린 고드름 끝).
- 판정: Falling인 위험물이 **그 틱에 지나간 영역 전체**와 살아 있는 플레이어가 겹치면 사망(결과 cause는 1). Warning/Landed는 무해. 처음 닿는 면(발판 윗면, 상자 윗면, 바닥)에서 멈추므로 **발판 아래에 있으면 맞지 않는다**. 단 고드름은 얼음 발판을 뚫는다.
- 겹침은 **그림 모양**(`HitShapes`, v4부터)으로 잰다. 이동 상자(800 × 1200, 발판·벽·밀치기·밟기·상자)와 따로다. 모양은 0.1유닛 높이 띠 여러 개(띠마다 왼쪽·오른쪽 끝)이고 스프라이트를 잰 값이다(알파 128 이상, 외곽선 포함, 10 밀리유닛 단위로 올림). 위험물은 띠마다 이전 끝 높이 ~ 새 끝 높이를 쓸고 지나간 영역, 플레이어는 지금 자세의 띠(오른쪽을 볼 때 기준, 왼쪽이면 좌우 뒤집기). 겹침 = 어떤 띠 쌍이 가로·세로 모두 겹침(끝이 닿기만 하면 아님).
  - 몸(대기·달리기·점프·낙하): 0~0.1 ±0.39, 0.1~0.2 ±0.47, 0.2~0.8 ±0.51, 0.8~0.9 ±0.46, 0.9~1.0 ±0.41, 1.0~1.1 ±0.36, 1.1~1.2 ±0.26. 머리 장식은 넣지 않는다.
  - 엎드림 = 몸 × (가로 1.15, 세로 0.6)(높이 0.72), 대시 = 앞뒤로 긴 눈물방울(-0.81~+0.83, 높이 0.9), 밀려남(스턴) = 뒤로 넘어지는 모양, 밟힘 = 밀려남 × (1.35, 0.4). 그리는 쪽(`PlayerView`)과 같은 우선순위: 스턴(밟힘) → 대시 → 엎드림 → 몸.
  - 위험물(끝 기준): 식칼 칼날 ±0.12~0.13 / 높이 1.0, 고드름 끝 ±0.03 ~ 위 ±0.22 / 1.24, 화물 상자 ±0.72 / 1.21, 포크 갈래 ±0.19 / 손잡이 ±0.1 / 1.2. 착지·쌓임·예고 기둥은 그대로 `HazardSpec` 폭을 쓴다.
  - 서버 옵션 `--legacy-hitbox`는 예전 판정(종류별 폭 상자 vs 이동 상자)으로 돌린다(전후 비교용). 라운드 끝 로그에 두 판정 비교(`hit rule …`)가 남는다.
- 클라이언트는 `PushablePhysics.HazardLandingY(stage, boxes, kind, x, KnifeSpawnY)`로 멈출 높이를 미리 그릴 수 있다(`stage`는 그 틱의 `Stage.AtRoundTick`).

### 얼음 (겨울 처마)

- 얼음 면: `Platform.Ice` 발판 위, 또는 바닥의 `Stage.FloorIce` 구간 위(몸 가운데 x 기준). `Stage.IsIceAt`.
- 틱 시작에 얼음 위에 서 있으면(`Grounded`): 걷기 `vx`를 목표 속도로 **틱당 최대 50**(`IceAccel`)씩 바꾼다. 그 전에 `vx`를 ±400으로 자른다(대시·넉백 뒤에도 걷는 속도에서 미끄러지기 시작). 손을 떼면 약 1.4유닛 미끄러진다. 공중은 그대로 즉시.
- 스턴 중 얼음 위면 `vx -= vx / 8`(보통 /4).
- 얼음 위에 서 있다 대시에 맞으면 `vy = 0`, Grounded 유지(들리지 않고 미끄러짐). 약 5유닛 밀린다(보통 약 2.4).
- 상자도 얼음 위 마찰이 25(보통 50).

### 녹는 발판 (디저트 가게)

- `Platform.MeltPerTick > 0`인 발판은 Playing 200틱(10초)부터 양 끝이 틱마다 그만큼 가운데로 줄어 `MinWidth`에서 멈춘다. `Stage.AtRoundTick(roundTick)`이 그 틱의 모양을 준다(정수 계산, 서버·클라이언트 같음). 스냅샷의 roundTick만으로 계산되므로 따로 보내지 않는다.
- 서버는 매 틱 `AtRoundTick(Playing이면 RoundTick, 아니면 0)`으로 플레이어·상자·위험물을 계산한다. 클라이언트 예측은 최신 스냅샷의 roundTick부터 재시뮬레이션 스텝마다 1씩 올려 쓴다.

### 창고 상자

- 착지한 화물 상자는 그 자리(아래쪽 가운데 = 위험물 x, 착지 높이)에 1400 × 1200 밀리는 상자로 추가된다(아래 '밀리는 상자' 규칙 그대로). 시작 상자 2개도 같은 크기.
- 상자 위 상자는 2단까지. 이미 2단 위에 떨어지면 부서진다.
- 단단한(crumble 0) 상자가 6개(`MaxRestingCrates`)를 넘으면 가장 오래된 것부터 `crumble = 20` → 20틱 뒤 사라진다. 그 위에 있던 상자·플레이어는 떨어진다(떨어지는 상자는 무해).
- Countdown 시작·Waiting 복귀 때 맵의 시작 상자만 남기고 모두 없앤다.

### 대시 밀치기

모든 플레이어가 한 스텝 움직인 뒤 `DashCollision.Resolve`가 처리한다.

- 대시 중인 플레이어의 몸이 다른 살아 있는 플레이어와 겹치면, 상대는 대시 방향으로 `vx = ±900`, `vy = +500`을 받고 8틱 동안 조작 불가(stun). 공격자는 대시가 끝나고 `vx = 0`.
- 상대가 **엎드려 있으면** 넉백이 절반(`vx = ±450`, `vy = +250`)이다. 스턴 길이는 같다.
- 둘 다 대시 중이면 둘 다 서로 반대쪽으로 튕겨 나간다.
- 한 틱에 한 사람은 한 번만 밀린다.
- 서버만 판정한다. 클라이언트는 예측하지 않고 다음 스냅샷으로 보정된다.

### 밟기

`DashCollision` 다음에 `StompCollision.Resolve`가 처리한다.

- 스스로 떨어지던(이번 틱에 y가 줄었고 스턴 중이 아닌) 플레이어 A의 발이 틱 전에는 B의 머리 높이 이상, 틱 후에는 머리 높이 이하이고 가로로 겹치면 A가 B를 밟은 것이다. 여러 명이면 머리가 가장 높은 쪽.
- B: 30틱(1.5초) 스턴, `Stomped` 표시(납작하게 그림), `vx = 0`, 대시·엎드림 취소. **이미 스턴 중이면 스턴을 새로 걸지 않는다**(계속 밟아 영원히 묶어 두지 못하게).
- A: B의 머리 위에 놓이고 `vy = +700`으로 튕겨 오른다(최고 약 2.45유닛).
- 판정은 서버. 단 클라이언트는 **밟은 쪽(자기) 튕김만** `StompCollision.PredictBounce`로 예측한다: 최신 스냅샷의 살아 있는 다른 플레이어를 각자 마지막으로 누른 버튼(`prevButtons`)으로 내 예측과 같은 틱만큼 함께 `Step`하고, 그 틱의 전/후 위치로 서버와 같은 조건(`StompCollision.Lands`)을 검사한다. 예측하지 않으면 상대를 지나쳐 바닥까지 떨어졌다가 다음 스냅샷에서 약 2유닛 위로 끌려 올라간다. 상대 기절은 예측하지 않는다(내가 밟힌 경우는 다음 스냅샷에서 멈춤으로 보정, 보정 거리가 작다).

### 밀리는 상자

스테이지 정의의 `Pushables`(아래쪽 가운데·크기)에서 라운드마다(Countdown 시작, Waiting 복귀) 다시 놓인다.

- 플레이어에게는 단단한 상자다: 옆면은 막고, 윗면은 설 수 있다(한 방향 발판이 아니므로 Down+Jump로 통과하지 않는다). 이미 겹쳐 있으면 막지 않아 빠져나갈 수 있다(갇힘 없음).
- 걸어서 옆면에 닿으면 상자 `vx = ±200`(같은 방향으로 더 빠르면 그대로). 플레이어는 그 면에서 멈추므로 미는 동안 초당 4유닛으로 느려진다.
- 대시로 옆면에 닿으면 대시가 거기서 끝나고 상자 `vx = ±700`. 땅에 닿아 있으면 매 틱 `vx`가 50씩 줄어 약 4.9유닛 미끄러진 뒤 멈춘다.
- 상자는 중력을 받고 바닥·발판(위에서)·다른 상자 윗면에 착지, 벽과 다른 상자 옆면에서 멈춘다. 미끄러지다 플레이어 옆면에 닿으면 그 플레이어를 앞으로 밀어낸다(벽에 끼면 상자가 멈춤). 상자 위에 서 있던 플레이어는 상자가 움직인 만큼 같이 옮겨진다.
- 칼은 상자 윗면에도 꽂힌다(`PushablePhysics.KnifeLandingY`).

### 한 틱의 처리 순서 (서버)

1. 네트워크 이벤트(접속, 입력, 끊김)를 큐에서 꺼내 반영
2. 살아 있는 플레이어마다 입력 하나를 꺼내 `PlayerPhysics.Step`(틱 시작 시점의 상자를 고정된 단단한 물체로 봄)
3. `DashCollision.Resolve`
4. `StompCollision.Resolve`
5. 상자: 이번 틱에 옆면을 민 플레이어의 힘 적용 → `PushablePhysics.Step` → 위에 선 플레이어 같이 옮기기
6. (Playing) 위험물 생성 → `KnifePhysics.Step`(상자 윗면 포함) → 판정 → 사망 처리 → 착지한 화물 상자를 상자로 → 종료 검사

(틱 맨 앞, 2보다 먼저: 사라지는 중인 상자의 crumble을 줄이고 0이 된 것을 없앤다.)
7. 상태 타이머 진행(Countdown/Ended)
8. Snapshot 전송

## 6. 물리 상수 (`GameConstants`)

| 이름 | 값 | 환산 |
|---|---|---|
| PlayerWidth × PlayerHeight | 800 × 1200 | 0.8 × 1.2 유닛 |
| MoveSpeed | 400 /틱 | 8 u/s, 가속 없이 즉시 |
| Gravity | 100 /틱² | 40 u/s² |
| MaxFallSpeed | 1000 /틱 | 20 u/s |
| JumpVelocity | 800 /틱 | 최고 높이 2.8 유닛 (낮은 발판 2.4에 오를 수 있음) |
| DashSpeed / DashTicks | 1200 /틱, 4틱 | 0.2초 동안 4.8 유닛, 중력 없음 |
| DashCooldownTicks | 40 | 대시 시작부터 2초 |
| KnockbackSpeed / Lift / StunTicks | 900 / 500 / 8 | 수평 속도는 매 틱 1/4씩 줄어듦 |
| StompStunTicks / StompBounce | 30 / 700 | 밟힌 쪽 1.5초 기절, 밟은 쪽 튕김 |
| CrouchHeight / CrouchMoveSpeed | 720 / 160 | 엎드리면 판정 높이 60%, 이동 40% (제안값, 플레이 후 조정) |
| CrouchKnockbackSpeed / Lift | 450 / 250 | 엎드린 채 대시에 맞으면 넉백 절반 |
| PushableWalkSpeed / DashSpeed / Friction | 200 / 700 / 50 | 상자: 걸어 밀기, 대시로 치기, 땅 마찰(틱당) |
| KnifeWidth × KnifeHeight | 400 × 1000 | |
| KnifeWarningTicks / FallSpeed / LandedTicks | 20 / 700 / 10 | 다른 위험물은 `HazardSpec`(5장 표) |
| IceAccel / IceStunDecayDivisor / IceBoxFriction | 50 / 8 / 25 | 얼음 위 걷기 가감속, 스턴 감속, 상자 마찰 |
| MeltDelayTicks | 200 | 녹는 발판이 줄기 시작하는 roundTick |
| MaxRestingCrates / CrateStackLimit / CrateCrumbleTicks | 6 / 2 / 20 | 창고 상자 |

### `PlayerPhysics.Step(ref state, buttons, stage)` 순서

`Step(ref state, buttons, stage, boxes, out contact)`: boxes는 상자 목록(없으면 null). 서버는 현재 상자, 클라이언트 예측은 최신 스냅샷의 상자를 넘긴다.

1. `pressed = buttons & ~PrevButtons`. dashTicks, dashCooldown 1 감소.
0. 틱 시작에 얼음 위에 서 있는지(`onIce`) 본다.
2. stun 중이면: stun 1 감소(0이 되면 Stomped 해제), 대시 취소, `vx -= vx / 4`(얼음이면 `/ 8`). 입력 무시.
3. 아니면: Left/Right로 방향(dir) 계산, 대시 중이 아니면 facing 갱신. Dash가 눌렸고 대시·쿨다운이 0이면 대시 시작(dashTicks=4, cooldown=40). 대시 중이면 `vx = facing × 1200`, `vy = 0`. 아니면 목표 `want = dir × (땅 위에서 Down이면 160, 아니면 400)`, `vx = want`(얼음이면 ±400으로 자른 뒤 want 쪽으로 최대 50)이고, Jump가 눌렸고 땅 위면: Down도 누르고 있고 발판 위면 **이번 틱만 발판을 무시**(아래로 떨어짐), 아니면 `vy = 800`.
4. 대시 중이 아니면 `vy -= 100`, 최소 -1000.
5. `x += vx`, 벽(0.4 ~ 너비-0.4)에서 멈춤(stun 중 벽에 닿으면 vx = 0). 이어서 상자 옆면: 이번 이동으로 새로 겹치면 그 면에 멈추고 contact에 기록, 대시 중이었으면 대시가 끝남(`vx = 0`).
6. `y += vy`. 내려가는 중(vy ≤ 0)이면 이전 y ~ 새 y 사이에 있는 가장 높은 면에 착지: 바닥은 항상, 발판은 (3의 발판 통과 틱이 아니고) 몸이 가로로 겹칠 때만, 상자 윗면은 항상. 착지하면 `vy = 0`, Grounded. 발판은 아래에서 위로 통과한다.
7. `Crouching = Grounded && Down && 대시·스턴 아님`. 엎드리면 몸 높이가 720(판정·칼·밟기 모두 이 높이).
8. `PrevButtons = buttons`.

## 7. 스테이지

겨울 처마(id 2)·창고(id 3)·디저트 가게(id 4)의 좌표·스폰·상자·장식은 `Shared/Simulation/Stage.cs`와 `docs/work/maps.md` 2장(같은 수치)에 있다. 아래는 주방.

### 스테이지 1 주방 (`Stage.Default`, id 1, 이름 "Kitchen")

- 너비 24000(24 유닛), 카메라용 높이 13500, 칼 시작 높이 14000.
- 바닥: y = 0, 0 ~ 24000, 단단함. 양 끝은 벽.
- 발판(한 방향, 위에서만 착지):

| 발판 | x 범위 | 윗면 y |
|---|---|---|
| 왼쪽 낮은 | 3000 ~ 9000 | 2400 |
| 오른쪽 낮은 | 15000 ~ 21000 | 2400 |
| 가운데 높은 | 9000 ~ 15000 | 4800 |

- 스폰 지점(참가자 순서대로): x = 1500, 22500, 4500, 19500, 7500, 16500, 10500, 13500 (모두 y = 0, 왼쪽 절반은 오른쪽을, 오른쪽 절반은 왼쪽을 봄).
- 밀리는 상자 1개(버터 블록): 아래 가운데 (12000, 0), 1200 × 1000. 높은 발판 아래.
- 흔들리는 장식(클라이언트 전용, 서버는 무시): 수건(4300, 2050 매달림), 국자(19700, 2050 매달림), 마늘(12000, 4450 매달림), 허브(8300, 0 서 있음).

### 맵 데이터에 오브젝트 넣기

`Stage` 생성자의 선택 인자 두 개로 넣는다. 다른 맵도 같은 방식으로 배치하면 서버·클라이언트가 그대로 처리한다.

```csharp
new Stage(id, name, width, viewHeight, knifeSpawnY, platforms, spawnPoints,
    pushables: new[] { new PushableSpawn(x, y, width, height) },               // 서버가 계산, 스냅샷으로 전송
    decorations: new[] { new Decoration("towel", x, y, width, height, hanging: true) }); // 클라이언트만
```

- `Decoration.Kind`는 클라이언트가 그림을 고르는 이름이다(`DecorView.Draw`). 모르는 이름은 회색 사각형으로 그린다. 매단 장식은 (x, y)가 윗변 가운데, 서 있는 장식은 아랫변 가운데이며 그 점을 축으로 흔들린다.
- 실행 중에 상자를 새로 만들려면(예: 떨어져 멈춘 화물 상자) 서버 `GameRoom`의 상자 배열에 `PushableState`를 추가한다(`LandCrate`). 스냅샷 항목에 크기가 실려 있어 클라이언트는 따로 알 필요가 없다.
- 그 밖의 선택 인자: `hazard`(위험물 종류), `schedule`(스폰 곡선), `hazardSlots`(위험물 x 후보), `floorIce`(얼음 바닥 구간). 발판은 `new Platform(l, r, top, ice: true)`, 녹는 발판은 `Platform.Melting(centerX, width, minWidth, top, meltPerTick)`.

## 8. 입력 시퀀스와 예측·보정 규칙

### 클라이언트

1. 자신의 고정 20Hz 틱을 돈다. 매 틱:
   - 현재 버튼을 읽고 `seq`를 1 올린다(연결마다 1부터 시작, 0은 "없음").
   - `Input(seq, buttons)`을 보낸다.
   - 내가 살아 있으면 `PlayerPhysics.Step(ref predicted, buttons, stage, 최신 스냅샷의 상자, out _)`로 내 캐릭터를 바로 움직이고, 다른 플레이어도 마지막 버튼으로 한 스텝 굴린 뒤 `StompCollision.PredictBounce(이전 상태, ref predicted, 다른 플레이어 전, 후)`로 머리 밟기 튕김을 반영한다.
   - `(seq, buttons)`를 미확인 목록에 넣는다.
2. 스냅샷을 받으면 내 항목에서:
   - `predicted = snapshot.State`
   - 미확인 목록에서 `seq <= lastInputSeq`를 버린다.
   - 남은 입력을 순서대로 다시 `Step` + `PredictBounce` 한다(재시뮬레이션. 상자는 최신 스냅샷 값 고정, 다른 플레이어는 최신 스냅샷에서 다시 굴리기 시작).
   - 화면 위치는 이전 예측 위치에서 새 예측 위치로 짧게 보정해 튀지 않게 한다.
3. 다른 플레이어와 상자는 스냅샷 두 개 사이를 보간해서 그린다(예: 2~3틱 = 100~150ms 늦게). **위험물은 내가 살아 있는 동안 내 예측 시각에 그린다**: 내 화면 몸은 서버 틱 `최신 스냅샷 + 미확인 입력 수`(틱 사이는 보간)이므로, 같은 틱의 위험물을 최신 스냅샷에서 `KnifePhysics.Step`으로 그만큼 앞서 계산한다(그 틱의 `Stage.AtRoundTick`, 최신 스냅샷 상자). 그러면 화면에서 칼과 몸이 닿는 순간이 서버 판정 틱과 같다. 아직 서버가 만들지 않은 위험물은 그만큼 늦게 나타난다(예고가 그만큼 짧아 보임). 죽거나 관전하면 보간 시각으로 돌아가며, 되감지 않고 잠깐 멈춘다. 착지해 상자가 된 화물은 최신 스냅샷의 새 상자로 바로 그린다.
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
