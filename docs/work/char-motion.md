# 캐릭터 모션

담당 세션: `[OnlineSeverGame] 캐릭터 모션 · 01` (워크트리 `.claude/worktrees/char-motion-3de3bd`, 브랜치 `claude/char-motion-3de3bd`)
범위: 젤리 캐릭터 동작 프레임 보강, 새 동작(착지·엎드림·납작 기절·방향 전환·눈 깜빡임), 그림과 따로 도는 출렁임, 이모트 4종(프로토콜 v7). 확정된 아트 방향(확정 사항 3·4)과 위험물 판정 모양(`Shared/Simulation/HitShapes.cs`)은 그대로 두고, 맞지 않으면 그림을 판정에 맞췄다.

## 1. 어떻게 플레이되는가

- **움직임이 부드러워짐**: 달리기 6 → 8프레임, 점프·낙하 2 → 4, 대시·밀려남 3 → 4. 점프는 막 뛰었을 때 가장 길게 늘었다가 꼭대기로 갈수록 줄어든다(반복 없음), 낙하는 납작한 채로 팔랑인다.
- **착지**: 높이 뛰었다 내려앉으면 0.15초 동안 눌렸다 펴진다(눈이 반쯤 감김).
- **방향 전환**: 좌우를 바꾸면 0.12초 동안 몸을 카메라 쪽으로 돌렸다가(정면) 반대쪽을 본다. 3D로 렌더해서 머리 장식도 같이 돈다.
- **눈 깜빡임**: 가만히 서 있으면 2~5초마다 한 번씩 깜빡인다(캐릭터마다 박자가 다름).
- **엎드림 전용 그림**: 아래키. 예전처럼 그림을 세로로 누르지 않고, 몸만 낮게 눌리고 눈썹을 찌푸린 그림. 머리 장식은 눌리지 않는다.
- **납작 기절 전용 그림**: 밟히거나 대시에 맞아 땅에 붙은 동안 뒤로 넘어진 납작한 몸 + 머리 위를 도는 노란 별 3개 + 소용돌이 눈. 공중에서 밀려 날아가는 동안은 예전대로 밀려남 그림.
- **출렁임**: 그림 프레임과 따로, 몸 전체가 발밑을 기준으로 뒤늦게 흔들린다. 출발·멈춤(위가 뒤로 처졌다가 앞으로 쏠림), 점프(길게 늘어남), 착지(눌렸다 튐), 대시·밀려남·납작 기절·엎드리기 시작(찌그러짐)에 반응해 두세 번 출렁이다 0.5초쯤에 멈춘다. 그림만 바뀌고 판정·예측과는 상관없다.
- **이모트**: Z 인사(정면을 보며 좌우로 흔듦), X 놀리기(> < 눈 + 혀 내밀기), C 기뻐하기(승리 깡충), V 슬퍼하기(처진 눈썹 + 눈물). 다른 플레이어에게도 보인다. 1.6초 동안, 서 있을 때만(움직이면 바로 끝남). 1초에 한 번. 죽었거나 관전 중이면 안 된다. 숫자 1~4·R은 방장의 맵 선택이 이미 쓰고 있어서 피했다. 화면 아래 조작 안내에 "Z·X·C·V 이모트"를 붙였다.
- 봇도 약 10초마다 무작위 이모트를 한다(다른 플레이어 이모트가 보이는지 확인용).
- 8색·머리 장식 8종이 모든 새 동작에 그대로 나온다(`Art/preview/characters_poses.png`: 세로 = 1~8번, 가로 = 16동작).

## 2. 어떻게 구현했는가

### 2.1 시트 형식 (유지한 것과 바뀐 것)

유지: 프레임 256 × 256, 128 px/유닛, 피벗 (128, 8) px = 몸 아래 가운데, 오른쪽 보기(왼쪽은 X 뒤집기), 맨 위 행이 row 0, `jelly_sheet.json`이 동작별 행·프레임 수·fps·반복을 알려 준다. 기존 8행의 순서·이름도 그대로라 예전 클라이언트 코드(`ArtSprites`가 json을 읽어 자름)로도 기존 8동작은 그대로 나온다.

바뀐 것: 시트가 1536 × 2048(6열 × 8행) → **2048 × 4096(8열 × 16행)**. json 동작 항목에 `draw_scale`([x, y], 기본 [1, 1]) 추가.

