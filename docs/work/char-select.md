# 캐릭터 선택 (젤리·젤리곰·푸딩)

담당: `[OnlineSeverGame] 캐릭터 선택 · 01` · 브랜치 `claude/char-select-fbbff8` · 프로토콜 v10

## 1. 어떻게 플레이되는가

(작성 중)

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

## 3. 직접 돌려 보기

(작성 중)

## 4. 확인한 것과 남은 일

- `dotnet test` 225개 통과(새 `CharSelectTests` 14개: 패킷 왕복·예전 Hello 읽기·접속 시 검증과 명단 전달·늦게 들어온 사람·관전 입장·대기/카운트다운 즉시 변경·진행/결과 중 보류 후 다음 카운트다운 적용·대기로 돌아갈 때 적용·**캐릭터와 상관없이 스냅샷·결과 바이트 동일**·시뮬레이션 코드에 캐릭터 인자 없음·봇 무작위 종류).

## 5. 변경 기록

- 2026-11-10 서버·공용 패킷·봇·테스트, 프로토콜 v10.
