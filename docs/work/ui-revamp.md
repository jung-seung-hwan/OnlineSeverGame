# 작업 기록: UI 개편 (게임 그림체에 맞춘 모든 화면)

- 담당 세션: `[OnlineSeverGame] UI 개편 · 01`, 브랜치 `claude/ui-revamp-570fe2`(워크트리). main 병합은 사용자 승인 뒤 조율 세션이 한다.
- 목표: 기능은 그대로 두고 모든 화면 UI를 게임 그림체(손바닥만 한 젤리, 말랑한 카툰, 어두운 저채도 배경과 선명한 캐릭터 색)에 맞게 다시 만든다. 보기와 쓰기 편하게.
- 상태: **구현·자동 확인 완료.** 프로토콜·서버 변경 없음(클라이언트만). 사람이 직접 보고 판단할 것은 8장.

---

## 1. 어떻게 보이고 쓰는가

| 화면 | 예전(IMGUI) | 지금 |
|---|---|---|
| 접속 | 검은 사각형 패널, 기본 회색 버튼, 맑은 고딕 | 움직이는 타이틀 로고 아래 둥근 젤리 카드. 큰 초록 `접속`, 파란 `오프라인 미리보기`, 보라 `설정`, `언어 · English` 칩. 접속 실패는 빨간 테두리 상자 |
| 접속 중 | 글 한 줄 + 취소 | 같은 자리 카드에 `접속 중… 주소`, 젤리 색 점 넷이 차례로 통통 뜀, `취소` |
| 대기 | 왼쪽 글 목록(색 네모), 맵 글 두 줄 + 회색 버튼 | 왼쪽 **접속자 카드**: 젤리 얼굴(플레이어 색 테) + 이름(플레이어 색) + `나`·`★방장` 배지. 위 가운데 상태 띠. 오른쪽 위 `설정`·`나가기`, 작은 로고, **다음 맵 카드**(맵 미리보기 그림 + 이름 + `방장 지정` 배지). 방장에게는 맵 그림 버튼 4개(번호 = 키 1~4)와 `순환·무작위·고정` 버튼(키 R) |
| 카운트다운 | 위 띠 글 `3초 후 라운드 시작` | 대기 화면 + 가운데 **큰 숫자**(3 포도·2 블루베리·1 라임 색)가 매 초 1.7배에서 튀어 들어오고 끝에 흐려짐, 위에 `라운드 2 · 주방` |
| 진행 HUD | 왼쪽 위 글, 오른쪽 위 글·버튼, 아래 안내 두 줄 늘 표시 | 가장자리에 작게: 왼쪽 위 `생존 3/4` + 참가자 색 점(탈락은 흐림), 위 가운데 시간, 오른쪽 위 라운드·핑과 작은 `설정`·`나가기`, 왼쪽 아래 `Shift 대시` 게이지(차면 하늘색 `준비`). 시작 순간 `시작!`이 0.8초 튀어나옴. 조작 안내는 라운드 처음 6초만. 17~22초에는 이 맵 옆 위험물 경고 띠(빨강, 깜빡임) |
| 관전 | 가운데 위 큰 검은 패널 | 시간 아래 작은 띠 두 줄(`탈락! 관전 중 · 남은 생존자 5명` / `다음 맵: 창고 (순환)`) — 떨어지는 위험물을 가리지 않게 |
| 결과 | 검은 패널 안 글 목록 | 가운데 카드가 통 튀어나옴. 왼쪽 **1~3위 단상**(금·은·동 색 블록, 2-1-3 배치) 위에 젤리가 서서 움직이고 1위는 승리 동작, 아래 이름·`생존`/`12.3초 · 눈덩이에 맞음`. 4위부터는 작은 얼굴과 두 줄 목록. 오른쪽 다음 맵 카드, `5초 후 다음 라운드` + 줄어드는 막대, `나가기` |
| 설정(새 화면) | 없음(언어 버튼만) | 어느 화면에서나 `설정` 버튼 또는 **Esc**. `언어`(한국어 / English 버튼), `소리`(음량 자리, 다른 세션이 채움. 비어 있으면 `음량 조절은 곧 추가됩니다`), `조작`(키 모양 표). 바깥 어두운 곳을 누르거나 `닫기`로 닫는다. 게임은 멈추지 않는다(온라인) |
| 이름표 | `▼ 이름`, 이름 아래 게이지 | 이름(플레이어 색, 어두운 외곽선), 내 캐릭터는 이름 위 내 색 삼각형과 이름 아래 작은 대시 게이지(예전과 같은 자리) |
| 화면 전환 | 즉시 바뀜 | 새 화면이 0.28초 동안 아래(HUD는 위)에서 통통 튀며 나타남. 타이틀 ↔ 게임으로 넘어갈 때 어두운 막이 0.35초 동안 걷힘. 결과 카드·설정 창은 작게 시작해 튀어 커짐 |

