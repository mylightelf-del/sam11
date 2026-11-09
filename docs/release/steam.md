# Steam 연동

## 1. Steamworks.NET 넣기
1. Unity 패키지 관리자에서 Git URL로 추가: `https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net`
2. Player Settings > Scripting Define Symbols에 `STEAMWORKS_NET` 추가.
3. 개발 중에는 `Unity/steam_appid.txt`(지금 480 = Valve 시험 앱)를 실행 파일 옆에 둔다. 앱 ID를 받으면 바꾼다. 출시 빌드에는 넣지 않는다.
4. 이 저장소에서 컴파일 검사: `dotnet build tools/UnityCheck -p:Steam=true`

`Platform.Current`가 Steam을 시작하지 못하면(Steam이 꺼져 있음 등) 로컬 기록(PlayerPrefs)으로 넘어간다.

## 2. 업적
`data/base/achievements.json`의 `id`를 Steamworks 관리 화면의 업적 **API 이름**과 똑같이 만든다.

| API 이름 | 이름 | 조건 |
|---|---|---|
| first_conquest | 첫 함락 | 도시 2 |
| regional_power | 한 지방의 패자 | 도시 10 |
| half_the_realm | 천하의 절반 | 도시 25 |
| gathering_of_talent | 인재가 모이다 | 무장 50 |
| treasure_hunter | 보물 수집가 | 보물 5 |
| crowned | 왕위에 오르다 | 작위 4 이상 |
| unifier | 천하 통일 | 통일 |
| goal_achieved | 뜻을 이루다 | 시나리오 목표 |
| unifier_hard | 난세의 영웅 | 어려움 이상 통일 |
| yellow_turban | 창천이 죽었다 | 184 장각 멸망 |
| dong_zhuo_falls | 폭군의 최후 | 190 동탁 멸망 |
| red_cliffs | 적벽의 불길 | 207 적벽 화공 |
| survivor | 살아남은 자 | 도시 하나로 시작해 10년 |

판정은 코어(`AchievementTracker`)가 매월·게임 끝에 하고, `GameRoot`가 `Platform.Current.Unlock`을 부른다.
모드를 켠 판에서 업적을 막을지는 정해야 한다 (지금은 막지 않음).

## 3. 클라우드 저장 (자동 클라우드, 코드 없음)
Steamworks > 앱 관리 > Steam Cloud:
- 바이트 한도 100MB, 파일 수 200
- 루트 경로: Windows `WinAppDataLocalLow`, 하위 경로 `Sam11/삼국지/saves`, 패턴 `*.json`
- macOS: `MacAppSupport`, 하위 경로 `Sam11/삼국지/saves`
(회사·제품 이름은 `BuildScript`의 PlayerSettings와 같아야 한다.)

## 4. 빌드 올리기 (SteamPipe)
1. `tools/unity/sync.sh`
2. Unity: `Sam11 > 빌드 > Windows`, `macOS` (또는 명령줄 `-executeMethod Sam11.Editor.BuildScript.BuildWindows`)
3. Steamworks SDK의 `ContentBuilder`에서 depot 스크립트로 `Unity/Builds/Windows`, `Unity/Builds/macOS`를 각 depot에 올린다.
4. 베타 브랜치에서 시험한 뒤 기본 브랜치로.
