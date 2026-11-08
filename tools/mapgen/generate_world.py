#!/usr/bin/env python3
"""전국 헥스 맵 생성기.

입력: data/map/geography.json (경위도 기반 지리), data/base/strongholds.json (거점·연결)
출력: data/map/world.json (지형, 거점 배치·구조, 도시 영역, 시설 슬롯)
      data/base/strongholds.json 의 hex 필드 갱신

같은 입력이면 항상 같은 맵이 나온다 (결정론적 노이즈).

사용법: python3 tools/mapgen/generate_world.py
"""
import heapq
import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GEO = json.loads((ROOT / "data/map/geography.json").read_text(encoding="utf-8"))
SH_PATH = ROOT / "data/base/strongholds.json"
SH_DATA = json.loads(SH_PATH.read_text(encoding="utf-8"))
STRONGHOLDS = {s["id"]: s for s in SH_DATA["strongholds"]}

G = GEO["grid"]
W, H = G["width"], G["height"]
DLON = (G["lon_max"] - G["lon_min"]) / W
COS = math.cos(math.radians(32))
DLAT = DLON * COS * math.sqrt(3) / 2  # 헥스 세로 간격 = 가로 간격 × √3/2 (실거리 기준)
SEED = G["seed"]

# 지형 기호 (world.json의 terrain 문자열)
CODE = {"plain": ".", "road": "=", "forest": "f", "mountain": "m", "marsh": "w", "river": "~",
        "sea": "s", "ford": "d", "street": "c", "city_wall": "#", "gate": "g"}
WATER = {"river", "sea"}
STRUCT = {"street", "city_wall", "gate"}

# ------------------------------------------------------------------ 좌표


def axial(col, row):
    return (col - (row >> 1), row)


def offset(q, r):
    return (q + (r >> 1), r)


def inside(col, row):
    return 0 <= col < W and 0 <= row < H


DIRS = [(1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1)]


def neighbors(q, r):
    for dq, dr in DIRS:
        c, rw = offset(q + dq, r + dr)
        if inside(c, rw):
            yield (q + dq, r + dr)


def hex_dist(a, b):
    dq, dr = a[0] - b[0], a[1] - b[1]
    return (abs(dq) + abs(dr) + abs(dq + dr)) // 2


def ring(center, radius):
    if radius == 0:
        return [center]
    q, r = center[0] + DIRS[4][0] * radius, center[1] + DIRS[4][1] * radius
    out = []
    for i in range(6):
        for _ in range(radius):
            out.append((q, r))
            q, r = q + DIRS[i][0], r + DIRS[i][1]
    return out


def lonlat(q, r):
    col, row = offset(q, r)
    return (G["lon_min"] + (col + 0.5 * (row & 1) + 0.5) * DLON, G["lat_max"] - (row + 0.5) * DLAT)


def to_hex(lon, lat):
    row = round((G["lat_max"] - lat) / DLAT - 0.5)
    col = round((lon - G["lon_min"]) / DLON - 0.5 - 0.5 * (row & 1))
    best = None
    q0, r0 = axial(col, row)
    for cand in [(q0, r0)] + list(neighbors(q0, r0)):
        lo, la = lonlat(*cand)
        d = ((lo - lon) * COS) ** 2 + (la - lat) ** 2
        if best is None or d < best[0]:
            best = (d, cand)
    return best[1]


def geo_dist(lon1, lat1, lon2, lat2):
    return math.hypot((lon1 - lon2) * COS, lat1 - lat2)