그대로인 것: 한국어/영어 전환(설정 창 또는 접속 화면 칩, PlayerPrefs `lang`, `-lang`), 방장 맵 키 1~4·R(접속 뒤 어느 화면에서나, 다음 라운드부터), 맵 버튼은 서버에 요청만 하고 화면은 서버가 돌려준 값을 보여 줌, 결과 화면의 탈락 이유 글, 접속 오류 글, Enter로 접속.

## 2. 디자인 기준

모든 화면이 이 기준을 따른다. 값은 `Client/Assets/Resources/UI/Jelly.uss` 한 파일에 있다.

### 2.1 크기와 배율

- 기준 화면 **1280 × 720 점**. 창 크기가 바뀌면 `min(폭/1280, 높이/720)` 배로 전체를 키운다(UI Toolkit `PanelSettings.scale`, 매 프레임 확인). 그래서 어느 창이든 1280×720 상자가 통째로 들어가 **잘리지 않고**, 글자는 그 크기로 다시 그려져(SDF 글꼴) 2560×1440에서도 흐리지 않다. 폭이 넓은 창(21:9 등)은 가장자리 요소가 화면 끝으로 붙고 가운데가 넓어진다.
- 간격 8점 단위, 화면 가장자리 여백 12~14점.

### 2.2 모양: 둥근 젤리

| 요소 | 모양 |
|---|---|
| 패널(카드) | 모서리 24, 어두운 잉크 `rgba(34,28,46,0.93)`, 위·옆 밝은 테(3), **아래 두꺼운 어두운 테(6)** = 말랑한 두께 |
| 젤리 버튼 | 모서리 18, 아래 그림자 테 6, **위쪽 광택 띠**(흰 20%). 올리면 1.04배로 부풀고(ease-out-back), 누르면 4점 눌려 내려가며 납작해짐(아래 테 2, 세로 0.96) |
| 작은 버튼 | 높이 34, 모서리 14, 아래 테 4 |
| 알약(pill) | HUD 글 상자. 모서리 18, 반투명 |
| 젤리 얼굴 | 원 안에 캐릭터 스프라이트를 2.2배로 키워 머리·눈이 보이게, 테두리 = 플레이어 색 |
| 맵 그림 | 모서리 16, 어두운 테, 아래에 이름 띠 |
| 키 모양 | 크림색 작은 키캡, 아래 테 3 |

### 2.3 색

| 쓰임 | 색 |
|---|---|
| 배경 잉크(패널) | `#221C2E` 93%, 테 `#423658`/`#54466E`, 아래 테 `#14101C` |
| 글 | 크림 `#FFF6E8`, 흐린 글 `#BDB3CF` |
| 주요 버튼(접속) | 라임 `#4DC35A` / 아래 `#288038` |
| 보조 버튼(미리보기) | 블루베리 `#3F6FE6` / `#24449E` |
| 설정 버튼 | 포도 `#9A5DDB` / `#623696` |
| 보통 버튼(나가기·닫기) | 잉크 보라 `#4A3D63` / `#2B233C` |
| 고른 것(선택·방장·승리 글) | 레몬 `#F0C93A` / `#A68216`, 글은 어두운 갈색 |
| 플레이어 표시(카드·점·이름표·단상 젤리) | 플레이어 8색(`PlayerColors`) 그대로 |
| 단상 | 1위 레몬, 2위 하늘(소다), 3위 주황(귤) — 플레이어 색 표에서 고름 |
| **경고(위험물 빨강 `#FF4033`)** | **경고에만**: 접속 실패 상자, 옆 위험물 직전 경고 띠. 나가기·탈락 같은 글에는 쓰지 않는다 |

