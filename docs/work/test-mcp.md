# 테스트용 MCP 도구

단위 테스트와 서버+봇 실제 경기를 **백그라운드 작업**으로 돌리는 MCP 서버를 직접 만들었다. Claude Code 같은 MCP 클라이언트가 이 서버의 도구를 부르면, 서버가 작업을 큐에 넣고 작업 id를 바로 돌려준다. 테스트는 뒤에서 돌고, 클라이언트는 그동안 다른 일을 하다가 `wait_job`·`job_status`로 결과를 받는다.

- 코드: `Tools/TestMcp/` (C# .NET 10 콘솔, NuGet 없음)
- 등록: 저장소 루트 `.mcp.json` (서버 이름 `onlinesevergame-test`)
- 터미널용 클라이언트: `Tools/TestMcp/mcp-call.mjs` (Node)
- 결과: `artifacts/test-mcp/<시각>-<작업 id>/` 아래 로그와 `summary.json`

## 1. 무엇을 할 수 있나

| 도구 | 하는 일 | 바로 돌려주는 것 |
|---|---|---|
| `run_unit_tests` | `dotnet test` (선택: `filter`) | 작업 id |
| `run_match` | 솔루션 빌드 → 빈 포트에 서버 → 봇 N개 접속 → 정해진 라운드 뒤 둘 다 종료 | 작업 id |
| `wait_job` | 작업이 끝나거나 최대 55초가 지날 때까지 기다림 | 상태(끝났으면 요약) |
| `job_status` | 상태, 지금 단계, 걸린 시간, 돌고 있으면 마지막 로그 12줄 | 상태 |
| `job_log` | 작업 로그(build·test·server·bots) 중 원하는 것, `grep`·`tail` | 로그 줄 |
| `list_jobs` | 이 서버가 받은 작업 목록(최신 먼저) | 목록 |
| `cancel_job` | 대기 중이면 취소, 돌고 있으면 프로세스 트리째 종료 | 상태 |

`run_match` 인자: `rounds`(1~10, 기본 2), `bots`(1~8, 기본 8, 다 들어와야 라운드 시작. `dropOneAfter`가 있으면 하나 적어도 시작), `dropOneAfter`(초, 첫 라운드 중 마지막 봇을 갑자기 끊음), `seed`, `map`(1~4), `ai`(`dodge`/`random`), `verbose`, `timeoutSeconds`(기본 `rounds × 200 + 60`).

### 요약에 들어가는 것

- **단위 테스트**: 통과·실패·건너뜀 수, 실패한 테스트마다 이름·메시지·스택 앞 4줄, 가장 느린 테스트 3개, 빌드가 실패했으면 `error` 줄. `verdict`가 한 줄 결론(`all 120 tests passed` 등).
- **경기**: 서버·봇 종료 코드, 끝난 라운드 수, 라운드마다 서버의 `round N END ...` 줄(밀치기·대시·밟기·위험물 수 포함)과 그 아래 판정·입력 대기·순위 줄, 죽은 횟수, 봇별 보낸 seq / 서버가 확인한 seq, 문제 줄(서버: `Exception`·`protocol error`·`send queue overflow`·`accept failed`, 봇: `Exception`·`connect failed`·`rejected`).
- 통과 조건(경기): 봇·서버 둘 다 종료 코드 0, 끝난 라운드 수 ≥ `rounds`, 문제 줄 0개.

## 2. 어떻게 만들었나

### MCP가 실제로 주고받는 것

MCP stdio 방식은 **표준 입력/출력으로 JSON-RPC 2.0 메시지를 한 줄에 하나씩** 주고받는 것이 전부다. 도구만 있는 서버라면 메서드 네 개면 된다.

```
클라이언트 → {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18",...}}
서버      ← {"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18","capabilities":{"tools":{}},"serverInfo":{...}}}
클라이언트 → {"jsonrpc":"2.0","method":"notifications/initialized"}          (알림: id 없음, 답 없음)
클라이언트 → {"jsonrpc":"2.0","id":2,"method":"tools/list"}
서버      ← {"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"run_unit_tests","description":...,"inputSchema":{...}}, ...]}}
클라이언트 → {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"run_unit_tests","arguments":{}}}
서버      ← {"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"{ \"jobId\": \"unit-1\", ... }"}],"isError":false}}
```

- `initialize`: 클라이언트가 원한 버전이 지원 목록(`2025-06-18`, `2025-03-26`, `2024-11-05`)에 있으면 그대로, 없으면 가장 새 버전으로 답한다.
- `ping`: 빈 객체로 답한다.
- 알림(id 없는 메시지)에는 답하지 않는다. 모르는 메서드는 오류 `-32601`, 줄이 JSON이 아니면 `-32700`.
- 도구 결과는 `content`의 텍스트 하나다. 이 서버는 그 텍스트에 들여쓴 JSON을 넣는다(모델이 읽기 쉽고, 스크립트가 파싱하기도 쉽다).
- 인자가 틀렸거나 없는 작업 id면 프로토콜 오류가 아니라 `isError: true`인 도구 결과로 돌려준다. 그래야 모델이 메시지를 읽고 인자를 고쳐 다시 부른다.
- **표준 출력에는 JSON-RPC 줄만** 쓴다. 다른 글자가 한 줄이라도 섞이면 클라이언트 파서가 깨진다. 진단 문구는 표준 오류로 보낸다.

라이브러리(공식 MCP SDK 등)를 쓰지 않은 이유: 저장소 규칙이 "NuGet은 테스트 프레임워크에만"이고, 위 네 메서드는 `System.Text.Json`만으로 150줄 정도면 된다. 대신 지원하는 것은 도구(tools)뿐이다(resources·prompts·진행 알림 없음).

### 파일별 역할 (`Tools/TestMcp/`)

| 파일 | 역할 |
|---|---|
| `Program.cs` | 저장소 루트 찾기(`OnlineSeverGame.slnx`가 있는 위 폴더, 또는 `--repo`), UTF-8 stdin/stdout 준비, 서버 실행 |
| `McpServer.cs` | 줄 읽기 → JSON 파싱 → 메서드 분기. 요청마다 스레드 풀에서 처리(`wait_job`이 55초 막아도 다른 호출은 답함), 표준 출력 쓰기는 락으로 한 줄씩 |
| `Tool.cs` | 도구 정의(이름·설명·입력 스키마·처리 함수), 인자 읽기(`2`, `2.0`, `"2"` 모두 받음), 스키마 빌더 |
| `Tools.cs` | 도구 7개 목록. 시작 도구는 큐에 넣고 id만 돌려준다 |
| `JobRunner.cs` | 작업 큐(전용 스레드 하나가 들어온 순서대로 하나씩), 작업 상태, 자식 프로세스 실행과 로그 파일 |
| `Checks.cs` | 작업 본문: 단위 테스트(.trx 결과 파일 읽기), 경기(빌드·서버·봇·로그 분석) |
| `mcp-call.mjs` | 터미널용 최소 MCP 클라이언트 |

### 백그라운드 작업은 어떻게 도나

1. `run_*` 도구가 `JobRunner.Enqueue`로 작업을 만든다. 응답에는 작업 id와 앞에 밀린 작업 수만 들어 있어서 바로 돌아온다.
2. 전용 스레드 하나가 큐에서 작업을 꺼내 **하나씩** 실행한다. 작업마다 솔루션을 빌드하는데, 같은 프로젝트를 동시에 빌드하면 `obj/` 파일을 두고 서로 부딪히기 때문이다.
3. 자식 프로세스(`dotnet test`, `dotnet run`)의 출력은 줄 단위로 `<작업 폴더>/<이름>.log`에 바로 쓴다. 그래서 돌고 있는 중에도 `job_status`가 마지막 줄을, `job_log`가 전체를 읽을 수 있다.
4. 끝나면 상태를 `passed`/`failed`/`cancelled`로 바꾸고 같은 내용을 `summary.json`으로 남긴다.

### 부딪힌 문제와 결정

- **자식이 MCP 입력을 먹을 수 있다**: 자식 프로세스는 표준 입력을 물려받는다. 그대로 두면 서버·봇이 클라이언트가 보낸 JSON 줄을 읽어 버릴 수 있다. 그래서 자식의 표준 입력은 따로 만들어 바로 닫는다.
- **빌드가 끝나도 파이프가 안 닫힌다**: `dotnet build`가 띄우는 MSBuild 노드·빌드 서버가 출력 파이프를 물려받아 빌드가 끝난 뒤에도 살아 있으면, "출력 끝"을 기다리는 쪽이 영영 멈춘다. 환경 변수 `MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_USE_MSBUILD_SERVER=0`으로 노드를 남기지 않고, 프로세스가 끝나면 출력 읽기는 최대 3초만 더 기다린다.
- **서버·봇 동시 빌드 충돌**: 경기는 먼저 `dotnet build`를 한 번 하고, 서버와 봇은 `dotnet run --no-build`로 띄운다.
- **포트 충돌**: 사람이 7777에 서버를 띄워 두었을 수 있어서, 경기마다 빈 포트를 OS에서 받아 쓴다.
- **라운드 시작 시점**: `--min-players`를 봇 수로 준다. 봇 8개가 다 들어와야 라운드가 시작되어, 매번 같은 조건이 된다. `dropOneAfter`를 주면 봇 수 − 1로 준다. 처음에는 이 경우에도 봇 수를 주었다가, 첫 실행에서 봇 하나가 끊긴 뒤 라운드 2가 8번째 사람을 기다리며 시작되지 않아 시간 초과로 실패했다(아래 5장).
- **솔루션(`.slnx`)에 넣지 않았다**: MCP 클라이언트가 서버를 띄워 두는 동안 `OnlineSeverGame.TestMcp.dll`이 잠긴다. 솔루션에 들어 있으면 작업이 하는 루트 `dotnet build`가 그 dll을 덮어쓰려다 실패한다. 그래서 따로 빌드한다(`dotnet build Tools/TestMcp`).
- **클라이언트가 떠나면 작업도 끝난다**: 표준 입력이 닫히면(세션 종료) 돌던 작업의 프로세스 트리를 모두 죽이고 나간다. 서버·봇이 남아 포트를 잡고 있는 일이 없다.
- **판정 기준을 로그 문구에 둔다**: 경기 성공 여부는 서버 로그의 `round N END on map`, `death #`, `push  #` 문구와 종료 코드로 센다. 서버 로그 문구를 바꾸면 `Checks.Match`도 같이 고쳐야 한다.

## 3. 직접 써 보기

### 빌드 (처음 한 번, 그리고 `Tools/TestMcp`를 고친 뒤)

```
dotnet build Tools/TestMcp
```

MCP 클라이언트가 서버를 쓰고 있는 중에는 dll이 잠겨서 다시 빌드할 수 없다. 클라이언트(세션)를 닫거나 MCP 서버를 끊은 뒤 빌드한다.

### Claude Code에서

저장소 루트의 `.mcp.json`에 이미 등록되어 있다.

```json
{ "mcpServers": { "onlinesevergame-test": { "type": "stdio", "command": "dotnet",
    "args": ["artifacts/bin/TestMcp/debug/OnlineSeverGame.TestMcp.dll"] } } }
```

1. 이 저장소에서 **새 세션**을 연다(MCP 서버 목록은 세션을 시작할 때 읽는다). 프로젝트 MCP 서버라 처음 한 번 사용 허락을 묻는다.
2. 도구는 `mcp__onlinesevergame-test__run_unit_tests` 같은 이름으로 보인다.
3. "MCP로 단위 테스트 돌리고 끝나면 알려 줘" → `run_unit_tests` → 다른 일 → `wait_job` 식으로 쓴다.

명령줄로 등록하려면(같은 효과): `claude mcp add onlinesevergame-test --scope project -- dotnet artifacts/bin/TestMcp/debug/OnlineSeverGame.TestMcp.dll`

### Gemini CLI 등 다른 MCP 클라이언트에서

stdio MCP를 지원하면 같은 명령을 등록하면 된다. 예: Gemini CLI는 `.gemini/settings.json`에

```json
{ "mcpServers": { "onlinesevergame-test": { "command": "dotnet",
    "args": ["artifacts/bin/TestMcp/debug/OnlineSeverGame.TestMcp.dll"], "cwd": "D:/OnlineSeverGame" } } }
```

(이 저장소에는 넣지 않았다. Gemini 로그인이 아직 안 되어 확인 못 함.)

### 터미널에서 (`mcp-call.mjs`)

MCP 클라이언트가 없는 곳에서 쓰는 작은 클라이언트다. 서버를 띄우고, 초기화하고, 도구 하나를 부르고, `--wait`면 작업이 끝날 때까지 `wait_job`을 반복한다. 이 프로그램이 끝나면 서버와 작업도 끝난다.

```
node Tools/TestMcp/mcp-call.mjs --list
node Tools/TestMcp/mcp-call.mjs run_unit_tests --wait
node Tools/TestMcp/mcp-call.mjs run_unit_tests "{\"filter\":\"FullyQualifiedName~PhysicsTests\"}" --wait
node Tools/TestMcp/mcp-call.mjs run_match "{\"rounds\":2,\"dropOneAfter\":5}" --wait
```

종료 코드: 0 = 통과, 1 = 실패, 2 = 사용법·서버 문제. 진행 상황(`[unit-1] running: dotnet test (45 s)`)은 표준 오류로 나온다.

### 결과 보는 곳

`artifacts/test-mcp/20261103-153012-unit-1/` 같은 폴더에 `test.log`(또는 `build.log`·`server.log`·`bots.log`), `summary.json`, 단위 테스트면 `unit.trx`가 남는다. `artifacts/`는 git에 들어가지 않는다.

## 4. 새 도구를 더하려면

1. 검사 본문을 `Checks.cs`에 함수로 쓴다: `JsonObject 이름(JobContext ctx, ...)`. `ctx.Step("...")`으로 지금 단계를 알리고, `ctx.Run("로그이름", dotnet 인자, 제한 시간)` 또는 `ctx.Start(...)`로 프로세스를 띄운다. 돌려주는 객체에 `passed`(bool)와 `verdict`(한 줄)를 꼭 넣는다.
2. `Tools.cs` 목록에 `Tool`을 하나 더한다: 이름, 모델이 읽을 설명(무엇을 하고 무엇을 돌려주는지), `Schema`로 입력 스키마, `Run`에서 인자를 **먼저** 검사한 뒤 `jobs.Enqueue(...)`.
3. `dotnet build Tools/TestMcp` 후 세션을 다시 열거나 `node Tools/TestMcp/mcp-call.mjs --list`로 목록에 보이는지 확인한다.

## 5. 확인한 것과 남은 일

### 실제로 확인한 것

- 프로토콜 검사 17개(임시 스크립트, 서버를 stdio로 직접 띄워 줄 단위로 주고받음) 모두 통과: 버전 협상(지원 버전 그대로, 모르는 버전은 최신), 문자열 id의 `ping`, 알림 무응답, 도구 7개와 스키마, 모르는 메서드 `-32601`, JSON 아닌 줄 `-32700`(id null), 범위 밖 인자·없는 작업 id·빠진 `jobId`는 `isError` 결과, 모르는 도구 `-32602`, 작업 즉시 반환과 취소, 표준 출력에 JSON 외 글자 없음, 표준 입력이 닫히면 종료 코드 0으로 나감.
- `mcp-call.mjs run_unit_tests --wait`(2026-11-03): 156개 모두 통과, 4.9초(이미 빌드된 상태). 요약에 개수·느린 테스트 3개·`.trx` 경로가 들어옴.
- `mcp-call.mjs run_match {"rounds":2,"dropOneAfter":5,"seed":7} --wait`:
  - 첫 실행은 **실패**로 끝났다: 라운드 1(주방, 54초)은 끝났지만 봇 하나를 끊은 뒤 라운드 2가 시작되지 않았고, 460초 제한에 걸려 서버·봇을 죽였다(`killed after 460 s with 1/2 round(s) done`). 원인은 도구가 `--min-players 8`을 준 것(게임 문제 아님). 위 2장처럼 고쳤다. 이때 시간 초과 종료가 프로세스 트리째 동작해 남은 서버·봇 프로세스가 없음도 확인했다.
  - 고친 뒤: 통과, 106.6초. 라운드 1 주방 30.4초, 라운드 2 겨울 처마 61.9초, 끊긴 봇은 라운드 1에서 `Disconnected`로 7위·8위 처리, 남은 봇 7개 모두 보낸 seq 2065 = 서버 확인 seq 2065, 문제 줄 0개, 서버·봇 종료 코드 0.
- 새 Claude Code 세션에서 진짜 MCP 도구(`mcp__onlinesevergame-test__*` 7개)로 불러 봄(2026-11-03): 서버가 자동으로 붙었고, `run_unit_tests` → 도는 중 `list_jobs`(running) → `wait_job` 결과 156개 모두 통과(5.4초). `run_match {"rounds":1,"bots":4}` → `job_status`가 단계(`starting server on port …`)와 서버 로그 끝부분을 보여 줬고, `wait_job` 결과 통과(74.3초, 주방 67.6초, 사망 3, 문제 줄 0, 서버·봇 종료 코드 0). `job_log {"log":"server","grep":"END"}`는 `round 1 END` 줄과, 대소문자를 가리지 않아서 `state=Ended`가 붙은 퇴장 줄 4개를 함께 돌려줬다(설명대로의 동작).

### 확인 못 한 것

- 실패한 테스트가 있을 때 요약(메시지·스택)이 제대로 채워지는지. 지금 테스트가 모두 통과해서 실패 결과를 읽어 본 적이 없다(.trx의 `Message`·`StackTrace` 요소를 읽게만 해 둠).
- 빌드가 깨졌을 때 `buildErrors` 줄 모으기(한국어 MSBuild 출력에서도 `: error CS....` 꼴이라 잡힐 것으로 예상).
- Gemini CLI에서 등록해 부르기(로그인 미완료).

## 6. 변경 기록

- 2026-11-03 `Tools/TestMcp` MCP 서버(도구 7개, 작업 큐, 단위 테스트·경기 요약), `.mcp.json` 등록, `mcp-call.mjs` 클라이언트, 이 문서. 첫 경기 실행에서 찾은 최소 인원 문제(봇을 끊으면 봇 수 − 1) 수정, `--verbose`에서만 세어지던 밀치기 수 항목 제거. `50856c7`
- 2026-11-03 새 세션에서 진짜 MCP 도구로 `run_unit_tests`·`list_jobs`·`wait_job`·`run_match`·`job_status`·`job_log`를 불러 모두 정상 동작함을 확인(코드 변경 없음).
