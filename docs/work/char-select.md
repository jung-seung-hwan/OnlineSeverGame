# 캐릭터 선택 (젤리·젤리곰·푸딩)

담당: `[OnlineSeverGame] 캐릭터 선택 · 01` · 브랜치 `claude/char-select-fbbff8` · 프로토콜 v11

## 1. 어떻게 플레이되는가

- 캐릭터 세 종류: **젤리**, **젤리곰**, **푸딩**. 색은 예전처럼 들어온 순서(플레이어 번호 1~8)로 정해진다. 고르는 건 몸 모양뿐이다.
- **접속 화면**: 이름·서버 칸 아래에 세 칸(그림 + 이름). 접속 전에는 내 번호를 모르므로 **마지막 접속에서 받은 번호의 색**으로 보여 준다(처음이면 딸기색). 아래에 "색은 들어온 순서(플레이어 번호)로 정해져요". 고른 칸은 레몬색 테에 대기 동작으로 움직이고, 나머지는 첫 프레임을 조금 흐리게.
- **대기 화면**(대기·카운트다운): 왼쪽 접속자 카드 아래에 같은 세 칸(이번에는 내 진짜 색). 누르면 **바로** 바뀌고 모두의 화면·접속자 카드 얼굴이 그 캐릭터로 바뀐다.
- **결과 화면**: 오른쪽 '다음 맵' 아래에 작은 세 칸(이름은 제목 줄 오른쪽). 인원이 차 있으면 방이 결과 → 카운트다운으로 바로 넘어가 대기 화면을 거치지 않으므로, 다음 판 캐릭터는 주로 여기서 고른다. 여기서 고르면 **다음 라운드부터**: 단상의 내 캐릭터는 방금 뛴 캐릭터 그대로이고, 칸 아래에 "다음 라운드부터 ○○"가 뜬다. 다음 카운트다운 첫 장면부터 새 캐릭터.
- 라운드 진행 중(살아 있을 때·관전)에는 고르는 칸이 없다.
- 고른 값은 PlayerPrefs `character`(1~3)에 남아 다음 실행에도 그 캐릭터로 접속한다.
- 모든 화면에 반영: 게임 중 캐릭터(달리기·점프·대시·기절·엎드림·이모트 등 16동작 전부), 대기 화면 접속자 카드 얼굴, 결과 단상(1위 승리 동작)과 4위 아래 목록 얼굴, 관전 중 보이는 다른 플레이어, 미리보기(나 = 고른 캐릭터, 봇은 세 종류를 돌아가며).
- 봇(`Tools/BotClient`)은 무작위 종류로 들어온다.
- 판정은 캐릭터와 상관없이 같다(곰 귀·푸딩 접시가 칼에 걸려 죽는 일은 없다). 판정 모양은 세 종류 중 가장 좁은 그림에 맞춰져 있다.

## 2. 어떻게 구현했는가

### 2.1 패킷 방식과 이유

- `Hello` 끝에 `u8 character`: 접속하면서 바로 고른 캐릭터를 알린다. 접속 뒤 따로 요청을 보내면 첫 스냅샷 몇 장 동안 젤리로 보였다가 바뀌는 깜빡임이 생긴다.
  - 서버는 이 바이트를 **있을 때만** 읽는다. 예전(v9) 클라이언트의 짧은 Hello도 읽혀서 서버가 '버전 불일치' Reject를 보낼 수 있다(없으면 패킷 오류로 그냥 끊겨 사용자가 이유를 모른다).
- `PlayerJoined` 끝에 `u8 character`(그 사람의 **지금** 캐릭터): 늦게 들어온 사람은 원래 받는 접속자 명단만으로 모두의 캐릭터를 안다. 따로 '전체 캐릭터 목록' 패킷이 필요 없다.
- 바꾸기는 새 패킷 두 개: `CharacterRequest`(52, C) → 서버 검증 → `PlayerCharacter`(34, S, 모두에게). 이모트(51/33)·맵 고르기(50/RoomInfo 32)와 같은 '요청 → 서버가 확인하고 알림' 모양.
- **스냅샷에는 싣지 않음**: 플레이어마다 1바이트 × 초당 20번을 계속 보낼 이유가 없다(가끔 바뀌는 값). 바뀔 때 한 번, 들어올 때 명단으로 충분하다.
- 서버 규칙(`GameRoom.RequestCharacter`): 모르는 종류(0, 4~255)·모르는 플레이어는 버림. 대기·카운트다운이면 바로 바꾸고 알림. 진행·결과 중이면 '원하는 캐릭터'로만 기억했다가 다음 카운트다운 시작(또는 대기로 돌아갈 때)에 바꾸고 알림 — 그 틱의 `RoundStart`보다 먼저 가서 카운트다운부터 새 캐릭터로 보인다. 결과 단상은 방금 라운드를 뛴 캐릭터 그대로.
- 판정은 그대로: `Shared/Simulation`에는 캐릭터를 받는 곳이 없다. `HitShapes`는 세 종류 중 가장 좁은 그림에 맞춰져 있다(docs/work/char-motion.md).

