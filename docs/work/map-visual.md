# 맵 비주얼 (배경 깊이층 · 움직이는 배경 · 빈 공간 채우기)

- 담당 세션: `[OnlineSeverGame] 맵 비주얼 · 01`, 브랜치 `claude/map-visual-baa34f` (main 병합은 사용자 승인 후 조율 세션)
- 범위: 네 맵(주방·겨울 처마·창고·디저트 가게)의 배경을 여러 겹으로 나누고, 배경에 움직임을 넣고, 화면 위쪽 빈 공간을 소품으로 채운다(단계 A, 지금 배치). 맵 레이아웃 세션의 새 배치·새 구조물·맵 이벤트 그림을 그 세션 설계 문서(`map-layout.md`, 커밋 b3c88ca) 좌표·크기대로 렌더한다(단계 B, 6장).
- 지킨 기준(다시 정하지 않음): progress 확정 사항 3의 화면 대비 규칙 — 배경은 어둡고 저채도, 발판 윗면 밝은 테두리, 캐릭터는 중간 밝기의 선명한 색, 위험물과 예고만 최고 대비. 그림 방식은 기존 Blender 스크립트(`Art/blender/build_sprites.py`)와 같은 평면 색·계단 빛 무리.

## 1. 어떻게 보이게 되는가

**깊이층** — 맵마다 배경이 네 장으로 나뉜다.

| 층 | 무엇 | 시차(depth) | 그리는 순서 |
|---|---|---|---|
| far (먼 배경) | 예전 배경에서 발판에 붙은 것을 뺀 벽·창·후드·소나무·빛줄기 등. 살짝 안개색을 섞어 한 단계 뒤로 물러나 보이게 | 0.5 | -100 |
| mid (중간 배경) | 새로 그린 소품: 위쪽 빈 공간을 채우는 선반·걸린 조리도구·곶감·형광등·진열장·꼬마전구 줄 등. 벽보다 어두운 실루엣 + 왼쪽 위 얇은 밝은 테두리 | 0.25 | -95 |
| attach (붙은 것) | 발판·처마에 붙어 있어야 하는 것: 주방 레일·사슬·걸린 국자, 겨울 창턱 뒤 창문과 들보 받침, 창고 레일·사슬·기둥, 디저트 몰딩 | 0 (움직이지 않음) | -90 |
| front (맨 앞) | 흐리게 번진 아주 어두운 소품 실루엣. 좌우 벽 바깥(x < 0, x > 24)과 바닥 아래 띠에만 서 있어 플레이 공간을 가리지 않음 | -0.6 (반대로) | 35 |

- 시차: 카메라는 그대로 두고, **내 캐릭터가 화면 가운데에서 끝으로 갈수록** 배경층이 살짝 따라 움직인다(먼 층 최대 ±0.4 유닛, 중간 ±0.2, 맨 앞은 반대로 ∓0.48). 카메라가 흔들리면(타격감 세션의 흔들림 등) 그 오프셋도 같은 비율로 층에 적용된다. 세로 시차는 카메라 흔들림만 따른다(바닥에 선 배경 소품이 뜨지 않게).
- 발판·캐릭터·위험물·예고는 전혀 움직이지 않는다. 판정과 무관하다.

**움직이는 배경** — 모두 발판보다 뒤에 그리고 알파를 낮게 잡았다(위험물·예고보다 눈에 띄지 않게).

| 맵 | 움직이는 것 |
|---|---|
| 주방 | 선반 위 작은 냄비에서 김이 올라와 커지며 사라짐, 후드 등불 빛이 천천히 숨 쉼 |
| 겨울 처마 | 두 겹으로 내리는 눈(먼 층 작고 느린 눈 70개, 중간 층 큰 눈 26개), 벽 팔에 걸린 등불 두 개가 저절로 흔들리며 불빛이 일렁임, 창턱 뒤 창문 불빛이 불꽃처럼 흔들림 |
| 창고 | 달빛 줄기 안에서 떠다니는 먼지, 매달린 형광등 두 개(오른쪽 것은 몇 초마다 지직거림), 벽 환풍기가 천천히 돎, 벽등 빛 숨 쉼 |
| 디저트 가게 | 몰딩 아래 꼬마전구 줄이 색을 돌려 가며 물결처럼 켜짐, 진열장 유리·사탕 병에 짧은 반짝임, 창밖 가로등 빛 숨 쉼 |

