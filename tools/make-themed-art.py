# Card backs and booster boxes themed per set, made from the red originals: the red family is
# shifted to the set's colour (cream, greys and outlines stay), so every wear stage carries over.
# Card backs get the set's surface detail (patina, drips, bubbles); boxes get the set's pack
# emblem on their label. Writes card_back_<set>[_light|_worn|_ruined] and
# booster_box[_open]_<set>. Run make-pack-art.py first (emblems), then make-slab-art.py.
# Usage: python make-themed-art.py <repo root>
import colorsys
import sys
from PIL import Image

root = sys.argv[1]
items = f"{root}/Textures/Items"

THEMES = {
    # hue (0-1), saturation and brightness factors, surface detail colours
    "rustwave": {"hue": 0.065, "sat": 1.0, "val": 1.05, "detail": (84, 150, 130), "kind": "specks"},
    "doomed": {"hue": 0.78, "sat": 0.55, "val": 0.75, "detail": (150, 200, 70), "kind": "drips"},
    "oxygen": {"hue": 0.56, "sat": 0.55, "val": 1.3, "detail": (235, 250, 255), "kind": "bubbles"},
    "grace": {"hue": 0.12, "sat": 0.7, "val": 1.3, "detail": (255, 244, 200), "kind": "specks"},
    "brood": {"hue": 0.24, "sat": 0.75, "val": 0.85, "detail": (232, 222, 172), "kind": "specks"},
    "neural": {"hue": 0.85, "sat": 1.0, "val": 1.05, "detail": (80, 220, 230), "kind": "drips"},
    "fates": {"hue": 0.98, "sat": 0.9, "val": 0.5, "detail": (210, 60, 70), "kind": "drips"},
    "ascension": {"hue": 0.72, "sat": 0.45, "val": 1.3, "detail": (255, 250, 220), "kind": "specks"},
    "singularity": {"hue": 0.70, "sat": 0.9, "val": 0.45, "detail": (250, 170, 70), "kind": "specks"},
    "recursion": {"hue": 0.38, "sat": 0.85, "val": 0.55, "detail": (90, 240, 120), "kind": "drips"},
}


def themed(img, t):
    out = img.copy()
    p = out.load()
    for y in range(out.height):
        for x in range(out.width):
            r, g, b, a = p[x, y]
            if a == 0:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if s > 0.2 and (h < 0.12 or h > 0.92):   # the red family
                nr, ng, nb = colorsys.hsv_to_rgb(t["hue"], min(1, s * t["sat"]), min(1, v * t["val"]))
                p[x, y] = (round(nr * 255), round(ng * 255), round(nb * 255), a)
    return out


def is_body(px, x, y, t):
    """A themed (recoloured) pixel: safe to paint detail on."""
    r, g, b, a = px[x, y]
    if a == 0:
        return False
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    return s > 0.15 and abs(h - t["hue"]) < 0.08


DETAIL = {
    "specks": [(5, 4), (10, 11), (9, 5), (6, 12)],
    "drips": [(6, 3), (6, 4), (6, 5), (10, 3), (10, 4)],
    "bubbles": [(10, 4), (5, 10), (9, 11), (6, 4)],
}

for name, t in THEMES.items():
    # card backs, every wear stage
    for suffix in ("", "_light", "_worn", "_ruined"):
        img = themed(Image.open(f"{items}/card_back_red{suffix}.png").convert("RGBA"), t)
        p = img.load()
        for (x, y) in DETAIL[t["kind"]]:
            if is_body(p, x, y, t):
                p[x, y] = t["detail"] + (255,)
        img.save(f"{items}/card_back_{name}{suffix}.png")

    # boxes: recolour, then the pack's emblem on the label in place of the red diamond
    pack = Image.open(f"{items}/booster_pack_{name}.png").convert("RGBA")
    emblem = pack.crop((5, 9, 11, 13))   # the pack's 6 x 4 emblem window
    for opened in ("", "_open"):
        src = Image.open(f"{items}/booster_box{opened}_red.png").convert("RGBA")
        img = themed(src, t)
        sp, p = src.load(), img.load()
        # the label: the rows and columns of cream; the diamond: the red pixels inside it
        CREAM = (213, 210, 184, 255)
        label = [(x, y) for y in range(src.height) for x in range(src.width) if sp[x, y] == CREAM]
        rows = sorted({y for _, y in label if sum(1 for _, yy in label if yy == y) >= 8})
        if rows:
            lx0 = min(x for x, y in label if y in rows); lx1 = max(x for x, y in label if y in rows)
            diamond = [(x, y) for y in range(rows[0], rows[-1] + 1) for x in range(lx0, lx1 + 1)
                       if sp[x, y][3] and colorsys.rgb_to_hsv(*(c / 255 for c in sp[x, y][:3]))[1] > 0.3]
            diamond = [(x, y) for x, y in diamond if x > (lx0 + lx1) // 2]   # the right half: the left has text lines
            if diamond:
                x0, x1 = min(x for x, _ in diamond), max(x for x, _ in diamond)
                y0, y1 = min(y for _, y in diamond), max(y for _, y in diamond)
                for y in range(y0, y1 + 1):
                    for x in range(x0, x1 + 1):
                        p[x, y] = CREAM
                img.alpha_composite(emblem, ((x0 + x1 + 1) // 2 - 3, (y0 + y1 + 1) // 2 - 2))
        img.save(f"{items}/booster_box{opened}_{name}.png")
print("ok")
