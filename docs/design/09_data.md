# 09 데이터 형식 (v0.1)

게임 데이터는 모두 `data/` 폴더의 JSON 파일이다. 게임 코어(08 UI·기술 구조 설계서 3장)와 신무장 편집기, 모드가 같은 형식을 쓴다.

## 1. 폴더 구조

```
data/
├─ base/                     게임 공통 데이터 (모든 시나리오가 공유)
│  ├─ officers.json          무장 (정적 정보: 능력치·특기·적성·혈연)
│  ├─ strongholds.json       거점과 연결 관계
│  ├─ formations.json        진형 (고유 보정·상성·지형 보정·전법 목록)
│  ├─ tactics.json           전법
│  ├─ terrain.json           지형
│  ├─ traits.json            특기
│  ├─ treasures.json         보물
│  ├─ facilities.json        시설
│  ├─ techs.json             기교
│  ├─ ranks.json             관직·군주 작위
│  ├─ weather.json           날씨와 계절별 확률
│  └─ rules.json             밸런스 상수 (상성 배율, 연쇄 보너스, 사기 구간 등)
├─ scenarios/
│  └─ 190/
│     ├─ scenario.json       시작 상태: 세력, 거점 상태, 무장 배치, 관계, 외교, 보물
│     └─ events.json         시나리오 이벤트
└─ schema/                   JSON Schema (형식 검증용)
```

- **정적 데이터와 시나리오 상태를 나눈다.** 무장의 능력치는 `base/officers.json`에 한 번만 쓰고, 어느 세력·어느 거점에 있는지는 시나리오마다 `scenario.json`에 쓴다.
- 밸런스 수치는 코드에 박지 않고 `rules.json`에 둔다. 모드로 바꿀 수 있다.

## 2. 공통 규칙

| 항목 | 규칙 |
|---|---|
| ID | 영문 소문자·숫자·밑줄 (`cao_cao`, `fish_scale`). 한 파일 안에서 유일 |
| 표시 이름 | `{"ko": "조조", "zh": "曹操"}` 형식. `ko` 필수, 나머지는 선택 (다국어 확장) |
| 진형 ID | `fish_scale` 어린, `crane_wing` 학익, `square` 방원, `long_snake` 장사, `wedge` 추행, `wild_geese` 안행, `siege` 공성진, `naval` 수진 |
| 능력치 키 | `ldr` 통솔, `war` 무력, `int` 지력, `pol` 정치, `cha` 매력 |
| 날짜 | `"190-03"` (연-월) |

## 3. 주요 형식

### 3.1 무장 (`base/officers.json`)

```json
{
  "id": "cao_cao",
  "name": { "ko": "조조", "zh": "曹操" },
  "gender": "male", "birth": 155, "death": 220,
  "stats": { "ldr": 97, "war": 74, "int": 92, "pol": 92, "cha": 95 },
  "hidden": { "loyalty": 6, "ambition": 15, "compat": 25 },
  "aptitude": { "fish_scale": "A", "crane_wing": "A", "wedge": "S", "...": "..." },
  "traits": ["talent_eye", "strategist"],
  "kin": { "father": "...", "siblings": ["..."] },
  "ruler_personality": { "aggression": 8, "faith": 5, "domestic": 8, "diplomacy": 6 }
}
```

- `ruler_personality`는 군주가 될 수 있는 무장에만 쓴다 (06 AI 설계서 6.2).
- 능력치는 이 게임 고유의 값이다. 다른 게임의 수치를 옮기지 않는다.

### 3.2 거점 (`base/strongholds.json`)
- `type`: `major_city` / `city` / `gate` / `port` / `ferry` / `fortress`
- `links`: 길로 직접 이어진 거점. 양방향으로 적는다. AI 경로 계획과 헥스 맵이 생기기 전의 테스트에 쓴다.
- `hex`: 헥스 맵 좌표. 맵 제작(M0~M2) 전까지는 `null`.

### 3.3 효과 표현 (특기·보물·날씨·전법 공통)