**빈 공간 채우기** (모두 mid 층, 발판 높이에서 떨어진 위쪽, 밝은 윗면 선 없음 = 발판으로 오해 안 되게)

| 맵 | 채운 소품 |
|---|---|
| 주방 | 양념 선반(병·유리병·냄비), 그 아래 접시 건조대, 벽시계, 냄비 걸이(프라이팬·거품기·국자·편수냄비), 후드 옆 벽장, 오른쪽 벽 둥근 도마·밀대 |
| 겨울 처마 | 곶감 말리는 장대(곶감 줄 4개), 화환, 등불 벽 팔 두 개, 왼쪽 벽 삿갓, 오른쪽 벽 나무 썰매 |
| 창고 | 매달린 형광등 두 개, 레일 아래 배관과 압력계, 벽 환풍기, 벽등 아래 흐린 경고 줄무늬 판 |
| 디저트 가게 | 케이크 진열장(돔 케이크 포함), 사탕 병 선반, 꼬마전구 줄 |

## 2. 어떻게 만들었는가

### 2.1 그림 (Blender)

- 새 스크립트 `Art/blender/build_depth.py`. 기존 `build_sprites.py`를 모듈로 불러와(마지막 `main()`에 `if __name__ == '__main__'` 한 줄만 추가) 맵 장면을 **같은 모델 함수**(`build_map`, `build_winter`, `build_warehouse`, `build_dessert`)로 만든 뒤:
  1. 발판에 붙은 물체(`MAPS[이름]['attach']`)를 `BGA` 컬렉션으로 옮긴다 → far / attach 분리. 안개를 빼면 far + attach = 예전 `<맵>_bg`(같은 물체를 둘로 나눠 렌더).
  2. `mid_<맵>()`이 중간 소품을 `MID` 컬렉션에, `front_<맵>()`이 실루엣을 `FRONT` 컬렉션에 더한다.
  3. 컬렉션마다 72 px/유닛, world rect (-1.5, -1.5)~(25.5, 14.0)으로 렌더. far는 안개색을 10~12% 섞고, front는 흐림(상자 흐림 3번, 반지름 5 px) 후 알파 0.92. mid/attach/front는 투명 테두리를 잘라 내고 잘린 world rect를 기록한다.
- 기존 파일(`<맵>_bg.png`, 발판 조각, `map.json`)은 **건드리지 않았다**. 새 파일만 추가한다.
- 움직이는 배경용 흰색 그림(128 px/유닛, 피벗 가운데)은 numpy로 4배 초과 표본 후 줄여 만든다: `Art/sprites/map_fx/fx_glow`(3단 계단 빛 무리, 벽 빛과 같은 방식), `fx_puff`(만화 김), `fx_flake`(점), `fx_sparkle`(네 갈래 별), `fx_bar`(켜진 형광관). 창고 환풍기 날개 `map_warehouse/vent_fan.png`만 어두운 색.
- 콘셉트 이미지는 새로 만들지 않았다(Codex 미사용). 소품은 기존 맵 콘셉트·기준 프롬프트의 소재(주방 조리도구, 겨울 처마 등불, 창고 랙·등, 디저트 진열장)를 그대로 썼고, 기준 프롬프트·아트 방향은 바꾸지 않았다.

### 2.2 출력 위치와 파일 규칙

| 맵 | 폴더 | 새 파일 |
|---|---|---|
| 주방 | `Art/sprites/map/` | `kitchen_far/mid/attach/front.png`, `layers.json` |
| 겨울 처마 | `Art/sprites/map_winter/` | `winter_far/mid/attach/front.png`, `layers.json` |
| 창고 | `Art/sprites/map_warehouse/` | `warehouse_far/mid/attach/front.png`, `vent_fan.png`, `layers.json` |
| 디저트 가게 | `Art/sprites/map_dessert/` | `dessert_far/mid/attach/front.png`, `layers.json` |
| 공용 | `Art/sprites/map_fx/` | `fx_glow/puff/flake/sparkle/bar.png`, `fx.json` |

