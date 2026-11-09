# sam11 — 삼국지 신작 프로젝트

삼국지 5~11의 호평 시스템을 조합한 군주제 전략 시뮬레이션. Unity, 풀 3D, 수묵화풍.

## 폴더

| 폴더 | 내용 |
|---|---|
| `docs/design/` | 설계서 (00 개요 ~ 09 데이터 형식) |
| `data/` | 게임 데이터 JSON (무장·거점·진형·전법·특기·보물·시설·기교·규칙, 시나리오) |
| `src/Sam11.Core/` | 게임 코어 (순수 C#, .NET Standard 2.1 — Unity에서 그대로 사용): 전투, 세계·내정, 게임 루프, AI |
| `tests/Sam11.Core.Tests/` | 코어 단위 테스트 |
| `tools/validate_data.py` | 데이터 형식·참조 검증 |
| `tools/Sam11.Play/` | 콘솔 클라이언트 — 한 판을 처음부터 끝까지 직접 플레이 (M4 수직 단면) |
| `tools/Sam11.Sim/` | 시연 콘솔 (호로관 전투, 경제 점검, 사수관 공략, AI 자동 대전, `SIM_SCENARIO`로 시나리오 선택) |
| `tools/mapgen/` | 전국 맵 생성기와 미리보기 렌더러 |
| `data/map/` | 지리 원본(`geography.json`)과 생성된 맵(`world.json`) |

## 실행

데이터 검증 (Python 3 + `jsonschema`):

```bash
python3 tools/validate_data.py
```

코어 테스트 (.NET 8 SDK):

```bash
dotnet test tests/Sam11.Core.Tests
```

직접 플레이 (시나리오, 세력, 시드). 들어가서 `도움말`을 입력하면 명령 목록이 나온다:

```bash
dotnet run --project tools/Sam11.Play -- 190 cao_cao 1
```

저장한 판 이어 하기:

```bash
dotnet run --project tools/Sam11.Play -- load save1.json
```

명령을 파일에 적어 대본처럼 돌릴 수도 있다 (예: `자동 360`은 10년을 AI에게 맡김):

```bash
dotnet run --project tools/Sam11.Play -- 190 cao_cao 1 < script.txt
```

전투 시연 (시드, 날씨: clear / rain / snow / fog / gale):

```bash
dotnet run --project tools/Sam11.Sim -- 190 clear
```

경제 점검 (명령 없이 N개월 진행):

```bash
dotnet run --project tools/Sam11.Sim -- economy 12
```

AI 대 AI 자동 대전 (년, 시작 시드, 판수, 난이도: Novice / Normal / Hard / Hell). 1판이면 연대기, 여러 판이면 통계:

```bash
dotnet run --project tools/Sam11.Sim -- auto 10 1 3 Normal
```

전국 맵 공성 시연 (조조군의 사수관 공략):

```bash
dotnet run --project tools/Sam11.Sim -- siege
```

맵 다시 생성 (`data/map/geography.json`을 고친 뒤):

```bash
python3 tools/mapgen/generate_world.py
```

```bash
python3 tools/mapgen/render_world.py 190
```

![190년 세력도](docs/images/world_190.png)