### 2.2 서버·봇

- `Shared/Protocol`: `CharacterKind`(1 젤리, 2 젤리곰, 3 푸딩), `ProtocolConstants.MaxCharacter`·`IsValidCharacter`, 패킷 2개, Hello·PlayerJoined 필드.
- `Server/GameServer`: `RoomPlayer.Character`(지금)·`WantedCharacter`(원하는 것), `AddPlayer(name, character)`, `RequestCharacter`, `ApplyWantedCharacter`(StartCountdown·EnterWaiting에서). 연결 스레드는 `NetEventKind.Character` 이벤트로 넘긴다.
- 봇(`Tools/BotClient/Bot.cs`): 시드로 정한 무작위 종류를 Hello에 싣는다. 입력용 난수와 다른 난수열이라 같은 시드의 봇 움직임은 예전과 같다.

### 2.3 클라이언트

- 그림: `Art/sprites/characters/bear_*`, `pudding_*`(8색 시트 + `*_sheet.json`)를 `Client/Assets/Resources/Art/characters/`로 그대로 복사. 가져오기 설정은 `Client/Assets/Editor/ArtImporter.cs` 규칙(압축·밉맵 없음, 최대 4096)으로 Unity가 만든 `.meta`(젤리 시트와 같은 설정 확인).
- `View/ArtSprites.cs`: `Character(kind, colorIndex, anim)`. 시트 한 장이 2048 × 4096 비압축(약 32MB)이라 24장을 한꺼번에 읽지 않고 **처음 쓰이는 (종류, 색)만** 읽는다(예전엔 젤리 8장을 한꺼번에 읽었다). 8명 경기에서 한 번에 쓰이는 시트는 최대 8장 + 고르기 칸의 내 색 3장. 종류의 그림이 없으면 젤리로 그린다. `HasCharacter`는 json의 동작 이름만 본다(세 종류 행이 같음). 예전 `Character(colorIndex, anim)`은 젤리(타이틀 로고가 씀).
- 모델: `PlayerRender.Character`, `RosterEntry.Character`, `IGameSession.RequestCharacter`. `Model/CharacterChoice.cs`: 고른 값(PlayerPrefs `character`), 마지막 색(`lastColor`, Welcome 때 저장), 실행 인자 `-character jelly|bear|pudding`(이번 실행만), 클릭 자동 시험 중에는 저장 안 함.
- `Game/NetworkSession.cs`: Hello에 고른 값, `PlayerJoined`·`PlayerCharacter`로 플레이어별 캐릭터 표(나간 사람 것도 결과 화면을 위해 남김), 명단·그리기에 채움. `RequestCharacter`는 접속돼 있으면 `CharacterRequest`를 보낸다. 화면은 서버가 알려 준 값만 그린다(예측 안 함 — 대기 중 바로 적용이 한 번 왕복 늦을 뿐이고, 결과 화면에서 고른 것이 미리 바뀌어 보이는 일을 막는다).
- `Game/PreviewSession.cs`: 나 = 고른 캐릭터, 봇 1~7 = 젤리곰·푸딩·젤리 차례. 고르면 바로 바뀜.
- `UI/CharacterPicker.cs`: 세 칸(맵 고르기와 같은 `thumb` 모양, 고른 칸 `thumb--selected`). 접속(`connect-char-*`)·대기(`lobby-char-*`)·결과(`result-char-*`) 화면에 하나씩. 그림은 시트 프레임을 그대로(발 = 프레임 아래에서 8 px)를 칸 바닥에 맞춰 놓는다.
- `View/PlayerView.cs`, `UI/UiKit.Face`, `UI/ResultView.cs`, `UI/LobbyView.cs`: 모든 그림 호출에 종류를 넘긴다. 카드·단상을 다시 만드는 기준(sig)에 종류를 넣어 바뀌면 다시 그린다.
- 글: `Loc` 표에 `char.1~3`(젤리/Jelly, 젤리곰/Jelly Bear, 푸딩/Pudding), `char.title`, `char.next`, `char.colorNote` 두 언어.
- 클릭 자동 시험(`UI/UiTest.cs`)에 캐릭터 단계 추가: 접속 화면 칸 → 고른 값, 접속 → Hello로 그 캐릭터, 대기 화면 칸 → 서버 `PlayerCharacter`로 바로 바뀜, 결과 화면 칸 → 고른 값만 바뀌고 그려지는 캐릭터는 그대로.