`layers.json` 형식(클라이언트가 그대로 읽는다):

```json
{ "ppu": 72, "map": "winter",
  "layers": [ { "name": "far", "file": "winter_far", "rect": [-1.5, -1.5, 25.5, 14.0], "depth": 0.5, "order": -100 }, ... ],
  "fx": [ { "kind": "snow", "layer": "far", "area": [-1.5, -0.6, 25.5, 13.2], "count": 70, "size": 0.07,
            "speed": [0.35, 0.6], "color": "#8FA3C8", "alpha": [0.28, 0.42] }, ... ] }
```

- `rect`: 그림이 덮는 월드 사각형(그림 왼쪽 아래 = (x0, y0)). `depth`: 시차 비율. `order`: 정렬 순서.
- fx 종류: `glow`(at, size=반지름, alpha 범위, period, flicker), `steam`(at, size, count, rise, period), `snow`(area, count, size, speed), `dust`(points = 빛줄기 사각형 네 꼭짓점), `tube`(area = 켜진 관, size = 빛 무리, flicker 1 = 지직거림), `sway`(sprite, pivot px, at, scale, amp 도, period, glow, glowAt), `spin`(sprite, at, amp = 도/초), `bulbs`(points, colors), `sparkle`(points, size). fx는 자기 층과 함께 시차로 움직인다.
- 클라이언트 복사본: `Client/Assets/Resources/Art/<같은 폴더>/` (+ `map_fx/`).

### 2.3 클라이언트

- `Client/Assets/Scripts/View/BackdropView.cs` (새 파일): `TryBuild(parent, folder, layout)`가 `Resources/Art/<folder>/layers.json`을 읽어 층과 fx를 만든다. 파일이 없으면 false → StageView가 예전 한 장 배경을 그린다(안전한 되돌림).
- 시차는 `LateUpdate`: 가상 카메라 = (내 캐릭터 x − 화면 가운데)/(화면 반 폭) × `FocusRange` 0.8 (부드럽게 따라감) + 실제 카메라가 쉬는 자리(`Width/2, Height/2 − 0.5`)에서 벗어난 만큼(최대 0.6). 층 위치 = 가상 카메라 × depth.
- `StageView.cs`: 네 맵의 배경 한 줄을 `if (!BackdropView.TryBuild(...))`로 감쌌다(줄 4곳만).
- `WorldView.cs`: 내 캐릭터가 살아 있으면 `BackdropView.SetFocus(p.Position)` 한 줄.

## 3. 직접 돌려 보기

```bash
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python Art/blender/build_depth.py -- depth
```

(약 4분: fx 그림 + 네 맵. 맵 하나만: `-- kitchen` / `winter` / `warehouse` / `dessert`, fx만 `-- fx`.) 렌더 뒤 새 파일을 `Client/Assets/Resources/Art/`의 같은 폴더로 복사한다.

확인 그림: `Art/preview/<맵>_depth.png`(far + mid + attach + 발판 + front), `Art/preview/<맵>_depth_shift.png`(내 캐릭터가 오른쪽 끝에 있을 때처럼 층을 옮긴 것).

게임에서: 서버 `dotnet run --project Server/GameServer -- --port 7805 --map 2` + 봇 + `Client/Build/Windows/Client.exe -server 127.0.0.1:7805 -autoconnect`. 좌우로 끝까지 걸어 가면 배경이 살짝 따라온다.

## 4. 확인한 것과 남은 일

### 단계 A (2026-11-10)