| 행 | 동작 | 프레임 | fps | 반복 | 판정 띠 (`HitShapes`) |
|---|---|---|---|---|---|
| 0 | idle 대기 | 4 | 6 | O | Body |
| 1 | run 달리기 | 6 → **8** | 12 → 14 | O | Body |
| 2 | jump 점프 상승 | 2 → **4** | 10 → 12 | O → **X** | Body |
| 3 | fall 낙하 | 2 → **4** | 10 | O | Body |
| 4 | dash 대시 | 3 → **4** | 15 → 16 | O | Dash |
| 5 | knockback 밀려남 | 3 → **4** | 12 | X | Stunned |
| 6 | death 사망 | 4 | 10 | X | (판정 없음) |
| 7 | victory 승리(= 기뻐하기 이모트) | 4 | 8 | O | Body |
| 8 | **land** 착지 | 3 | 20 | X | Body |
| 9 | **crouch** 엎드림 | 2 | 3 | O | Crouch |
| 10 | **stun** 납작 기절 + 별 | 4 | 8 | O | Stomped (`draw_scale` x 1.1) |
| 11 | **turn** 방향 전환 | 2 | 24 | X | Body |
| 12 | **blink** 눈 깜빡임 | 3 | 16 | X | Body |
| 13 | **emote_wave** 인사 | 4 | 6 | O | Body |
| 14 | **emote_taunt** 놀리기 | 4 | 8 | O | Body |
| 15 | **emote_sad** 슬퍼하기 | 4 | 4 | O | Body |

Unity 가져오기 최대 크기를 2048 → 4096으로 올렸다(`Client/Assets/Editor/ArtImporter.cs`). 2048이면 4096 높이 시트가 줄어들어 프레임 자르기가 틀어진다. 이 때문에 `Client/Assets/Resources/Art/` 아래 모든 그림의 `.meta`에서 `maxTextureSize`가 4096으로 바뀌었다(그림 자체는 캐릭터 시트만 바뀜).

### 2.2 Blender (`Art/blender/build_sprites.py`)

몸 메시·래티스·셰이프 키 6개·장식 8종(`body_bmesh`, `SHAPES`, `build_decor`, `PLAYERS`)은 건드리지 않았다. 바꾼 곳:
- `ANIMS` 표: 위 16행. `_run(i, n=8)`, `_stun(i)`.
- 프레임 키 추가: `yaw`(카메라 쪽으로 더 돌리기, 방향 전환·인사·놀리기), `sq`(발밑 기준 가로·세로 누르기), `stars`(별 위상), `dx`.
- `Squash` 빈 객체를 `View` 위 부모로 둬서 `sq`가 화면 x·y 그대로 눌리게 했다(가로 배율이 xy 같아서 30° 돌린 시점과 섞이지 않는다). 장식 기준점(`DecorAnchor`)은 그 프레임의 기울기를 0으로 세우고 누른 배율의 역수를 줘서 **장식은 눌리지 않고** 눌린 머리 꼭대기에 얹힌다.
- 얼굴 객체 추가: `Eyes_half`, `Eyes_closed`(∪), `Eyes_squint`(> <), `Eyes_sad`(안쪽이 올라간 눈썹 + 눈물), `Mouth_tongue`. 눈물 재질 `Tear` 추가.
- 별 3개(`build_stars`, 컬렉션 `Stars`): 노란 다섯 꼭지 별 + 장식과 같은 외곽선, 머리 위 타원을 돌며 뒤쪽에선 몸 뒤로 지나간다. 프레임 밖으로 나가지 않게 가운데를 −0.6~0.6 유닛으로 묶음.
- `REPORT`에 `hit_band_errors` 추가(2.3). 조정용 대상 `bands`(환경 변수 `BANDS=stun,crouch`)는 띠마다 그린 폭과 판정 폭을 찍는다.

### 2.3 엎드림·납작 기절을 판정에 맞춘 방법