## 3. 직접 돌려 보기

```bash
dotnet test
dotnet run --project Server/GameServer -- --port 7806 --min-players 4
```

```bash
Client/Build/Windows/Client.exe -server 127.0.0.1:7806 -autoconnect -name Gom -character bear
```

```bash
Client/Build/Windows/Client.exe -server 127.0.0.1:7806 -autoconnect -name Pudi -character pudding
```

```bash
dotnet run --project Tools/BotClient -- --port 7806 --count 2
```

- 클라이언트 둘이 대기 화면에서 서로를 곰·푸딩으로 보는지, 왼쪽 카드 얼굴이 맞는지, 봇 두 명이 들어오면 라운드가 시작되고 결과 단상에도 각자의 캐릭터가 서는지 본다. 결과 화면에서 다른 칸을 누르면 "다음 라운드부터"가 뜨고 다음 카운트다운에 바뀐다.
- 서버 로그: `join  #1 'Gom' Bear`, `char  #1 is now Pudding`, `char  #2 wants Jelly from the next round`.
- `-character`를 빼면 접속 화면에서 고른 값(PlayerPrefs)을 쓴다.

## 4. 확인한 것과 남은 일

확인함(main 합친 뒤 다시 돌린 결과. 합치기 전에도 같은 확인을 한 번 함):

- `dotnet build` 경고·오류 0, `dotnet test` 225개 통과. 새 `CharSelectTests` 14개: 패킷 왕복·예전(v9) Hello도 읽힘·접속 시 검증(0·9 → 젤리)과 명단 전달·늦게 들어온 사람이 그전에 바뀐 캐릭터를 앎·도중 입장 관전자·대기/카운트다운 즉시 변경과 알림·모르는 종류와 플레이어 무시·진행/결과 중 보류 후 다음 카운트다운에 적용(RoundStart보다 먼저 알림)·대기로 돌아갈 때 적용·**캐릭터를 어떻게 고르든(도중에 계속 바꿔도) 3000틱 동안 스냅샷·결과 바이트가 같음**·`Shared/Simulation`에 캐릭터 필드·인자 없음·봇 무작위 종류(세 종류 모두 나옴).
- Unity 배치 빌드 성공(컴파일 오류·경고 없음). 새 시트 `.meta`가 젤리 시트와 같은 가져오기 설정(최대 4096, 비압축, 밉맵 없음).
- 서버(7806, 최소 4명) + 클라이언트 둘(`-character bear` 한국어, `-character pudding` 영어, `-autoplay`, 1280×720) + 봇 둘, 약 110초, 4초마다 스크린샷(`artifacts/char-select/shots/`, 합치기 전 것은 `before-merge/`, Git 밖):
  - 대기 화면: 두 클라이언트가 서로를 곰·푸딩으로 봄, 접속자 카드 얼굴, 고르기 칸 세 개가 내 색(딸기·블루베리)으로.
  - 게임 중(달리기·대시·엎드림·납작 기절 포함): 두 화면 모두 곰·푸딩·봇(합치기 전 젤리 둘, 합친 뒤 곰 둘).
  - 카운트다운: 4명 카드 + 고르기 칸이 왼쪽 패널에 들어감.
  - 결과 화면: 단상의 푸딩·곰(나간 봇은 흐린 곰), 4위 줄 곰 얼굴, 오른쪽 고르기 칸이 카드 안에 들어감.
  - 서버 로그 `join  #1 'Gom' Bear`, `#2 'Pudi' Pudding`, 봇 `Bear`/`Jelly`. 클라이언트 로그 예외 0.