확인한 것:
- Blender 렌더: 네 맵 × 4층 + fx 그림 6장 오류 없음. 기존 맵 파일(`<맵>_bg`, 발판 조각, `map.json`)은 다시 렌더해도 바이트 단위로 같음(git diff 없음).
- 확인 그림 `Art/preview/<맵>_depth.png`, `_depth_shift.png`를 직접 열어 봄: 중간 소품이 벽보다 어둡고, 발판 높이(1.6·2.0·2.4·3.6·4.8·6.0)와 겹치는 밝은 가로선이 없고, 맨 앞 실루엣은 벽 바깥에만 섬. attach 층(사슬·레일·창턱 창문)은 층을 옮겨도 발판에 붙어 있음.
- Unity 배치 빌드 성공(컴파일 오류 0). 서버(포트 7805, `--map 1~4`, `--min-players 6`) + 봇 5 + 클라이언트(`-autoplay`, 1600×900 창)로 맵마다 약 35초씩 돌리며 4초마다 스크린샷: 네 맵 모두 `Backdrop`이 만들어지고(로그 예외 0), 김·눈·흔들리는 등불·형광등·환풍기·꼬마전구·반짝임이 화면에 나옴. 내 캐릭터가 오른쪽 끝에 있을 때 먼 층 창문이 약 0.4유닛, 중간 진열장이 약 0.2유닛 오른쪽으로 옮겨진 것을 같은 맵 두 장에서 확인.
- **위험물 예고가 배경보다 먼저 보이는가**: 네 맵 스크린샷에서 가장 밝고 대비가 큰 것은 여전히 칼·고드름·포크(흰색)와 빨간 예고 기둥·착지 표시. 움직이는 배경은 모두 발판 뒤(정렬 -89 이하), 알파 0.05~0.5. 첫 시험에서 디저트 꼬마전구(반지름 0.24, 알파 최대 0.42)가 포크가 나오는 화면 위쪽 줄에서 눈에 띄어 반지름 0.14, 알파 최대 0.26으로 낮췄고, 주방 후드 빛(최대 0.11 → 0.07)과 김(0.2 → 0.15)도 낮춤. 다시 찍은 디저트 화면에서 전구는 은은하고 예고 기둥이 먼저 보임.

- main 병합(ef1713e) 뒤 다시 확인: Unity 빌드 성공, 같은 방법으로 네 맵 34초씩 스크린샷 28장, 예외 0. 네 맵 한 장씩 모은 그림: `Art/preview/map_visual_ingame.png`(왼쪽 위 주방, 오른쪽 위 겨울, 왼쪽 아래 창고, 오른쪽 아래 디저트).

확인 안 한 것 / 사람이 보고 판단할 것:
- 움직임의 느낌(김 속도, 눈 양, 형광등 지직거림 빈도, 시차 폭 0.8)은 정지 스크린샷으로만 봤다. 실제 플레이에서 어지럽거나 너무 약하면 `layers.json` 숫자나 `FocusRange`만 바꾸면 된다.
- 21:9처럼 16:9보다 넓은 화면: 배경 그림이 27유닛만 덮는 기존 문제(art.md)가 그대로이고, 시차로 가장자리가 0.4유닛 더 드러날 수 있다.
- 다른 브랜치(타격감·UI)의 카메라 흔들림·프레이밍과 합친 뒤의 모습. 카메라 쉬는 자리가 바뀌면 오프셋이 생기므로 0.6으로 제한해 두었다.
- 색약 시뮬레이션.

## 6. 단계 B: 새 배치 그림 (맵 레이아웃 세션 설계 b3c88ca)

### 6.1 무엇을 그렸나

기준: 레이아웃 세션 브랜치의 `docs/work/map-layout.md` 6장 그림 목록(`git show refs/heads/claude/map-layout-ea33fc:docs/work/map-layout.md`). **파일 이름·픽셀 크기·world_rect·피벗은 그 표와 글자 그대로 같다**(스크립트 REPORT로 크기 확인). 숫자가 바뀌면 그 문서가 먼저 바뀌고 이 스크립트 상수를 고쳐 다시 렌더한다.

