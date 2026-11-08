#!/usr/bin/env python3
"""게임 데이터 검증: JSON Schema 형식 검사 + 파일 간 참조 검사.

사용법: python3 tools/validate_data.py
"""
import json
import sys
from pathlib import Path

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parent.parent / "data"
errors = []


def load(rel):
    with open(ROOT / rel, encoding="utf-8") as f:
        return json.load(f)


def check_schema(data_rel, schema_rel):
    data, schema = load(data_rel), load(schema_rel)
    for e in Draft202012Validator(schema).iter_errors(data):
        loc = "/".join(str(p) for p in e.absolute_path)
        errors.append(f"[형식] {data_rel} {loc}: {e.message}")
    return data


def err(msg):
    errors.append(f"[참조] {msg}")


def unique_ids(items, label):
    ids = [i["id"] for i in items]
    for d in {i for i in ids if ids.count(i) > 1}:
        err(f"{label} ID 중복: {d}")
    return set(ids)


# --- 기본 데이터 ---
officers = check_schema("base/officers.json", "schema/officers.schema.json")["officers"]
strongholds = check_schema("base/strongholds.json", "schema/strongholds.schema.json")["strongholds"]
formations = check_schema("base/formations.json", "schema/formations.schema.json")["formations"]
tactics = check_schema("base/tactics.json", "schema/tactics.schema.json")["tactics"]
traits = check_schema("base/traits.json", "schema/traits.schema.json")["traits"]
treasures = check_schema("base/treasures.json", "schema/treasures.schema.json")["treasures"]
terrain_ids = {t["id"] for t in load("base/terrain.json")["terrain"]}

officer_ids = unique_ids(officers, "무장")
sh_ids = unique_ids(strongholds, "거점")
form_ids = unique_ids(formations, "진형")
tactic_ids = unique_ids(tactics, "전법")
trait_ids = unique_ids(traits, "특기")
treasure_ids = unique_ids(treasures, "보물")

for o in officers:
    for t in o["traits"]:
        if t not in trait_ids:
            err(f"무장 {o['id']}: 없는 특기 {t}")
    if o.get("death") is not None and o["death"] < o["birth"]:
        err(f"무장 {o['id']}: 사망년이 출생년보다 이름")
    if all(v == "C" for v in o["aptitude"].values()) and o["stats"]["ldr"] >= 50:
        err(f"무장 {o['id']}: 통솔 50 이상인데 펼칠 수 있는 진형이 없음")
    for rel, ref in o.get("kin", {}).items():
        for r in ref if isinstance(ref, list) else [ref]:
            if r not in officer_ids:
                err(f"무장 {o['id']}: 혈연({rel}) 대상 없음 {r}")

for s in strongholds:
    for l in s["links"]:
        if l not in sh_ids:
            err(f"거점 {s['id']}: 없는 연결 {l}")
        elif s["id"] not in next(x for x in strongholds if x["id"] == l)["links"]:
            err(f"거점 {s['id']} → {l}: 연결이 한쪽 방향뿐")
    if not s["links"]:
        err(f"거점 {s['id']}: 연결된 거점이 없음")

# 거점 연결 그래프가 하나로 이어지는지
seen, stack = set(), [strongholds[0]["id"]]
links = {s["id"]: s["links"] for s in strongholds}
while stack:
    n = stack.pop()
    if n not in seen:
        seen.add(n)
        stack.extend(l for l in links[n] if l in links)
for s in sh_ids - seen:
    err(f"거점 {s}: 나머지 맵과 연결되지 않음")

for f in formations:
    for t in f["tactics"]:
        if t not in tactic_ids:
            err(f"진형 {f['id']}: 없는 전법 {t}")
    for t in f["strong_against"]:
        if t not in form_ids:
            err(f"진형 {f['id']}: 상성 대상 없음 {t}")
    for t in f["terrain_modifiers"]:
        if t not in terrain_ids:
            err(f"진형 {f['id']}: 없는 지형 {t}")
for t in tactics:
    if t["formation"] not in form_ids:
        err(f"전법 {t['id']}: 없는 진형 {t['formation']}")