`나가기`를 빨강으로 하지 않은 이유: 게임 안에서 빨강은 "곧 칼이 떨어진다"는 뜻이라, UI에 빨강이 흔하면 예고의 대비가 약해진다(아트 기준 '위험물 예고만 최고 대비').

### 2.4 글꼴

- **배달의민족 주아체(Jua)**, SIL Open Font License 1.1. 출처 Google Fonts 저장소(`github.com/google/fonts/ofl/jua`, 저작권 "The Jua Project Authors", 디자이너 Woowahan Brothers). 파일 `Client/Assets/Resources/UI/Fonts/Jua-Regular.ttf`, 라이선스 전문 `Jua-OFL.txt`(같은 폴더, 빌드에도 들어감). OFL이라 게임에 넣어 팔아도 되고 출처 표기·라이선스 동봉 조건만 지키면 된다. 글꼴 파일만 따로 팔 수는 없다.
- 고른 이유: 둥글고 굵은 손글씨풍이라 젤리 그림체와 맞고, 한글과 영문이 한 글꼴에 있다.
- 빠진 글자: KS X 1001 한글 2,350자만 있어 드문 음절(예 `힣`)과 기호 `← → ↑ ↓ · — … ▼`가 없다. 그래서 **OS 글꼴을 대체 글꼴로** 이어 붙였다(맑은 고딕 → Apple SD Gothic Neo → Noto Sans CJK KR → Noto Sans KR → Arial 중 처음 찾은 것, `UiFonts.cs`). 빠진 글자만 그 글꼴로 그린다. Windows에서는 로그에 `[UI] fallback font: Malgun Gothic`.
- 크기(점): 큰 숫자 150, 제목 40, 소제목 28, 버튼 22(작은 버튼 17), 본문 20, 작은 글 16, 아주 작은 글 14, 이름표 15.

### 2.5 움직임

- 나타남: 0.25~0.4초, ease-out-back(살짝 넘쳤다 돌아옴 = 말랑함). 사라짐은 즉시(게임 반응 우선).
- 버튼: 올림 0.12초 부풀기, 누름 0.08초 눌림.
- 반복 움직임은 플레이를 가리지 않는 곳에만: 타이틀 로고, 접속 중 점, 카운트다운 숫자, 경고 띠 깜빡임, 단상 젤리.

## 3. UI 방식 결정: UI Toolkit

| | IMGUI 다듬기 | uGUI | **UI Toolkit (고름)** |
|---|---|---|---|
| 추가 패키지 | 없음 | `com.unity.ugui` + TextMeshPro 기본 자원(셰이더) 가져오기 필요 | 없음(`com.unity.modules.uielements` 이미 있음) |
| 해상도 배율 | 직접 계산(모든 사각형·글자 크기에 배율 곱함). `GUI.matrix`로 키우면 글자가 흐려짐 | CanvasScaler | `PanelSettings.scale` 하나 |
| 잘림 | 사각형을 손으로 잡아 글이 넘치면 잘림(이전 점검에서 15건 고침) | Layout Group | **flexbox 배치**: 상자가 글에 맞춰 늘어남, 줄바꿈·말줄임 |
| 둥근 모서리·테·그림자 | 텍스처를 만들어 9-slice | 스프라이트 필요 | USS `border-radius`, 테 굵기·색으로 바로 |
| 움직임 | 매 프레임 계산 | Animator/코드 | USS transition(누름·올림) + 코드 |
| 클릭 자동 시험 | 실행 중 이벤트를 넣을 길이 없음(OS 마우스 흉내는 창 포커스에 좌우) | EventSystem 레이캐스트 | **패널에 포인터 이벤트를 넣으면 실제 마우스와 같은 길로 처리**, 그 점의 맨 위 요소도 고를 수 있음 |
| 코드로만 만들기(에디터 화면 없이 배치 모드) | 쉬움 | 번거로움(프리팹·캔버스) | 쉬움(요소를 코드로 만들고 USS 한 파일) |
| 다른 세션이 붙이기 | 쉬움 | 보통 | `UiHooks` + `UiKit` 함수로 쉬움 |