| 맵 | 정지 조각 (72 px/유닛) | 움직이는 발판 (72 px/유닛, 피벗 = 윗면 가운데) | 이벤트·기타 그림 |
|---|---|---|---|
| 주방 | kitchen_bg(다시), kitchen_floor(도마 + 가스레인지, 화구 링 4개 + 손잡이), spice_rack(3단 탑, 칸 아래 매단 양념 깡통), ladle_rail, wall_board, tray_shelf, range_hood(윗면 7.2 평평) | — | burner_flame(4프레임 가로 1536 × 128, 피벗 아래 가운데), burner_glow(예고: 빨갛게 달아오른 링), butter(버터 블록) |
| 겨울 처마 | winter_bg(다시: 창문을 새 창턱 위로, 돌 선반 받치는 굴뚝은 attach), winter_floor(얼음 7~13), winter_ice_sill(폭 4.0, 고드름 술 8개), winter_beam(폭 4.0), stone_ledge, icy_gutter | snow_awning, snow_awning_crack(무너지기 직전: 금 + 흘러내리는 눈) | snow_dump(무너질 때 떨어지는 눈 덩어리), wind_streak(바람 줄, 흰색), snowflake |
| 창고 | warehouse_bg(다시: 팔레트 사슬 뺌), warehouse_floor(컨베이어 틀·롤러), workbench, wall_shelf, catwalk, warehouse_rack3 | crane_pallet(팔레트 + 사슬 두 줄 + 레일 위 활차 통째) | conveyor_belt(1.0 × 0.25 이어 붙이는 무늬, 윗줄 밝음) |
| 디저트 가게 | dessert_bg(다시: 리프트 기둥 홈, 메뉴판을 웨하스 위로 옮김), cherry_cake(체리는 앞면 장식, 윗면 위로 안 나옴), wafer_shelf | cake_lift_plate | cake_lift_column(0.6 × 1.0 세로 반복), syrup(9-slice 양 끝 36 px, 반투명), syrup_drip. 아이스크림 4장은 지금 그림 그대로(9-slice) |

- 모든 발판 윗면에 밝은 선(대비 규칙), 발판 외곽선 #121318 0.03, 움직이는 그림 외곽선 #14161D. 불꽃·달아오른 링만 밝은 주황·빨강(위험물·예고 = 최고 대비).
- 새 배치에 맞춘 깊이층도 같이 만들었다(`<맵>_far/mid/attach/front.png` + `layers.json`): 중간 소품을 새 발판 높이(1.2·2.4·3.6·4.8·6.0·7.2·8.4·9.6)와 겹치지 않게 다시 놓음(주방 냄비 걸이·접시 건조대를 후드 오른쪽 위로, 시계를 국자 걸이 위로, 김은 후드 뒤에서 덕트를 따라 오름 / 겨울 화환·삿갓·썰매(세로로 걸기)·등불 팔 위치 / 창고 환풍기를 5.0으로 내리고 경고판 대신 공구판 / 디저트 진열장 왼쪽으로, 사탕 병 선반은 웨하스 높이와 가까워 뺌). 움직이는 배경 위치도 맞춤.
- `<맵>_bg.png` = far + attach(안개 없이) 한 장: layers.json을 안 읽는 클라이언트용.

### 6.2 출력 위치와 파일 규칙

- 폴더: `Art/sprites/<맵 폴더>/layout2/` (map, map_winter, map_warehouse, map_dessert). **지금 main은 옛 배치라서** 옛 그림을 덮어쓰지 않으려고 따로 둔다. 레이아웃 브랜치가 병합되면 이 폴더 파일을 한 단계 위(맵 폴더)로 옮기고(이름이 같은 "다시" 파일은 교체), `Client/Assets/Resources/Art/`에도 복사한다.
- 폴더마다 `map.json`: `pieces`(정지 조각 world_rect, 창턱은 instances 2개), `moving`(움직이는 발판: size, `pivot_px_from_bottom_left` = 윗면 가운데, `pivot_normalized`, `start_world_rects` = 처음 자리), `sprites`(128 px/유닛 그림의 크기·피벗·프레임, 72 px/유닛 반복 무늬·9-slice 정보).
- 확인 그림: `Art/preview/<맵>_layout2.png` (far + mid + attach + 조각 + 움직이는 발판 처음 자리 + 이벤트 그림 몇 개 + 캐릭터 5명 + front). 주방은 화구 1·3에 불, 2에 예고 링, 창고는 벨트, 겨울은 바람 줄·눈 덩어리, 디저트는 왼쪽 시럽·리프트 기둥.
- 다시 렌더(루트, 약 4분): `"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python Art/blender/build_layout2.py -- layout2` (맵 하나: `-- kitchen` 등). 스크립트는 `build_sprites.py`의 맵 모델 함수를 배치 상수만 바꿔 다시 쓰고(겨울 창턱·들보·얼음, 옛 발판·사슬 삭제), 새 조각을 더한다.

