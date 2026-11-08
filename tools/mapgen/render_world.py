#!/usr/bin/env python3
"""world.json 미리보기 이미지.

사용법: python3 tools/mapgen/render_world.py [시나리오ID] [출력경로]
  시나리오를 주면 도시 영역을 세력 색으로 칠한다.
"""
import json
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
world = json.loads((ROOT / "data/map/world.json").read_text(encoding="utf-8"))
names = {s["id"]: s["name"]["ko"] for s in json.loads((ROOT / "data/base/strongholds.json").read_text(encoding="utf-8"))["strongholds"]}
kinds = {s["id"]: s["type"] for s in json.loads((ROOT / "data/base/strongholds.json").read_text(encoding="utf-8"))["strongholds"]}
scenario = sys.argv[1] if len(sys.argv) > 1 and sys.argv[1] != "-" else None
out = Path(sys.argv[2]) if len(sys.argv) > 2 else ROOT / ("docs/images/world_" + (scenario or "terrain") + ".png")

S = 4.2  # 헥스 반지름(px)
W, H = world["width"], world["height"]
legend = world["terrain_legend"]
COLORS = {
    "plain": (222, 212, 180), "road": (150, 112, 70), "forest": (128, 152, 108), "mountain": (138, 126, 108),
    "marsh": (160, 178, 150), "river": (104, 150, 196), "sea": (164, 190, 210), "ford": (150, 184, 214),
    "street": (196, 72, 60), "city_wall": (60, 56, 52), "gate": (214, 166, 40),
}

owner_color = {}
if scenario:
    sc = json.loads((ROOT / f"data/scenarios/{scenario}/scenario.json").read_text(encoding="utf-8"))
    fac_color = {f["id"]: tuple(int(f["color"][i:i + 2], 16) for i in (1, 3, 5)) for f in sc["factions"]}
    for c in sc["cities"]:
        if c["owner"]:
            owner_color[c["stronghold"]] = fac_color[c["owner"]]
terr_owner = world["territory_legend"]

img_w = int(math.sqrt(3) * S * (W + 0.5)) + 4
img_h = int(1.5 * S * H + S) + 4
img = Image.new("RGB", (img_w, img_h), (245, 240, 228))
d = ImageDraw.Draw(img)


def center(col, row):
    return (math.sqrt(3) * S * (col + 0.5 * (row & 1) + 0.5) + 2, 1.5 * S * row + S + 2)


def poly(cx, cy):
    return [(cx + S * math.cos(math.radians(60 * i - 30)), cy + S * math.sin(math.radians(60 * i - 30))) for i in range(6)]


def blend(a, b, t):
    return tuple(int(a[i] * (1 - t) + b[i] * t) for i in range(3))


for row in range(H):
    for col in range(W):
        t = legend[world["terrain"][row][col]]
        color = COLORS[t]
        if scenario and t not in ("sea",):
            o = terr_owner.get(world["territory"][row][col])
            if o in owner_color:
                color = blend(color, owner_color[o], 0.45 if t not in ("street", "city_wall", "gate") else 0.2)
        d.polygon(poly(*center(col, row)), fill=color)

# 영역 경계선
for row in range(H):
    for col in range(W):
        me = world["territory"][row][col]
        if me == "-":
            continue
        cx, cy = center(col, row)
        for dc, dr, edge in ((1, 0, 0), (0 if row % 2 == 0 else 1, 1, 1), (-1 if row % 2 == 0 else 0, 1, 2)):
            c2, r2 = col + dc, row + dr
            if 0 <= c2 < W and 0 <= r2 < H and world["territory"][r2][c2] not in (me, "-"):
                p = poly(cx, cy)
                a, b = [(p[0], p[1]), (p[1], p[2]), (p[2], p[3])][edge]
                d.line([a, b], fill=(70, 60, 50), width=1)

# 시설 슬롯
for s in world["strongholds"]:
    for sl in s["facility_slots"]:
        col, row = sl["q"] + (sl["r"] >> 1), sl["r"]
        cx, cy = center(col, row)
        d.ellipse([cx - 1.3, cy - 1.3, cx + 1.3, cy + 1.3], fill=(90, 70, 40))

font = ImageFont.truetype("/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc", 12)
small = ImageFont.truetype("/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc", 10)
for s in world["strongholds"]:
    q, r = s["center"]["q"], s["center"]["r"]
    cx, cy = center(q + (r >> 1), r)
    k = kinds[s["id"]]
    f = font if k in ("major_city", "city") else small
    text = names[s["id"]]
    tw = d.textlength(text, font=f)
    y = cy + (11 if k == "major_city" else 6)
    d.text((cx - tw / 2, y), text, font=f, fill=(20, 20, 20) if k in ("major_city", "city") else (80, 50, 20),
           stroke_width=2, stroke_fill=(250, 246, 236))

out.parent.mkdir(parents=True, exist_ok=True)
img.save(out)
print(out, img.size)