- 클릭 자동 시험(`-uitest`, 한국어 1280×720, 서버 7806 + 봇 둘) **35/35 통과**: 접속 화면 푸딩·곰 칸, Hello로 곰 접속, 대기 화면 푸딩 칸 → 서버가 바로 바꿈 → 곰으로 되돌림, 결과 화면 젤리 칸 → 고른 값만 바뀌고 그려지는 캐릭터(곰)는 그대로 + "다음 라운드부터 젤리" 표시(서버 로그 `char  #1 wants Jelly from the next round`), 기존 맵·설정·나가기 단계 모두.
  - 합치기 전 첫 시험은 34/35: 창이 보이는 사이 누군가 대기 화면 칸을 눌러(로그에 시험이 누르지 않은 요청 5번, 클릭 소리 4번 증가) 결과 화면 확인의 기준 캐릭터가 달라졌다. 서버 동작은 맞았고, 시험이 '누르기 전 캐릭터'를 기준으로 삼도록 고침.
- 접속 실패 시 경고 상자까지 나온 접속 화면(1280×720)이 잘리지 않음(`shots/connect-error.png`).
- 테스트 MCP(`onlinesevergame-test`)는 원본 체크아웃(D:/OnlineSeverGame)에서 돌아 이 워크트리 테스트를 못 찾는다(필터 결과 0개) → `dotnet test`를 직접 돌림.
- 그림 최종본: 그림 담당 '캐릭터·맵 아트 · 02 (Blender)' 세션에 최종본인지 물었으나 이 작업이 끝날 때까지 답이 없었다. main의 시트(시트 그림이 마지막으로 바뀐 커밋 52d5536 이후 그대로)를 그대로 복사했다. 새로 렌더하면 `Art/sprites/characters/{bear,pudding}_*.png`와 `*_sheet.json`을 `Client/Assets/Resources/Art/characters/`로 다시 복사하면 된다(.meta는 그대로 둠).

확인 못 함 · 사람이 보고 판단할 것:

- 고르기 칸의 크기·위치(대기 화면 왼쪽 아래, 결과 화면 오른쪽)와 고른 칸 표시가 잘 보이는지. 8명이 꽉 찬 대기 화면에서 왼쪽 패널 길이(계산으로는 약 650 px로 들어감, 화면으로는 4명까지만 봄).
- 접속 화면 미리보기 색 = 마지막 접속 번호의 색: 다음 접속에서 다른 번호를 받으면 색이 달라진다(안내 문구로 알림). 처음이면 딸기색.
- 결과 화면이 5초뿐이라 그 안에 고르기 빠듯할 수 있음(대기 화면을 거치지 않는 연속 경기에서 고를 곳이 결과 화면과 카운트다운 3초뿐).
- 텍스처 메모리: 시트를 쓰는 것만 읽게 바꿨지만 8명이 모두 다른 (종류, 색)이면 8장 + 고르기 칸 3장 ≈ 350MB 비압축. 압축 여부는 아트·클라이언트 쪽 결정.
- 한국어 이외 창 크기(2560×1440, 4:3), 영어 결과 화면의 고르기 칸은 영어 대기 화면만 봄.
- 시험 클라이언트가 `-autoconnect`로 접속하면서 예전부터 PlayerPrefs에 이름·서버 주소를 저장한다(이 작업 전부터 있던 동작). 그래서 이 작업의 시험 뒤 이 PC의 게임 저장값이 이름 `Gom`, 서버 `127.0.0.1:7806`, `lastColor` 1로 남아 있다. 직접 플레이할 때 접속 화면에서 고쳐야 한다.

## 5. 변경 기록

- 2026-11-10 서버·공용 패킷·봇·테스트, 프로토콜 v10(아래 병합에서 v11로 바꿈).
- 2026-11-10 클라이언트: 곰·푸딩 시트, 종류별 지연 로딩, 고르기 칸(접속·대기·결과), 모든 그림에 종류, `-character`, 클릭 시험 단계.
- 2026-11-10 main(91c92eb, 납작 기절 1.25초 = 프로토콜 v10)을 합침. 같은 번호가 겹쳐 캐릭터 선택을 **v11**로 올림(`ProtocolConstants.Version`, protocol.md, 주석). 충돌은 protocol.md 버전 기록 줄과 CLAUDE.md 첫 문단뿐(두 쪽 내용 모두 살림).
- 2026-11-10 합친 뒤 다시 확인(테스트 225, Unity 빌드, 두 클라이언트 스크린샷, 클릭 시험 35/35, 접속 실패 화면). 접속 화면 칸 높이 118 → 104, 로고 크기 계산에 경고 상자 높이 포함.