### 6.3 클라이언트 연결: 레이아웃 세션 코드와 겹치는 파일

원칙: 레이아웃 브랜치가 먼저 main에 병합되고(조율 세션 순서), 이 세션이 병합 뒤 맞춘다. 지금 이 브랜치의 클라이언트는 옛 배치 + 단계 A 깊이층만 그린다.

| 파일 | 레이아웃 세션 | 이 세션이 병합 뒤 할 일 |
|---|---|---|
| `Client/Assets/Scripts/View/StageView.cs` | 새 배치를 그림(옛 그림 늘려 쓰기 / 단색 도형), 움직이는 발판·차양 위치 갱신 | 새 조각 그림으로 교체, `BackdropView.TryBuild`가 `layout2` 층을 읽게(이 세션이 감싼 배경 줄 4곳) |
| `Client/Assets/Scripts/View/MapEventView.cs` (레이아웃 세션 새 파일) | 불꽃·바람·벨트·시럽을 단색 도형으로 | burner_flame/glow, wind_streak, conveyor_belt, syrup/syrup_drip 그림으로 교체 |
| `Client/Assets/Scripts/View/ArtSprites.cs` | (새 그림 불러오기를 더할 수 있음) | 72 px/유닛 움직이는 발판(윗면 가운데 피벗)·반복 무늬 불러오기 추가 |
| `Client/Assets/Scripts/View/WorldView.cs` | (바뀔 수 있음) | 이 세션은 `BackdropView.SetFocus` 한 줄만 |
| `Client/Assets/Resources/Art/<맵 폴더>/` | — | layout2 그림 복사, 옛 배치 전용 파일(shelf, pot_lid, winter_bench 등)은 쓰는 코드가 없을 때 정리 |
| `docs/README.md` | 자기 행 | 자기 행 |

### 6.4 확인한 것과 남은 일 (단계 B)

확인한 것: 네 맵 그림 36장 + 깊이층 + map.json 렌더 오류 없음, 크기가 map-layout.md 6장 표와 모두 같음(REPORT), 잘림 검사에서 가장자리에 닿는 것은 바닥까지 내려가는 탑 기둥·작업대 다리·랙 기둥·후드 아래 끝(문서 rect 아래 끝)뿐(의도). 확인 그림 4장을 직접 열어 봄: 발판 높이마다 밝은 윗면 선, 배경 소품이 새 발판 높이와 겹치지 않음, 캐릭터 발이 윗면에 닿음, 불꽃·예고 링이 가장 눈에 띔. 첫 렌더 뒤 고친 것: 짧아진 창턱 밖으로 나온 고드름 술, 새 웅덩이 밖에 남은 얼음 반짝임, 삿갓 둘레 원, 보이지 않던 체리, 너무 작은 허브 다발.

확인 안 한 것: 게임 화면(레이아웃 코드 병합 전이라 연결 안 함), 움직이는 발판이 실제로 움직일 때의 모습, 불꽃 4프레임 반복 속도, 차양이 무너지는 연출, 벨트 흐름. 병합 뒤 6.3대로 연결하고 서버 + 봇 + 클라이언트로 맵별 확인이 남는다.

## 5. 변경 기록

| 날짜 | 커밋 | 내용 |
|---|---|---|
| 2026-11-10 | 148649c | 단계 A: 네 맵 깊이층(far/mid/attach/front)·움직이는 배경·빈 공간 소품, `build_depth.py`, `BackdropView` |
| 2026-11-10 | 57bc10b | 단계 B: 레이아웃 설계(b3c88ca) 그림 36장 + 새 배치 깊이층(`build_layout2.py`, `layout2/` 폴더), 겨울 삿갓 둘레 원 제거(단계 A 그림도) |
| 2026-11-10 | ef1713e | main 병합(캐릭터 추가 그림, 충돌은 docs/README.md 표 한 줄), Unity 빌드·네 맵 다시 확인 |
| 2026-11-10 | (이 커밋) | 병합 뒤 확인 기록, 게임 화면 모음 `Art/preview/map_visual_ingame.png` |
