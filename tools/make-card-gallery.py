# Workshop gallery images from in-game card renders (F9 > Zam's Collectibles > Capture Card Renders,
# saved to Pictures\Zams Card Renders as <set>_<nn>.png and <set>_<nn>_graded.png):
#   workshop/cards_<set>.png      every captured card of a set, with the set's pack
#   workshop/cards_graded.png     the graded rares and ultras, if they were captured
# Sets without renders are skipped. Add the images to the Workshop page by hand.
# Usage: python make-card-gallery.py <repo root> [renders folder]
import json
import os
import random
import sys
from PIL import Image, ImageDraw, ImageFont

root = sys.argv[1]
renders = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.expanduser("~"), "Pictures", "Zams Card Renders")
items = f"{root}/Textures/Items"
out_dir = f"{root}/workshop"
os.makedirs(out_dir, exist_ok=True)
sets = json.load(open(f"{root}/Data/sets.json", encoding="utf-8"))["sets"]

FONTS = "C:/Windows/Fonts"
BOLD = f"{FONTS}/segoeuib.ttf"
TITLE = f"{FONTS}/impact.ttf"
WHITE, CREAM, GOLD, DIM = (255, 255, 255), (232, 228, 214), (255, 210, 70), (150, 156, 176)
INK = (20, 16, 30)

CARD_H = 300          # every card is shown at this height
PER_ROW = 7
GAP, MARGIN, HEADER = 18, 40, 110


def background(w, h, seed=7):
    """A dark station-night gradient with a scatter of stars (as make-workshop-images.py)."""
    img = Image.new("RGBA", (w, h))
    d = ImageDraw.Draw(img)
    for y in range(h):
        t = y / h
        d.line([(0, y), (w, y)], fill=(int(24 + 18 * t), int(20 + 12 * t), int(44 + 20 * t), 255))
    rnd = random.Random(seed)
    for _ in range(w * h // 2500):
        x, y = rnd.randrange(w), rnd.randrange(h)
        c = rnd.choice([(90, 96, 130), (130, 136, 170), (200, 204, 230)])
        d.point((x, y), fill=c + (255,))
    return img


def text(img, xy, s, size, font=BOLD, fill=WHITE, anchor="la", stroke=3):
    d = ImageDraw.Draw(img)
    d.text(xy, s, font=ImageFont.truetype(font, size), fill=fill, anchor=anchor, stroke_width=stroke, stroke_fill=INK)


def card(path):
    """A render scaled to CARD_H: whole-number steps stay crisp, anything else is smoothed."""
    img = Image.open(path).convert("RGBA")
    f = CARD_H / img.height
    method = Image.NEAREST if abs(f - round(f)) < 0.01 else Image.LANCZOS
    return img.resize((round(img.width * f), CARD_H), method)


def sheet(title, subtitle, cards, pack=None, accent=CREAM):
    """Cards in rows of PER_ROW under a title, with the set's pack beside the title."""
    rows = [cards[i:i + PER_ROW] for i in range(0, len(cards), PER_ROW)]
    cw = max(c.width for c in cards)
    w = MARGIN * 2 + PER_ROW * cw + (PER_ROW - 1) * GAP
    h = HEADER + len(rows) * CARD_H + (len(rows) - 1) * GAP + MARGIN
    img = background(w, h, seed=len(title))
    x0 = MARGIN
    if pack is not None:
        p = Image.open(pack).convert("RGBA")
        p = p.resize((p.width * 3, p.height * 3), Image.NEAREST)
        img.alpha_composite(p, (MARGIN, (HEADER - p.height) // 2))
        x0 += p.width + 20
    text(img, (x0, 24), title, 46, TITLE, WHITE, "la", 4)
    text(img, (x0, 78), subtitle, 20, BOLD, accent, "la", 3)
    for r, row in enumerate(rows):
        row_w = len(row) * cw + (len(row) - 1) * GAP
        x = (w - row_w) // 2
        y = HEADER + r * (CARD_H + GAP)
        for c in row:
            img.alpha_composite(c, (x + (cw - c.width) // 2, y))
            x += cw + GAP
    return img


made = []
for s in sets:
    paths = [f"{renders}/{s['id']}_{c['number']:02d}.png" for c in s["cards"]]
    found = [card(p) for p in paths if os.path.exists(p)]
    if not found:
        continue
    out = f"{out_dir}/cards_{s['id']}.png"
    sheet(s["name"].upper(), f"Tier {s['tier']}  ·  {len(s['cards'])} cards", found, f"{items}/{s['packSprite']}.png").convert("RGB").save(out)
    made.append(f"{out} ({len(found)} cards)")

graded = []
for s in sets:
    for c in s["cards"]:
        p = f"{renders}/{s['id']}_{c['number']:02d}_graded.png"
        if os.path.exists(p):
            graded.append(card(p))
if graded:
    out = f"{out_dir}/cards_graded.png"
    sheet("GRADED GEMS", "Sealed in a graded slab by the Card Grader", graded, accent=GOLD).convert("RGB").save(out)
    made.append(f"{out} ({len(graded)} slabs)")

print("\n".join(made) if made else f"No renders found in {renders}")
