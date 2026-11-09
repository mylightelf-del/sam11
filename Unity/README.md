# Unity 프로젝트 (M0 뼈대)

게임 코어(`src/Sam11.Core`)를 Unity 화면에 연결한 첫 뼈대. 콘솔판(`tools/Sam11.Play`)과 같은 `PlayerSession` 명령 통로를 쓰므로 저장 파일도 서로 맞는다.

## 처음 열기

1. Unity **2022.3 LTS** 이상 (내장 렌더 파이프라인).
2. 저장소 루트에서 코어 DLL과 데이터를 넣는다 (코어·데이터를 고칠 때마다 다시):
   ```bash
   tools/unity/sync.sh
   ```
   `Assets/Plugins/Sam11/Sam11.Core.dll`, `Assets/StreamingAssets/data`, `Assets/StreamingAssets/Mods`가 생긴다 (git에는 넣지 않는다).
3. Unity Hub에서 `Unity/` 폴더를 연다. 패키지 `com.unity.nuget.newtonsoft-json`이 자동으로 깔린다.
4. 빈 장면에서 **Play**. 장면 파일 없이 `Boot`가 `GameRoot`를 만든다.

## 조작

| 입력 | 하는 일 |
|---|---|
| WASD·방향키·화면 가장자리·가운데 버튼 끌기 | 카메라 이동 |
| 휠 / Q·E | 확대 / 회전 |
| 왼쪽 클릭 | 부대·거점 선택 (전법·계략 대상 고르기) |
| 오른쪽 클릭 (우리 부대 선택 중) | 빈 칸 이동, 적 부대 공격, 적 성벽·성문 공격 |
| Enter | 차례 끝 |
| Esc | 대상 고르기 취소 |

거점 창: 무장을 골라 징병·훈련·순찰·시혜·생산·건설, "출진"에 무장(최대 3)을 체크하고 진형·병력·군량을 정해 출진.
부대 창: 전법·계략·일기토·귀환. 이벤트와 외교 제안은 가운데 창에서 고른다.

## 구성 (`Assets/Scripts`)

| 파일 | 내용 |
|---|---|
| `Boot`, `GameRoot` | 시작, 데이터·모드 로드, 세션, 명령 실행(`Execute`), 저장·불러오기 (`persistentDataPath/saves`) |
| `MapView` | 전국 헥스 맵 덩어리 메시 (지형 높이·옆면·정점색), 마우스 판정용 콜라이더, 강조 칸 |
| `CityLayer`, `UnitLayer` | 거점(성벽·성문·정청·이름표), 부대(깃발·병력 막대·이름표, 시야 밖 적은 숨김) |
| `CameraRig`, `Selection` | 전략 카메라, 마우스 선택·명령 |
| `Hud`, `TitleScreen` | 임시 IMGUI 화면 (정식 UI는 UI Toolkit으로 교체 예정) |
| `HexMetrics`, `InkPalette`, `Fonts` | 헥스↔월드 좌표, 수묵 색, 한글 글꼴(운영체제 글꼴) |
| `Assets/Resources/Shaders/InkWash.shader` | 수묵화풍 셰이더: 채도 낮춤, 명암 단계, 먹 윤곽, 종이 결, 종이색 안개 |

## 검사

Unity 없이 스크립트가 Unity API에 맞게 컴파일되는지 본다 (NuGet `UnityEngine.Modules` 참조 어셈블리):

```bash
dotnet build tools/UnityCheck
```

셰이더와 실제 화면은 Unity 에디터에서 확인해야 한다.

## M0에서 이어 할 것

- 병사 군집으로 진형 보여 주기 (지금은 깃발 하나), 성능 측정
- 수묵 후처리 (먹 번짐·종이 질감 전체 화면), 산·물 표현
- 정식 UI (UI Toolkit, 수묵 디자인), 한글 글꼴 에셋
- 전투 연출 (공격·전법 애니메이션, 화공 불길)