```json
{ "type": "modifier", "stat": "attack", "op": "mul", "value": 0.15,
  "when": { "formation": ["wedge"], "terrain": ["plain"] } }
```

| type | 뜻 |
|---|---|
| `modifier` | 수치 보정. `op`가 `mul`이면 +15%, `add`면 +값 |
| `aura` | 주변 부대에 효과 (예: 위압) |
| `immunity` | 상태 무효 (혼란, 연쇄 등) |
| `duel_card` | 설전·일기토 특수 카드 추가 |
| `ignore_terrain_penalty` | 지형 페널티 무시 |
| `special` | 코드에서 따로 처리하는 고유 효과 |

`when` 조건 키: `formation`, `terrain`, `weather`, `role`(governor/ruler), `stratagem`, `troop_ratio_lte`, `turns_since_sortie_lte`

### 3.4 시나리오 (`scenarios/<id>/scenario.json`)
- `factions`: 세력 ID, 군주, 색, 작위(0~4), 본거지, 목표 승리 조건
- `cities`: **모든 거점**의 시작 상태 (소유 세력, 인구, 민심, 금, 군량, 병력, 훈련도, 병장기, 태수)
- `officers`: 무장 배치. `status`는 `active`(세력 소속) / `free`(재야) / `unborn`(아직 미성년, 15세에 등장) / `captive`(포로)
- `relations`: 의형제(`sworn`), 부부, 친애, 혐오
- `diplomacy`: 시작 시 외교 관계 (예: 반동탁 연합 12개월)
- `treasures`: 보물 위치. `owner`(무장) 또는 `location`(거점, `hidden`이면 탐색·이벤트로만 발견)

### 3.5 이벤트 (`scenarios/<id>/events.json`)

```json
{
  "id": "imperial_seal", "once": true, "locked": true,
  "trigger": { "all": [ { "owns": {...} }, { "officer_in": {...} } ] },
  "text": "...",
  "choices": [
    { "id": "keep", "text": "몰래 간직한다", "ai_weight": 7, "effects": [ ... ] }
  ]
}
```

- `trigger`는 `all` / `any` / `not`으로 조건을 묶는다.
- `locked: true`면 다른 이벤트의 `unlock_event`로 풀려야 발생한다.
- `ai_weight`: AI 세력이 이벤트를 만났을 때 선택지를 고르는 가중치.

## 4. 검증

```
python3 tools/validate_data.py
```

- **형식 검사**: `data/schema/`의 JSON Schema로 필수 항목, 값 범위, 허용 값을 검사한다.
- **참조 검사**:
  - 없는 특기·전법·보물·무장·거점을 가리키는지
  - 거점 연결이 양방향인지, 맵 전체가 하나로 이어지는지
  - 군주가 자기 세력 소속인지, 본거지를 소유하는지, 태수가 그 거점에 있는지
  - 무장이 소속 세력의 거점에 있는지
  - 15세 미만 무장이 등장 상태인지, 시작 연도 전에 죽은 무장이 있는지
  - 이벤트가 없는 이벤트를 해금하는지
- 데이터를 고칠 때마다 실행한다. 나중에 CI에 연결한다.

## 5. 190년 시나리오 초안 현황

| 항목 | 수 |
|---|---|
| 세력 | 14 (동탁, 원소, 한복, 조조, 손견, 원술, 공손찬, 유비, 유표, 유언, 마등, 도겸, 공융, 공손도) |
| 무장 | 100 (세력 소속 86, 재야 10, 미성년 4) |
| 거점 | 79 (도시 50, 관문 12, 항구 8, 나루 4, 요새 5) |
| 이벤트 | 3 (장안 천도, 옥새 발견, 연환계) |

## 6. 미결 사항

- [ ] 거점 목표 100개까지 확장 (관문·항구·요새 추가)
- [ ] 무장 수 확장 (190년 기준 약 300명 목표)
- [ ] 헥스 좌표 (`hex`) 입력 — 맵 제작 단계에서
- [ ] 시설 슬롯 위치 데이터 — 맵 제작 단계에서
- [ ] 여성 무장, 얼굴 이미지 지정 방식