판정(`HitShapes.Crouch` = 대기 몸 띠 × 가로 1.15 · 세로 0.6, `HitShapes.Stomped` = 밀려남 띠 × 1.35 · 0.4)은 예전 클라이언트가 그림을 누르던 배율 그대로 정의돼 있다. 그래서 그림도 같은 배율로 Blender에서 눌러 렌더했다(엎드림 = 대기 자세 `sq` (1.15, 0.6), 납작 기절 = 가장 많이 넘어간 밀려남 자세를 (1.227, 0.4)로 + 클라이언트가 x 1.1로 그림 = 1.35).
- Stomped 띠는 발 뒤로 1.1 유닛까지 가서 2 × 2 유닛 프레임(±1.0)을 넘는다. 그래서 납작 기절 행만 좁게 렌더하고 json `draw_scale` [1.1, 1]로 클라이언트가 넓혀 그린다.
- 렌더마다 1번 캐릭터의 몸만(장식·별 빼고) 프레임별로 찍어 100밀리 띠마다 그린 폭(알파 ≥ 0.5, 외곽선 포함)과 판정 폭을 비교한다(`band_errors`). 결과(밀리유닛, 동작 안 모든 프레임 중 최댓값):

| 동작 | 판정보다 넓게 그림 | 판정보다 좁게 그림 | 메모 |
|---|---|---|---|
| crouch | 7 | 58 | 맞음 |
| stun | 3 | 440 | 몸 쪽 띠(0.16~0.36)는 뒤 −1.06/앞 0.52 vs 판정 −1.10/0.54. 좁게 나온 440은 판정 띠 맨 위 0.48~0.56(밑 설명) |
| idle·blink·turn | 11 / 0 | 26 / 33 | |
| emote_taunt·sad | 21 / 14 | 32 / 80 | sad는 살짝 납작해 키가 0.08 낮음 |
| emote_wave | 50 | 101 | 카메라 쪽으로 돌며 좌우로 흔들어서 |
| run | 146 | 484 | **예전 달리기와 같은 값**(기울기 그대로). 판정 세션이 "0.06 안"이라고 적은 것과 다름 — 아래 '남은 일' |
| jump | 128 | 54 | 예전 점프 첫 프레임과 같은 늘임(0.35) |
| fall·land | 30 | 520 | 납작해서 키가 줄어 맨 위 띠(1.1~1.2)가 비는 것. 예전 낙하와 같음 |
| dash·knockback | 0 / 0 | — | 판정 띠가 여러 프레임 합집합이라 좁은 쪽은 안 셈 |

- Stomped 띠의 맨 위(0.48~0.56, 뒤쪽)와 앞쪽 위 모서리는 지금 밀려남 그림 어느 프레임에도 없는 부분이다(밀려남 그림 4프레임 모두 키 1.2 미만인데 Stunned 띠는 1.4까지 있음 = 예전 그림에서 잰 값으로 보임). 그림으로 채울 수 없어 남겨 두었다. 판정은 이 세션이 바꾸지 않는다.

### 2.4 클라이언트

- `View/ArtSprites.cs`: json `draw_scale`을 읽어 `Clip.DrawScale`, `Clip.Duration`, `HasCharacter(anim)`(예전 시트면 새 행 없음).
- `View/PlayerView.cs`(다시 씀):
  - 기본 동작 우선순위: 관전 → 사망 → 승리 → 납작 기절(`stun`, 없으면 밀려남 + 누르기) → 밀려남 → 대시 → 점프/낙하 → 엎드림(`crouch`, 없으면 대기 + 누르기) → 이모트(서 있을 때) → 달리기 → 대기. `HitShapes.OfPlayer`와 같은 규칙(공중 납작 기절 = 밀려남).
  - 덧동작: 대기·달리기일 때만 착지(공중 → 땅, 낙하 속도 2 u/s 넘게), 방향 전환(옛 방향 0·1프레임 → 새 방향 0프레임), 깜빡임(대기 2~5초마다)을 덮어 그린다. 다른 동작이 되면 바로 끝.
  - 출렁임: 몸·잔상을 `Jiggle` 자식에 넣고 스프링 두 개(세로 늘임·기울기, 강성 220, 감쇠 9, 최대 30% / 14°)를 프레임마다 푼다. 입력은 그리는 위치의 가로 속도 변화(2 유닛 넘게 튀면 순간이동으로 보고 무시)와 착지·점프·대시·기절·엎드리기 시작. 판정·예측 값은 읽기만 한다.
  - 예전 시트(전용 행 없음)를 넣어도 예전처럼 눌러 그린다.