결정: **UI Toolkit.** 해상도·잘림·둥근 모양·클릭 시험 네 가지 요구를 가장 적은 코드로 만족하고, 새 패키지가 필요 없어 `manifest.json`을 건드리지 않는다(다른 세션 브랜치와 충돌 없음).

알려진 위험과 대처:
- 한글 입력(IME): 이름 칸은 UI Toolkit `TextField`. 자동 시험으로는 IME 조합(자모 → 음절)을 확인할 수 없어 **사람 확인 항목**(8장). 문제가 있으면 이름 칸만 IMGUI로 되돌릴 수 있다.
- 테마: 런타임 기본 테마(`Resources/UI/JellyTheme.tss` = `@import url("unity-theme://default")`)를 깔고 그 위를 `Jelly.uss`로 덮는다. 기본 버튼 모양(`unity-button` 클래스)은 젤리 버튼에서 뺐다.
- 버튼은 키보드 포커스를 받지 않는다(`focusable = false`): 포커스가 남으면 게임 중 Space(점프)가 마지막에 누른 버튼(예: 나가기)을 다시 누르기 때문.

## 4. 어떻게 구현했는가

| 파일 | 내용 |
|---|---|
| `Client/Assets/Scripts/UI/GameUI.cs` | 뿌리. `PanelSettings`·`UIDocument`를 코드로 만들고 배율을 맞춘다. 화면 상태(`SessionState.Screen`)로 어느 뷰를 보일지 고르고, 전환 움직임·어두운 막, Esc(설정)·Enter(접속), 방장 키 1~4·R(`OnGUI` 키 이벤트, 예전과 같은 처리). `UiView`(나타남 움직임) 기본 클래스 |
| `UI/ConnectView.cs` | 접속·접속 중. 로고 크기는 접속 카드 높이로만 정해 접속 중으로 넘어가도 로고가 그대로 |
| `UI/LobbyView.cs` | 대기·카운트다운(접속자 카드, 상태 띠, 큰 숫자), `HintBar`(조작 안내 두 줄, 줄바꿈) |
| `UI/MapPicker.cs` | 다음 맵 카드(대기·결과 공용). 미리보기 `Resources/UI/maps/map1~4.png` |
| `UI/HudView.cs` | 진행·관전 HUD, `시작!`, 옆 위험물 경고 띠, 확장 자리 5곳 |
| `UI/ResultView.cs` | 결과 카드, 단상, 4위 아래 목록, 다음 라운드 막대 |
| `UI/SettingsView.cs` | 설정 창(언어·소리 자리·등록 칸·조작) |
| `UI/NameTagLayer.cs` | 이름표, 내 삼각형, 이름 아래 대시 게이지(월드 → 패널 좌표 `RuntimePanelUtils.CameraTransformWorldToPanel`) |
| `UI/TitleLogo.cs` | 로고 모양·움직임 계산은 그대로, 그리기만 IMGUI → `Painter2D`(`TitleLogoElement`). 젤리 넷은 스프라이트 요소 |
| `UI/UiKit.cs` | 젤리 버튼·글·얼굴·키캡·슬라이더 만들기 도구, 버튼 이름표(시험용) |
| `UI/UiFonts.cs` | Jua + OS 대체 글꼴 |
| `UI/UiHooks.cs` | 다른 작업이 붙는 자리(5장) |
| `UI/UiTest.cs` | 클릭 자동 시험(7장) |
| `Model/Loc.cs` | 새 글 29개(두 언어 모두), 시험 중 언어 저장 끄기(`Loc.Persist`) |
| `Game/GameBootstrap.cs` | `-uitest` 설명, 시험 중 이름·서버 주소 저장 안 함 |
| `Resources/UI/` | `Jelly.uss`(디자인 기준), `JellyTheme.tss`, `Fonts/`(Jua + OFL), `maps/`(맵 그림 4장) |

