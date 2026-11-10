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

## 6. 서버 설치 패키지 (2026-11-10, 조율 세션)

서버 컴퓨터에 그대로 풀어서 쓰는 패키지를 만든다. .NET 설치가 필요 없다.

```bash
python -I Tools/ServerPackage/package_server.py
```

- 결과: `artifacts/server/JellySurvivalServer-v<프로토콜>-<런타임>.tar.gz`(macOS Apple Silicon·인텔, Linux) 와 `...-win-x64.zip`. 이름의 v 번호가 프로토콜 버전이고, 플레이어 게임과 같아야 접속된다. 게임을 새로 배포할 때마다 이 스크립트를 다시 돌려 서버도 바꾼다.
- 들어 있는 것: `bin/`(서버), `server.conf`(포트 7771, 시작 인원 2, 맵 교체 방식, 추가 옵션), `README.txt`(설치·포트포워딩·확인 방법), 맥·리눅스 `start.sh`, 맥 `install-autostart-mac.sh`·`uninstall-autostart-mac.sh`(로그인 때 자동 시작하는 LaunchAgent), 윈도우 `run-server.bat`.
- `start.sh`: 서버가 꺼지면 3초 뒤 다시 켬, `logs/server.log`에 기록, 맥에서는 `caffeinate -i`로 잠자기 방지. 맥에서 처음 실행할 때 다운로드 격리 표시를 지우고, 윈도우에서 만든 실행 파일이라 서명이 없으므로 `codesign --sign -`로 이 맥용 서명을 붙인다(Apple Silicon은 서명 없는 실행 파일을 바로 종료).
- 맥·리눅스 압축본은 tar.gz로 만들어 스크립트 줄바꿈을 LF로, 실행 권한(755)을 넣었다. 윈도우에서 zip으로 만들면 둘 다 깨진다.
- 맥·리눅스는 단일 파일로 묶지 않고 폴더째 둔다(묶은 파일 안 라이브러리 서명 문제를 피하려고). 윈도우는 단일 exe.

- **작은 패키지가 기본(2026-11-10)**: Discord 무료 업로드 제한(10MB)에 맞추려고, 서버를 안 쓰는 프레임워크 코드를 잘라내고(trim) 압축한 단일 파일로 만든다. 압축본 기준 맥 Apple Silicon 5.7MB, 인텔 맥 6.2MB, 리눅스 6.3MB, 윈도우 6.0MB(예전 33~37MB). 여전히 .NET 설치는 필요 없다. 설정은 `Server/GameServer/GameServer.csproj`의 `SmallServer` 속성(명령줄 `PublishTrimmed`는 netstandard2.1 공용 프로젝트에까지 걸려 실패하므로 서버 프로젝트 안에서만 켬). 잘라낸 윈도우 서버로 봇 3명+클라이언트 한 라운드, 오류 출력 0줄 확인. 맥에서는 아직 실행해 보지 못했다. 혹시 문제가 있으면 `--full`로 예전처럼 큰 패키지를 만든다.

확인한 것: v10 패키지 4종 생성, tar 안 권한·줄바꿈 확인, 맥 스크립트 3개 `bash -n` 문법 통과, 윈도우 패키지를 `run-server.bat`으로 띄워 설정 파일 포트(시험 7795·7796)로 열리고 봇 3명 한 라운드 종료, v10 게임이 그 서버에 접속(welcome). **맥에서 실제 실행은 못 했다**(이 PC는 윈도우). 맥에서 처음 실행할 때 서명·방화벽 창이 뜨는지 사람이 확인해야 한다.

## 5. 변경 기록

| 날짜 | 커밋 | 내용 |
|---|---|---|
| 2026-11-10 | `da0499b` | 클라이언트 기본 서버 주소를 `wildmagic.co.kr:7771`로. DNS·포트 확인 결과, 맥 서버 실행 방법 정리 |
| 2026-11-10 | (이 커밋) | 서버 설치 패키지 스크립트(`Tools/ServerPackage/package_server.py`)와 6장 추가(조율 세션) |
| 2026-11-10 | (이 커밋) | 서버 패키지를 trim·압축 단일 파일로 줄임(약 6MB, Discord 업로드용). `--full` 옵션 |