- 이모트: `Model/RenderState.cs`(`PlayerRender.Emote/EmoteAge`, `EmoteTiming.Seconds` 1.6), `Model/SessionState.cs`(`IGameSession.SendEmote`), `Game/NetworkSession.cs`(보내기·받기, 내 이모트는 바로 보이고 서버 되돌림은 건너뜀, 1.1초 제한), `Game/PreviewSession.cs`(오프라인 미리보기: 바로 보임, 봇도 가끔), `Input/InputCollector.cs`(Z·X·C·V, `-autoplay motion`), `Game/GameBootstrap.cs`(접속 화면 말고는 이모트 키를 세션에 넘김), `Model/Loc.cs`(조작 안내 한국어·영어).

### 2.5 프로토콜 v7 · 서버

- `EmoteRequest`(51, C: `u8 emote`), `Emote`(33, S: `u8 playerId`, `u8 emote`), `EmoteKind` 1 인사·2 놀리기·3 기뻐하기·4 슬퍼하기, `ProtocolConstants.Version` 6 → 7, `MaxEmote`, `GameConstants.EmoteCooldownTicks` 20. 다른 패킷 배치는 그대로. `docs/protocol.md` 3장 표·'이모트 (v7)' 절·6장 표.
- 서버: `Connection`이 `NetEventKind.Emote`로 넘기고, `GameRoom.RequestEmote`가 확인(있는 플레이어, 살아 있음, 1~4, 1초 제한) 후 모두에게 `Emote`를 보낸다. 게임 상태는 바뀌지 않는다. `--verbose`면 `emote #id 종류` 로그.
- 봇(`Tools/BotClient/Bot.cs`): 틱마다 1/200 확률로 무작위 이모트.
- 테스트: `Server/GameServer.Tests/EmoteTests.cs`(패킷 왕복·모두에게 전달·1초 제한·모르는 종류·죽은 사람·관전자). `SideHazardTests`의 "버전 == 6"을 "≥ 6"으로.

### 2.6 다른 세션과 겹치는 공용 파일 (병합 때 볼 것)

'타격감·사운드 · 01'이 캐릭터 그리기 코드에 연출을 붙이므로 이 세션이 고친 공용 파일을 적는다.
- **`Client/Assets/Scripts/View/PlayerView.cs`: 거의 다시 씀.** 몸·잔상이 이제 `Jiggle` 자식 아래에 있다(루트 transform의 배율은 예전 누르기·`draw_scale`, `Jiggle`의 배율·회전은 출렁임). 연출을 붙일 때 루트 위치는 발밑, 몸 그림은 `_body`(SpriteRenderer, `Jiggle` 아래).
- `Client/Assets/Scripts/View/ArtSprites.cs`(Clip 필드·`HasCharacter` 추가), `Model/RenderState.cs`(필드 추가), `Model/SessionState.cs`(인터페이스 메서드 추가), `Game/NetworkSession.cs`·`PreviewSession.cs`(이모트), `Game/GameBootstrap.cs`(이모트 키·`-autoplay motion`), `Input/InputCollector.cs`, `Model/Loc.cs`(`hint.controls` 두 줄), `Editor/ArtImporter.cs`(4096).
- `Shared/Protocol/*`, `Shared/Simulation/GameConstants.cs`, `Server/GameServer/{Connection,GameServer,GameRoom}.cs`, `Tools/BotClient/Bot.cs`, `docs/protocol.md`, `CLAUDE.md`(이모트 한 줄, 봇·`-autoplay motion` 설명).
- 추가 캐릭터(젤리곰·푸딩) 세션에는 시작 전에 위 시트 형식(16행, 행 순서·이름, 얼굴 객체 이름)을 알렸다.

## 3. 직접 돌려 보기

```bash
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python Art/blender/build_sprites.py -- characters
```
약 1분. 끝에 `REPORT` 줄(대기 몸 범위, `hit_band_errors`, 잘린 프레임). 그다음 `-- preview`로 확인 그림, 그림을 클라이언트로 복사(`docs/work/art.md` 6.6의 `cp` 명령)하고 Unity 빌드.

```bash
BANDS=stun,crouch "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python Art/blender/build_sprites.py -- bands
```
띠마다 판정 폭과 그린 폭.