- 글은 매 프레임 지금 언어로 넣는다(바뀐 경우만 실제로 바꿈). 그래서 언어를 바꾸면 모든 화면이 바로 바뀐다. 카드·단상처럼 요소를 새로 만드는 곳은 명단·순위·언어로 만든 서명이 바뀔 때만 다시 만든다.
- 버튼 글은 버튼 안의 `Label`(`.jb-label`)에 넣는다. 광택 띠 같은 자식이 있는 버튼은 UI Toolkit이 글 폭을 재지 못해 폭이 0으로 줄었기 때문이다(첫 촬영에서 발견). 글을 바꿀 때는 `UiKit.SetText(button, ...)`.
- 맵 미리보기 그림은 `Art/sprites/<맵>/*_full.png`를 640폭으로 줄인 것. 맵 레이아웃·비주얼 작업으로 맵 그림이 바뀌면 다시 만들어야 한다(6장 명령).

## 5. 다른 작업이 붙는 자리 (`UiHooks`)

`GameUI.cs`를 고치지 않고 자기 파일에서 등록만 한다. 등록 시점은 상관없다(화면이 만들어진 뒤면 바로 다시 그림).

```csharp
// 설정 창 '소리' 칸 채우기(음량). "sound" 칸은 제목과 자리가 이미 있다.
UiHooks.AddSettingsSection("sound", 10, box =>
{
    UiKit.SliderRow("slider-master", Loc.T("sound.master"), Volume.Master, v => Volume.Master = v, box);
    UiKit.SliderRow("slider-sfx", Loc.T("sound.sfx"), Volume.Sfx, v => Volume.Sfx = v, box);
});
// 다른 칸: id 가 "sound" 가 아니면 소리 아래에 titleKey 제목을 달아 붙는다.
UiHooks.AddSettingsSection("graphics", 20, box => { /* ... */ }, titleKey: "settings.graphics");

// HUD 자리: TopLeft(생존 수 아래) · TopRight(버튼 아래) · BottomLeft(대시 게이지 옆) · BottomRight(비어 있음, 이모트 추천) · BottomCenter
UiHooks.AddHudElement(HudSlot.BottomRight, slot =>
{
    UiKit.Button("btn-emote-1", Loc.T("emote.1"), () => SendEmote(1), "jb--small", slot);
});

// 소리 붙이기
UiHooks.ButtonClicked += name => Sfx.Play("ui_click");   // 모든 젤리 버튼, 이름과 함께
UiHooks.ScreenChanged += (from, to) => Music.OnScreen(to); // 화면 전환
```

- 설정 칸은 언어가 바뀌면 다시 만든다(그래서 build 안에서 `Loc.T`를 쓰면 된다).
- 새 버튼은 `UiKit.Button`으로 만들면 모양이 같고 키보드 포커스를 받지 않으며, 이름으로 클릭 시험에 넣을 수 있다.
- 새 글은 `Loc` 두 표(한국어·영어)에 같은 키로 넣는다.
- 타격감·사운드 · 01, 캐릭터 모션 · 01 세션에 이 자리를 알렸다(2026-11-10). 그 세션들이 IMGUI로 `GameUI.cs`에 UI를 넣었다면 병합할 때 이 자리로 옮긴다.

## 6. 직접 돌려 보기

```bash
"C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe" -batchmode -nographics -projectPath Client -executeMethod OnlineSeverGame.Client.EditorTools.ProjectSetup.BuildWindows -quit -logFile build.log
```

서버 없이 모든 화면(8명, 미리보기: 대기 → 카운트다운 → 진행 → 관전 → 결과):

```bash
Client/Build/Windows/Client.exe -preview -lang en -screen-width 2560 -screen-height 1440 -popupwindow
```

서버와 봇으로(방장 맵 고르기·결과):

```bash
dotnet run --project Server/GameServer -- --port 7803 --min-players 2
```

```bash
Client/Build/Windows/Client.exe -server 127.0.0.1:7803 -autoconnect
```

```bash
dotnet run --project Tools/BotClient -- --port 7803 --count 3
```

보면 되는 것: 접속 카드와 로고, 설정(Esc), 언어 버튼, 대기 화면의 젤리 카드와 맵 그림 버튼(방장), 카운트다운 숫자, HUD 가장자리, 20초 직전 빨간 경고 띠, 결과 단상.

맵 미리보기 그림 다시 만들기(PowerShell, 저장소 루트):

