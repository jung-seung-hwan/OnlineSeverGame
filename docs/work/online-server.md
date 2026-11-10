# 작업 기록: 공개 서버 (wildmagic.co.kr:7771)

## 1. 어떻게 플레이되는가

- 클라이언트를 처음 켜면 접속 화면의 서버 주소 칸에 `wildmagic.co.kr:7771`이 들어 있다. 이름만 넣고 **접속**하면 공개 서버로 들어간다.
- 한 번 접속에 쓴 주소는 PlayerPrefs(`server`)에 저장되어 다음 실행에도 그 주소가 나온다. 그래서 **예전에 이 PC에서 켜 본 클라이언트는 저장된 `127.0.0.1:7777`이 그대로 나온다**. 칸을 지우고 새 주소를 넣으면 된다.
- 로컬 시험은 예전처럼 `-server 127.0.0.1:7777`로 주소를 덮어쓴다(명령줄 인자가 저장값보다 우선).

## 2. 어떻게 구현했는가

- 기본 주소만 바꿨다: `Client/Assets/Scripts/Game/GameBootstrap.cs`의 `ServerAddress`, 그리고 씬에 저장된 값(`Client/Assets/Scenes/Main.unity`). 씬에 저장된 값이 스크립트 기본값보다 우선하므로 둘 다 바꿔야 한다.
- 순서: 씬 값 → PlayerPrefs `server`(있으면) → 명령줄 `-server`(있으면).
- 클라이언트는 `TcpClient.Connect(host, port)`를 쓰므로 IP 대신 도메인 이름을 넣어도 DNS로 찾는다.
- 프로토콜 기본 포트(`ProtocolConstants.DefaultPort = 7777`)는 그대로 두었다. 주소에 포트를 빼먹으면 7777로 가므로 주소는 늘 `:7771`까지 적는다.
- 서버 코드는 바꾸지 않았다. 포트는 실행 옵션으로 준다.

## 3. 직접 돌려 보기

서버 컴퓨터(공인 IP `39.124.251.64`로 가는 곳)에서:

```bash
dotnet run --project Server/GameServer -- --port 7771 --min-players 2
```

맥미니에 .NET 없이 올리려면 Windows에서 빌드해 폴더째 복사한다(Apple Silicon. 인텔 맥이면 `osx-x64`).

```bash
dotnet publish Server/GameServer -c Release -r osx-arm64 --self-contained true -o publish/mac-server
```

맥에서:

```bash
xattr -dr com.apple.quarantine mac-server && chmod +x mac-server/OnlineSeverGame.Server && ./mac-server/OnlineSeverGame.Server --port 7771 --min-players 2
```

밖에서 들어오려면:

1. 공유기 포트포워딩: **TCP 7771 → 서버 컴퓨터의 내부 IP**(내부 IP는 DHCP 예약으로 고정).
2. 서버 컴퓨터 방화벽에서 들어오는 연결 허용(맥은 처음 실행 때 묻는 창에서 허용).
3. 서버 컴퓨터가 잠자기에 들어가지 않게(맥: `caffeinate -i ./mac-server/OnlineSeverGame.Server ...`).
4. 확인: 다른 네트워크(예: 휴대폰 핫스팟)의 Windows PC에서 `Test-NetConnection wildmagic.co.kr -Port 7771`이 `TcpTestSucceeded : True`. 같은 공유기 안에서는 공유기가 자기 공인 IP로 되돌려 주는 기능(헤어핀 NAT)이 없으면 도메인으로 안 붙을 수 있다. 그때는 내부 IP로 접속한다.

클라이언트: `Client/Build/Windows/Client.exe`를 켜고 접속. 서버와 클라이언트는 같은 프로토콜 버전이어야 한다(다르면 "버전 불일치"로 거부).

## 4. 확인한 것과 못 한 것

### 확인함 (2026-11-10)

- `wildmagic.co.kr`의 DNS는 `39.124.251.64`. 이 작업 PC의 공인 IP(`125.132.12.11`)와 달라서, 서버는 이 PC가 아닌 다른 곳(맥미니 쪽)이다.
- `Test-NetConnection wildmagic.co.kr -Port 7771`: **닫혀 있음**(`TcpTestSucceeded False`). 서버를 아직 켜지 않았거나 포트포워딩·방화벽이 아직이다.
- 서버를 `osx-arm64` 단독 실행 파일로 빌드하는 것은 성공(이 PC에서 publish). Windows 전용 호출은 `TickTimer`의 `timeBeginPeriod` 하나이고 `OperatingSystem.IsWindows()`로 막혀 있다.
- Windows 클라이언트를 다시 빌드(`ProjectSetup.BuildWindows`, 성공). 빌드된 씬(`Client_Data/level0`)에 `wildmagic.co.kr:7771`이 들어 있고 `127.0.0.1:7777`은 없다.
- 이 PC에서 빌드를 켜 보면 접속 화면에 예전 시험 때 저장된 `127.0.0.1:7792`가 나온다(PlayerPrefs가 기본값보다 우선, 1장의 설명대로). 저장값을 지우지 않아서(이름·언어·소리 설정도 같이 지워짐) 새 기본값이 화면에 나오는 것은 처음 켜는 PC에서만 볼 수 있고, 직접 보지는 못했다.

### 못 한 것

- 실제 맥에서 서버 실행.
- 공개 서버로 실제 접속해 한 판 하기(포트가 닫혀 있음).
- 인터넷 지연(수십 ms 이상)에서의 예측·보정 느낌. 지금까지 시험은 같은 PC·같은 네트워크뿐.
- 보안: 비밀번호·암호화가 없어 주소를 아는 누구나 들어온다. 방은 하나, 최대 8명.

## 5. 변경 기록

| 날짜 | 커밋 | 내용 |
|---|---|---|
| 2026-11-10 | (이 커밋) | 클라이언트 기본 서버 주소를 `wildmagic.co.kr:7771`로. DNS·포트 확인 결과, 맥 서버 실행 방법 정리 |