게임에서(시험 포트 7802):
```bash
dotnet run --project Server/GameServer -- --port 7802 --min-players 2 --verbose
```
```bash
dotnet run --project Tools/BotClient -- --port 7802 --count 3
```
```bash
Client/Build/Windows/Client.exe -server 127.0.0.1:7802 -autoconnect
```
Z·X·C·V로 이모트, ↓로 엎드림, 좌우를 바꿔 방향 전환, 높이 뛰어 착지를 본다. 손 안 대고 보려면 `-autoplay motion`(대기 → 달리기 → 방향 전환 → 점프·착지 → 엎드림 → 이모트 4종 → 대시를 차례로). 오프라인은 `-preview -autoplay motion`.

## 4. 확인한 것과 남은 일

확인한 것(2026-11-10):
- Blender 렌더 성공, 8장 모두 2048 × 4096, 잘린 프레임 0, 판정 띠 비교 2.3 표. `Art/preview/characters_poses.png`를 열어 8색 × 16동작에서 머리 장식 8종이 모두 나오는 것을 봤다. 대기 몸 범위는 예전과 같음(−0.50~0.50 × −0.02~1.21).
- `dotnet build`, `dotnet test` 185개 통과(새 이모트 테스트 7개 포함).
- Unity 배치 빌드 성공(컴파일 오류·경고 없음).
- 서버(7802, `--verbose`) + 봇 3 + 클라이언트 `-autoplay motion` 75초, 2라운드: 서버 로그에 이모트 20건 전달(봇 + 내 4종), 클라이언트 예외 없음. 0.5초마다 찍은 스크린샷에서 내 캐릭터를 잘라 보니 엎드림, 인사, 놀리기(혀), 납작 기절 + 별(봇에게 밟힘), 기뻐하기, 슬퍼하기(눈물), 대시 잔상, 눈 감은 깜빡임이 상태대로 나왔다. 다른 플레이어 이모트는 봇1(2번 파랑)의 슬퍼하기(눈썹·눈물)가 내 화면에 나온 장면으로 확인했다(봇은 계속 움직여서 이모트가 보이는 때가 드묾).
- 오프라인 미리보기 `-autoplay motion`을 0.04초마다 찍어 깜빡임(반쯤 → 감음), 방향 전환(옆 → 카메라 쪽 → 정면 → 반대쪽), 점프 → 낙하 → 착지 찌그러짐 → 튐, 밀려날 때 몸 기울기, 엎드림을 프레임 단위로 확인.
- 960 × 540 창에서도 조작 안내 줄이 잘리지 않음(한국어).
- 스크린샷·잘라 본 그림: `artifacts/char-motion/`(Git 제외, 빌드 출력 폴더).

확인 안 한 것 / 사람이 보고 판단할 것:
- 출렁임 세기(최대 30% 늘임·14° 기울기, 0.5초)가 손맛에 맞는지 — 정지 스크린샷으로는 판단이 어렵다. 숫자는 `PlayerView.cs` 맨 위 상수.
- 이모트 키(Z·X·C·V)와 시간(1.6초, 1초 제한)이 괜찮은지. 말풍선 같은 아이콘 없이 몸동작만이라 작은 화면에서 읽히는지.
- 방향 전환 정면 프레임에서 4번 깃발·1번 잎처럼 얇은 장식이 옆에서 보여 가늘어진다(0.04초). 거슬리는지.
- 납작 기절 얼굴이 세로 0.4로 눌려 소용돌이 눈이 가는 선처럼 보인다(별이 주된 신호).
- 텍스처 메모리: 캐릭터 시트 8장이 비압축 2048 × 4096이라 합계 약 270MB(예전 약 100MB). 압축 여부는 아트 문서에서 미뤄 둔 결정 그대로.
- 달리기 판정 차이(2.3 표의 run 146 / 484)는 이번 변경 전부터 있던 것. 판정 세션(`map-select-hit.md`)의 "0.06 안"과 다르니 판정 쪽에서 볼 일.
- 영어 조작 안내 줄이 좁은 창에서 잘리는지는 안 봄.
- `docs/work/art.md` 6.2의 시트 규격(8행·1536 × 2048)은 아트 세션 문서라 고치지 않았다. 지금 규격은 이 문서 2.1.

## 5. 변경 기록

| 날짜 | 커밋 | 내용 |
|---|---|---|
| 2026-11-10 | (이 커밋) | 시트 16행(프레임 보강 + 착지·엎드림·납작 기절·방향 전환·깜빡임·이모트 3행), 판정 띠 비교, 출렁임, 이모트(프로토콜 v7)·서버·봇·테스트, 서버+봇+클라이언트 스크린샷 확인 |