```powershell
Add-Type -AssemblyName System.Drawing
$src = @{1="Art\sprites\map\kitchen_full.png";2="Art\sprites\map_winter\winter_full.png";3="Art\sprites\map_warehouse\warehouse_full.png";4="Art\sprites\map_dessert\dessert_full.png"}
foreach ($k in $src.Keys) { $i=[Drawing.Image]::FromFile("$PWD\"+$src[$k]); $h=[int]($i.Height*640/$i.Width); $b=New-Object Drawing.Bitmap 640,$h; $g=[Drawing.Graphics]::FromImage($b); $g.InterpolationMode='HighQualityBicubic'; $g.DrawImage($i,0,0,640,$h); $b.Save("$PWD\Client\Assets\Resources\UI\maps\map$k.png"); $g.Dispose(); $b.Dispose(); $i.Dispose() }
```

## 7. 버튼 클릭 자동 시험 (`-uitest`)

사람 손으로 확인되지 않았던 버튼 클릭(map-select-hit.md '사람이 확인할 것: 버튼 클릭')을 자동으로 확인한다.

- 방법: 버튼 한가운데 점에서 **패널이 고르는 맨 위 요소가 그 버튼인지** 먼저 본다(다른 요소에 가려졌거나, 화면 밖이거나, 꺼져 있으면 실패). 그다음 그 점에 마우스 누름·뗌 포인터 이벤트를 패널에 넣는다. 실제 마우스와 같은 길(`PointerDown → PointerUp → Clickable → clicked`)로 처리되며 버튼 함수를 직접 부르지 않는다. 결과는 버튼이 바꾼 상태로 확인한다: 화면 전환, 언어, **서버가 돌려준** 다음 맵·교체 방식(RoomInfo).
- 시험이 '가려짐'을 제대로 잡는지도 확인한다: 설정 창이 열린 동안 그 아래 `접속` 버튼 자리의 맨 위 요소가 설정 창이어야 통과.
- 실행(포트 7803, 서버 최소 2명, 시험 클라이언트가 먼저 들어가 방장이 되고 약 25초 뒤 봇 2개가 들어와 라운드 시작):

```bash
dotnet run --project Server/GameServer -- --port 7803 --min-players 2
```

```bash
Client/Build/Windows/Client.exe -uitest -uitestshots D:/tmp/uitest -name UiTester -server 127.0.0.1:7803 -lang ko -autoplay
```

```bash
dotnet run --project Tools/BotClient -- --port 7803 --count 2 --ai random
```

  로그(`%USERPROFILE%/AppData/LocalLow/OnlineSeverGame/OnlineSeverGame/Player.log` 또는 `-logFile`)에 `[UITest] PASS …` / `FAIL …`, 끝에 `[UITest] DONE pass=28 fail=0`, 종료 코드 0(모두 통과) / 1. `-uitestshots` 폴더에 단계별 스크린샷.
- 시험 순서(28개): 접속 화면 `설정` → 설정 창 열림 → (가려짐 확인) → `English` → 언어·버튼 글 바뀜 → `한국어` → `닫기` → 접속 화면 언어 칩 두 번 → `접속` → 대기 화면 → 방장 됨 → 맵 버튼 3·2·4·1(서버 값) → `무작위`·`고정`·`순환` → 대기 `설정`·`닫기` → 봇 들어옴 → 카운트다운 → 진행 → HUD `설정`·`닫기` → 결과 → 결과 맵 버튼 2·`무작위` → `나가기` → 접속 화면.
- 시험 중에는 이름·서버 주소·언어를 저장하지 않는다(다른 세션·사용자 저장값 보호).
- 확인 못 하는 것: 실제 OS 마우스 커서·창 포커스, 키 1~4·R(키 경로는 예전 코드 그대로이고 map-select-hit 세션이 확인), Esc·Enter.

## 8. 확인한 것과 남은 일

### 확인함 (2026-11-10, Windows 빌드, 화면 3440×1440 PC)