def seg_dist(p, a, b):
    """점 p와 선분 ab 사이 거리 (위도 도 단위)."""
    px, py = p[0] * COS, p[1]
    ax, ay = a[0] * COS, a[1]
    bx, by = b[0] * COS, b[1]
    dx, dy = bx - ax, by - ay
    t = 0 if dx == dy == 0 else max(0, min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return math.hypot(px - ax - t * dx, py - ay - t * dy)


def line_dist(p, line):
    return min(seg_dist(p, line[i], line[i + 1]) for i in range(len(line) - 1))


def point_in_poly(x, y, poly):
    inside_ = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            inside_ = not inside_
        j = i
    return inside_


# ------------------------------------------------------------------ 노이즈 (결정론적)


def _hash(ix, iy, salt):
    h = (ix * 374761393 + iy * 668265263 + salt * 2147483647 + SEED * 144665) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0


def noise(x, y, scale, salt=0):
    x, y = x / scale, y / scale
    ix, iy = math.floor(x), math.floor(y)
    fx, fy = x - ix, y - iy
    sx, sy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a, b = _hash(ix, iy, salt), _hash(ix + 1, iy, salt)
    c, d = _hash(ix, iy + 1, salt), _hash(ix + 1, iy + 1, salt)
    return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sy


def fbm(x, y, salt=0):
    return 0.65 * noise(x, y, 6, salt) + 0.35 * noise(x, y, 2.5, salt + 7)


# ------------------------------------------------------------------ 1. 지형

terrain = {}
ALL = [axial(c, r) for r in range(H) for c in range(W)]
sea_poly = GEO["coastline"] + [[105.8, 15.0], [127.0, 15.0], [127.0, 44.0], [124.8, 44.0]]

for h in ALL:
    lon, lat = lonlat(*h)
    col, row = offset(*h)
    t = "plain"
    if point_in_poly(lon, lat, sea_poly):
        t = "sea"
    elif (lat > 41.0 and lon < 120.5) or (lon < 101.0 and lat > 28.0):
        t = "mountain"  # 북방 초원·서쪽 고원은 진입하기 어려운 경계
    else:
        n = fbm(col, row, 1)
        near_mtn = 99.0
        for m in GEO["mountains"]:
            d = line_dist((lon, lat), m["line"]) / m["width"]
            near_mtn = min(near_mtn, d)
            if d < 0.75 + 0.45 * n:
                t = "mountain"
        if t == "plain":
            f = GEO["forest"]
            fn = fbm(col, row, 3)
            if lat < f["south_of_lat"] and fn > 1 - f["density"] - 0.2:
                t = "forest"
            elif near_mtn < f["near_mountain_factor"] and fn > 0.45:
                t = "forest"
            elif fn > 0.82:
                t = "forest"
        for m in GEO["marshes"]:
            if geo_dist(lon, lat, *m["center"]) < m["radius"] * (0.8 + 0.4 * n) and t != "mountain":
                t = "marsh"
    terrain[h] = t

for lake in GEO["lakes"]:
    for h in ALL:
        if geo_dist(*lonlat(*h), *lake["center"]) < lake["radius"] and terrain[h] != "sea":
            terrain[h] = "river"


def rasterize(line):
    out = []
    for i in range(len(line) - 1):
        (x1, y1), (x2, y2) = line[i], line[i + 1]
        steps = max(1, int(geo_dist(x1, y1, x2, y2) / 0.02))
        for s in range(steps + 1):
            t = s / steps
            h = to_hex(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t)
            if not out or out[-1] != h:
                out.append(h)
    return out


river_cells = set()
for name, line in GEO["rivers"].items():
    for h in rasterize(line):
        if h in terrain and terrain[h] != "sea":
            terrain[h] = "river"
            river_cells.add(h)

# ------------------------------------------------------------------ 2. 거점 배치

RADIUS = {"major_city": 2, "city": 1, "fortress": 1, "gate": 2, "port": 0, "ferry": 0}
ORDER = ["major_city", "city", "fortress", "gate", "port", "ferry"]
placed = {}      # id -> center
occupied = {}    # hex -> stronghold id
warnings = []


def adjacent_water(h):
    return any(terrain.get(n) in WATER for n in neighbors(*h))


def footprint(center, kind):
    return [x for rad in range(RADIUS[kind] + 1) for x in ring(center, rad)] if kind != "gate" else [center]


for kind in ORDER:
    for sid, s in STRONGHOLDS.items():
        if s["type"] != kind:
            continue
        lon, lat = GEO["strongholds"][sid]
        target = to_hex(lon, lat)
        best = None
        for rad in range(0, 9):
            for cand in ring(target, rad):
                if cand not in terrain:
                    continue
                fp = footprint(cand, kind)
                if any(x not in terrain or terrain[x] == "sea" for x in fp):
                    continue
                if terrain[cand] in WATER:
                    continue
                if kind in ("port", "ferry") and not adjacent_water(cand):
                    continue
                ok = True
                for oid, oc in placed.items():
                    need = RADIUS[kind] + RADIUS[STRONGHOLDS[oid]["type"]] + 2
                    if hex_dist(cand, oc) < need:
                        ok = False
                        break
                if not ok:
                    continue
                score = rad + 2 * sum(terrain[x] in WATER for x in fp) + sum(terrain[x] == "mountain" for x in fp)
                if best is None or score < best[0]:
                    best = (score, cand)
            if best is not None and best[0] <= rad:
                break
        if best is None:
            sys.exit(f"{sid}: 배치할 곳을 찾지 못함")
        if best[1] != target:
            moved = hex_dist(best[1], target)
            if moved >= 4:
                warnings.append(f"{sid}: 목표 위치에서 {moved}칸 이동")
        placed[sid] = best[1]

# 구조물 칠하기 (관문은 도로를 낸 뒤에)
tiles = {sid: {} for sid in STRONGHOLDS}
for sid, c in placed.items():
    kind = STRONGHOLDS[sid]["type"]
    if kind == "gate":
        occupied[c] = sid
        continue
    for x in footprint(c, kind):
        if kind == "major_city":
            t = "street" if hex_dist(x, c) <= 1 else "city_wall"
        elif kind in ("city", "fortress"):
            t = "street" if x == c else "city_wall"
        else:
            t = "street"
        terrain[x] = t
        occupied[x] = sid
        tiles[sid][x] = "center" if x == c else ("street" if t == "street" else "wall")

# ------------------------------------------------------------------ 3. 도로

ROAD_COST = {"road": 0.5, "plain": 1, "forest": 2, "marsh": 3, "mountain": 5, "river": 8, "ford": 2}


def find_road(a, b):
    start, goal = placed[a], placed[b]
    openq = [(0, 0, start)]
    came = {start: None}
    cost = {start: 0}
    while openq:
        _, g, cur = heapq.heappop(openq)
        if cur == goal:
            break
        if g > cost[cur]:
            continue
        for n in neighbors(*cur):
            t = terrain[n]
            owner = occupied.get(n)
            if owner is not None and owner not in (a, b):
                continue
            if t == "sea":
                continue
            if t in STRUCT:
                step = 3 if t == "city_wall" else 1
            elif t == "gate":
                step = 1
            else:
                step = ROAD_COST[t]
            ng = g + step
            if ng < cost.get(n, 1e18):
                cost[n] = ng
                came[n] = cur
                heapq.heappush(openq, (ng + hex_dist(n, goal) * 0.5, ng, n))
    if goal not in came:
        return None
    path, cur = [], goal
    while cur is not None:
        path.append(cur)
        cur = came[cur]
    return path[::-1]


links = sorted({tuple(sorted((s["id"], l))) for s in STRONGHOLDS.values() for l in s["links"]})
road_paths = {}
for a, b in links:
    path = find_road(a, b)
    if path is None:
        sys.exit(f"{a}–{b} 도로를 낼 수 없음")
    road_paths[(a, b)] = path
    for x in path:
        t = terrain[x]
        owner = occupied.get(x)
        if t == "city_wall":
            terrain[x] = "gate"
            tiles[owner][x] = "gate"
        elif t in ("street", "gate"):
            pass
        elif owner is not None:
            pass  # 관문 중심
        elif t == "river":
            terrain[x] = "ford"
        else:
            terrain[x] = "road"

# 관문 구조: 중심이 성문, 도로와 엇갈린 방향으로 성벽 2칸씩.
# 관문은 길목이어야 하므로 성벽 바깥으로 산을 이어 붙여 옆으로 돌아갈 수 없게 한다.
GATE_RIDGE = 4
for sid, c in placed.items():
    if STRONGHOLDS[sid]["type"] != "gate":
        continue
    road_dirs = [i for i, (dq, dr) in enumerate(DIRS)
                 if terrain.get((c[0] + dq, c[1] + dr)) in ("road", "ford")]
    k = road_dirs[0] if road_dirs else 0
    terrain[c] = "gate"
    tiles[sid][c] = "gate"
    for side in ((k + 2) % 6, (k + 5) % 6):
        x = c
        for step in range(2 + GATE_RIDGE):
            x = (x[0] + DIRS[side][0], x[1] + DIRS[side][1])
            if x not in terrain or terrain[x] in ("road", "ford", "sea", "river") or x in occupied:
                break
            if step < 2:
                terrain[x] = "city_wall"
                occupied[x] = sid
                tiles[sid][x] = "wall"
            else:
                terrain[x] = "mountain"
            # 산줄기를 두 줄로 두껍게 해 대각선으로 빠져나가지 못하게 한다
            y = (x[0] + DIRS[(side + 1) % 6][0], x[1] + DIRS[(side + 1) % 6][1])
            if step >= 2 and y in terrain and terrain[y] in ("plain", "forest", "marsh") and y not in occupied:
                terrain[y] = "mountain"

# 성문이 하나도 없는 도시는 첫 성벽을 성문으로
for sid, t in tiles.items():
    if STRONGHOLDS[sid]["type"] in ("major_city", "city", "fortress") and "gate" not in t.values():
        x = next(h for h, v in t.items() if v == "wall")
        t[x] = "gate"
        terrain[x] = "gate"
        warnings.append(f"{sid}: 도로가 성벽을 지나지 않아 성문을 임의로 지정")

# ------------------------------------------------------------------ 4. 도시 영역

CITY_KINDS = ("major_city", "city")
cities = [sid for sid in STRONGHOLDS if STRONGHOLDS[sid]["type"] in CITY_KINDS]
TERR_COST = {"road": 1, "plain": 1, "forest": 2, "marsh": 3, "mountain": 4, "river": 6, "ford": 2,
             "street": 1, "city_wall": 1, "gate": 1}
owner_of = {}
dist = {}
pq = []
for sid in cities:
    c = placed[sid]
    dist[c] = 0
    owner_of[c] = sid
    heapq.heappush(pq, (0, sid, c))
while pq:
    d, sid, cur = heapq.heappop(pq)
    if d > dist.get(cur, 1e18) or owner_of.get(cur) != sid:
        continue
    for n in neighbors(*cur):
        t = terrain[n]
        if t == "sea":
            continue
        o = occupied.get(n)
        if o is not None and STRONGHOLDS[o]["type"] in CITY_KINDS and o != sid:
            continue
        nd = d + TERR_COST[t]
        if nd < dist.get(n, 1e18):
            dist[n] = nd
            owner_of[n] = sid
            heapq.heappush(pq, (nd, sid, n))

for sid in STRONGHOLDS:
    if STRONGHOLDS[sid]["type"] not in CITY_KINDS and owner_of.get(placed[sid]) is None:
        warnings.append(f"{sid}: 어느 도시 영역에도 속하지 않음")

# ------------------------------------------------------------------ 5. 시설 슬롯

slots = {}
for sid, s in STRONGHOLDS.items():
    n = s["facility_slots"]
    if n == 0:
        continue
    c = placed[sid]
    home = owner_of.get(c)
    lo, hi = {"major_city": (3, 6), "city": (2, 5), "port": (1, 2)}.get(s["type"], (2, 5))
    cands = []
    for rad in range(lo, hi + 1):
        for x in ring(c, rad):
            if x not in terrain or terrain[x] not in ("plain", "forest") or x in occupied:
                continue
            if owner_of.get(x) != home:
                continue
            if any(occupied.get(nb) not in (None, sid) for nb in neighbors(*x)):
                continue
            ang = math.atan2(x[1] - c[1], x[0] - c[0])
            cands.append((rad, terrain[x] != "plain", ang, x))
    cands.sort()
    chosen = []
    for _, _, _, x in cands:
        if any(x in road_set for road_set in []):
            continue
        chosen.append(x)
        if len(chosen) == n:
            break
    if len(chosen) < n:
        warnings.append(f"{sid}: 시설 슬롯 {len(chosen)}/{n}개만 확보")
    slots[sid] = [{"q": x[0], "r": x[1], "waterside": adjacent_water(x)} for x in chosen]
    for x in chosen:
        occupied.setdefault(x, None)

# ------------------------------------------------------------------ 6. 출력

ALPHA = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"
city_index = {sid: ALPHA[i] for i, sid in enumerate(cities)}

rows, terr_rows = [], []
for r in range(H):
    tr, cr = [], []
    for c in range(W):
        h = axial(c, r)
        tr.append(CODE[terrain[h]])
        o = owner_of.get(h)
        cr.append(city_index[o] if o else "-")
    terr_rows.append("".join(tr))
    rows.append("".join(cr))

world = {
    "_comment": "tools/mapgen/generate_world.py가 생성. 직접 고치지 말고 geography.json을 고친 뒤 다시 생성할 것.",
    "width": W, "height": H,
    "coords": "terrain/territory 문자열은 행(row) 단위, 열(col)은 odd-r 오프셋. axial q = col - (row >> 1), r = row",
    "projection": {"lon_min": G["lon_min"], "lat_max": G["lat_max"], "dlon": DLON, "dlat": DLAT},
    "terrain_legend": {v: k for k, v in CODE.items()},
    "terrain": terr_rows,
    "territory_legend": {city_index[s]: s for s in cities},
    "territory": rows,
    "strongholds": [
        {"id": sid, "center": {"q": placed[sid][0], "r": placed[sid][1]},
         "tiles": [{"q": h[0], "r": h[1], "kind": k} for h, k in sorted(tiles[sid].items(), key=lambda kv: (kv[0][1], kv[0][0]))],
         "territory_of": owner_of.get(placed[sid]),
         "facility_slots": slots.get(sid, [])}
        for sid in STRONGHOLDS
    ],
    "roads": [{"from": a, "to": b, "length": len(p) - 1} for (a, b), p in road_paths.items()],
}
(ROOT / "data/map/world.json").write_text(json.dumps(world, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")

for s in SH_DATA["strongholds"]:
    s["hex"] = {"q": placed[s["id"]][0], "r": placed[s["id"]][1]}
SH_PATH.write_text(json.dumps(SH_DATA, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

counts = {}
for t in terrain.values():
    counts[t] = counts.get(t, 0) + 1
land = sum(v for k, v in counts.items() if k != "sea")
print(f"맵 {W}×{H} = {W * H}칸 (육지 {land}칸)")
print("지형:", ", ".join(f"{k} {v}" for k, v in sorted(counts.items(), key=lambda kv: -kv[1])))
print(f"거점 {len(placed)}개, 도로 {len(road_paths)}개 (평균 {sum(len(p) for p in road_paths.values()) / len(road_paths):.1f}칸)")
for w in warnings:
    print("주의:", w)