for t in treasures:
    for tr in t["grants_traits"]:
        if tr not in trait_ids:
            err(f"보물 {t['id']}: 없는 특기 {tr}")

# --- 시나리오 ---
for sc_dir in sorted((ROOT / "scenarios").iterdir()):
    rel = f"scenarios/{sc_dir.name}"
    sc = check_schema(f"{rel}/scenario.json", "schema/scenario.schema.json")
    fac_ids = {f["id"] for f in sc["factions"]}
    placed = {p["officer"]: p for p in sc["officers"]}
    start_year = sc["start"]["year"]
    for f in sc["factions"]:
        if f["ruler"] not in placed or placed[f["ruler"]]["faction"] != f["id"]:
            err(f"{rel} 세력 {f['id']}: 군주 {f['ruler']}가 세력 소속이 아님")
        cap = next((c for c in sc["cities"] if c["stronghold"] == f["capital"]), None)
        if not cap or cap["owner"] != f["id"]:
            err(f"{rel} 세력 {f['id']}: 본거지 {f['capital']}를 소유하지 않음")
    city_ids = [c["stronghold"] for c in sc["cities"]]
    if set(city_ids) != sh_ids:
        err(f"{rel}: 도시 상태 목록이 거점 목록과 다름 (누락 {sorted(sh_ids - set(city_ids))})")
    for c in sc["cities"]:
        if c["owner"] is not None and c["owner"] not in fac_ids:
            err(f"{rel} 거점 {c['stronghold']}: 없는 세력 {c['owner']}")
        g = c.get("governor")
        if g and (g not in placed or placed[g]["faction"] != c["owner"] or placed[g]["location"] != c["stronghold"]):
            err(f"{rel} 거점 {c['stronghold']}: 태수 {g}가 그 거점의 소속 무장이 아님")
    owner = {c["stronghold"]: c["owner"] for c in sc["cities"]}
    by_id = {o["id"]: o for o in officers}
    for p in sc["officers"]:
        oid = p["officer"]
        if oid not in officer_ids:
            err(f"{rel}: 없는 무장 {oid}")
            continue
        if p["location"] not in sh_ids:
            err(f"{rel} 무장 {oid}: 없는 위치 {p['location']}")
        if p["status"] == "active":
            if p["faction"] not in fac_ids:
                err(f"{rel} 무장 {oid}: 없는 세력 {p['faction']}")
            elif owner.get(p["location"]) != p["faction"]:
                err(f"{rel} 무장 {oid}: 위치 {p['location']}가 소속 세력 거점이 아님")
        o = by_id[oid]
        age = start_year - o["birth"]
        if p["status"] in ("active", "free") and age < 15:
            err(f"{rel} 무장 {oid}: {age}세라 아직 등장할 수 없음 (unborn으로 둘 것)")
        if o.get("death") is not None and o["death"] < start_year:
            err(f"{rel} 무장 {oid}: 시작 연도 전에 사망한 무장")
    for r in sc.get("relations", []):
        for m in r["members"]:
            if m not in officer_ids:
                err(f"{rel} 관계: 없는 무장 {m}")
    for t in sc.get("treasures", []):
        if t["treasure"] not in treasure_ids:
            err(f"{rel}: 없는 보물 {t['treasure']}")
        if "owner" in t and t["owner"] not in placed:
            err(f"{rel} 보물 {t['treasure']}: 소유자 {t['owner']}가 시나리오에 없음")
    if "events" in sc:
        ev = check_schema(f"{rel}/{sc['events']}", "schema/events.schema.json")["events"]
        ev_ids = {e["id"] for e in ev}
        for e in ev:
            for ch in e["choices"]:
                for fx in ch["effects"]:
                    if fx["type"] == "unlock_event" and fx["event"] not in ev_ids:
                        err(f"{rel} 이벤트 {e['id']}: 없는 이벤트 해금 {fx['event']}")

if errors:
    print("\n".join(errors))
    print(f"\n오류 {len(errors)}건")
    sys.exit(1)
print(f"데이터 검증 통과: 무장 {len(officers)}, 거점 {len(strongholds)}, 진형 {len(formations)}, "
      f"전법 {len(tactics)}, 특기 {len(traits)}, 보물 {len(treasures)}")