- `dotnet build` 경고·오류 0, `dotnet test` 178개 통과(서버·공용 코드는 바꾸지 않음). Unity 배치 빌드 성공, 컴파일 경고·오류 0.
- **클릭 자동 시험**: 서버(7803) + 시험 클라이언트 + 봇 2. 한국어 1280×720 **28/28 통과**, 영어 2560×1440(`-popupwindow`) **28/28 통과**. main을 합친 뒤(`698e09d`)와 마지막 수정 뒤에도 같은 시험 다시 통과, `dotnet test` 178개 통과. 서버 로그에 버튼으로 보낸 요청이 그대로 찍힘(`host #1 set next map 3 'Warehouse'` … `rotation Random`, 결과 화면 중 `state Ended`에서 맵 2).
- **화면 촬영**(한국어·영어 × 1280×720·2560×1440, 각 46장, 클라이언트 로그 예외 0): 접속, 접속 중(연결 안 되는 주소), 대기(8명 카드, 방장 맵 고르기), 카운트다운 숫자, `시작!`, 진행 HUD, 17~22초 옆 위험물 경고 띠(겨울 처마), 관전, 결과(단상·4~8위 목록·다음 라운드 막대), 설정 창. 글 잘림·네모 글자(□) 없음. 2560×1440에서 글자·모서리가 흐리지 않음. 스크린샷은 Git 밖 `artifacts/ui/`(gallery, uitest, 바꾸기 전 `shots/before-*`).
- 촬영하면서 고친 것: (1) 광택 띠가 든 작은 버튼의 폭이 0으로 줄어 글이 잘림 → 글을 안쪽 Label로. (2) 접속 화면 카드·로고가 세로로 눌림 → 줄어들지 않게. (3) 미리보기에서 방장이 없는데 `★방장`이 붙음(예전 IMGUI에도 있던 것) → 방장 번호 0이면 표시 안 함. (4) 관전 패널이 떨어지는 위험물 자리를 가림 → 시간 아래 작은 띠로. (5) 클릭 시험이 시작 언어를 되돌리지 않음. (6) 영어 맵 키 안내(`Keys 1–4: next map · R: rotation (from the next round)`)가 맵 카드 오른쪽 밖으로 넘침 → 줄바꿈.

### 확인 못 함 / 사람이 보고 판단할 것

- **보기 좋은지**(색·크기·젤리 느낌), 버튼을 실제 마우스로 눌렀을 때의 손맛(부풀기·눌림), 화면 전환 길이.
- **한글 이름 입력(IME 조합)**: UI Toolkit `TextField`. 자동 시험으로는 확인 못 함. 문제면 이름 칸만 되돌릴 수 있다.
- Esc(설정)·Enter(접속)·키 1~4·R은 코드 경로만(키 처리는 예전과 같음).
- 대기 화면 아래 조작 안내 띠가 바닥에 선 캐릭터 발과 몇 점 겹친다(반투명). HUD 왼쪽 아래 대시 게이지도 화면 맨 왼쪽 아래에 선 캐릭터와 살짝 겹칠 수 있다.
- 4:3처럼 좁은 창, 1280×720보다 작은 창(배율이 1 아래로 내려가 작게 보임), 맥·리눅스(대체 글꼴 이름이 다름).
- 맵 미리보기 그림은 지금 맵 그림이다. 맵 레이아웃·비주얼 작업이 끝나면 6장 명령으로 다시 만들어야 한다.
- 음량 설정·이모트는 다른 세션 몫(자리만 마련, 5장).
- 촬영 중 PlayerPrefs(이름·서버 주소)는 `-autoconnect` 시험이 바꾼다(예전과 같음). 클릭 자동 시험은 바꾸지 않는다.

## 9. 변경 기록

- 2026-11-10: main(`dbe8d32`, 아트·문서만 바뀜)을 합침, docs/README.md 충돌만 해결. 영어 맵 키 안내 줄바꿈. 합친 뒤 dotnet build·test, Unity 빌드, 클릭 시험(한·영, 1280×720·2560×1440) 다시 통과.
- 2026-11-10 (`7b75f96`): 작업 시작. 현재 화면 촬영, UI Toolkit으로 전체 화면 다시 만듦(접속·접속 중·대기·카운트다운·HUD·관전·결과·설정·이름표·전환), 디자인 기준(`Jelly.uss`), Jua 글꼴, 맵 미리보기 그림, `UiHooks` 확장 자리, `-uitest` 클릭 자동 시험.
