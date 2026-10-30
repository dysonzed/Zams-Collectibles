# Steam Workshop images, composed from the mod's own pixel art:
#   preview.png                     main Workshop preview (uploaded by publish-workshop.ps1)
#   workshop/gallery_sets.png       every set's pack, box, card back and graded slab, by tier
#   workshop/gallery_features.png   the mod's features at a glance
# Gallery images are added by hand on the Workshop page (steamcmd only uploads the preview).
# Usage: python make-workshop-images.py <repo root>
import json
import os
import random
import sys
from PIL import Image, ImageDraw, ImageFont

root = sys.argv[1]
items = f"{root}/Textures/Items"
paper = f"{root}/Textures/Paper"
out_dir = f"{root}/workshop"
os.makedirs(out_dir, exist_ok=True)
sets = json.load(open(f"{root}/Data/sets.json", encoding="utf-8"))["sets"]

FONTS = "C:/Windows/Fonts"
BOLD = f"{FONTS}/segoeuib.ttf"
REGULAR = f"{FONTS}/segoeui.ttf"
TITLE = f"{FONTS}/impact.ttf"
WHITE, CREAM, GOLD, DIM = (255, 255, 255), (232, 228, 214), (255, 210, 70), (150, 156, 176)
INK = (20, 16, 30)


def sprite(name, scale, folder=items):
    img = Image.open(f"{folder}/{name}.png").convert("RGBA")
    return img.resize((img.width * scale, img.height * scale), Image.NEAREST)


def background(w, h, seed=7):
    """A dark station-night gradient with a scatter of stars."""
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


def thumbnail(name, max_w, max_h, folder=items):
    """Illustrated art (not pixel sprites): scaled smoothly to fit, with a dark frame."""
    art = Image.open(f"{folder}/{name}.png").convert("RGBA")
    art.thumbnail((max_w - 6, max_h - 6), Image.LANCZOS)
    framed = Image.new("RGBA", (art.width + 6, art.height + 6), INK + (255,))
    framed.alpha_composite(art, (3, 3))
    return framed


def on_disc(icon, colour=(64, 170, 170)):
    """Scenario icons are dark rings drawn for the game's teal disc."""
    disc = Image.new("RGBA", icon.size, (0, 0, 0, 0))
    ImageDraw.Draw(disc).ellipse([2, 2, icon.width - 3, icon.height - 3], fill=colour + (255,))
    disc.alpha_composite(icon)
    return disc


def text(img, xy, s, size, font=BOLD, fill=WHITE, anchor="la", stroke=3):
    d = ImageDraw.Draw(img)
    f = ImageFont.truetype(font, size)
    d.text(xy, s, font=f, fill=fill, anchor=anchor, stroke_width=stroke, stroke_fill=INK)


def paste_center(img, piece, cx, y):
    img.alpha_composite(piece, (int(cx - piece.width / 2), int(y)))


# ------------------------------------------------------------ main preview
W, H = 1024, 576
img = background(W, H)
logo = Image.open(f"{paper}/zamco_logo.png").convert("RGBA")
logo = logo.resize((logo.width // 2 * 3, logo.height // 2 * 3), Image.NEAREST)   # the 168 x 64 art at 3x
paste_center(img, logo, W / 2, 6)
text(img, (W / 2, 206), "ZAM'S COLLECTIBLES", 64, TITLE, WHITE, "ma", 4)
text(img, (W / 2, 280), "The Aug Warriors trading card game, on the station", 26, BOLD, CREAM, "ma", 3)
packs = [sprite(s["packSprite"], 5) for s in sets]
gap = 12
row_w = sum(p.width for p in packs) + gap * (len(packs) - 1)
x = (W - row_w) // 2
for p in packs:
    img.alpha_composite(p, (x, 326))
    x += p.width + gap
text(img, (W / 2, 506), "10 SETS  ·  3 TIERS  ·  120+ CARDS  ·  A CARD SHOP AFTER HOURS", 22, BOLD, GOLD, "ma", 3)
img.convert("RGB").save(f"{root}/preview.png", optimize=True)

# ------------------------------------------------------------ the sets, by tier
W, H = 1280, 760
img = background(W, H, 11)
text(img, (W / 2, 18), "TEN SETS. THREE TIERS.", 54, TITLE, WHITE, "ma", 4)
tiers = {}
for s in sets:
    tiers.setdefault(s.get("tier", 1), []).append(s)
y = 100
for tier in sorted(tiers):
    row = tiers[tier]
    text(img, (40, y + 4), f"TIER {tier}", 30, TITLE, GOLD, "la", 3)
    cell = (W - 200) // len(row)
    for i, s in enumerate(row):
        cx = 180 + cell * i + cell // 2
        pack = sprite(s["packSprite"], 3)
        box = sprite(f"booster_box_{s['id']}", 3)
        back = sprite(s["cardSprite"], 4)
        slab = sprite(s["cardSprite"].replace("card_back", "card_slab"), 4)
        left = cx - (pack.width + 10 + box.width) // 2
        img.alpha_composite(pack, (left, y))
        img.alpha_composite(box, (left + pack.width + 10, y))
        img.alpha_composite(back, (left + pack.width + 10, y + box.height - 4))
        img.alpha_composite(slab, (left + pack.width + 10 + back.width - 8, y + box.height - 4))
        text(img, (cx, y + 172), s["name"], 22, BOLD, WHITE, "ma", 3)
        text(img, (cx, y + 198), s["code"], 16, BOLD, DIM, "ma", 2)
    y += 220
img.convert("RGB").save(f"{out_dir}/gallery_sets.png", optimize=True)

# ------------------------------------------------------------ features
W, H = 1280, 720
img = background(W, H, 23)
text(img, (W / 2, 18), "COLLECT. GRADE. SEAL. PLAY.", 54, TITLE, WHITE, "ma", 4)
features = [
    (sprite("booster_box_singularity", 4), "Packs & Boxes", "Every set has its own packs,\nboxes and art"),
    (sprite("card_slab_grace", 7), "Card Grading", "Hidden conditions, graded slabs.\nA gem 10 is a jackpot"),
    (sprite("collector_box", 6), "Collector's Box", "Seal complete sets.\nAdd the ultra for a Master Set"),
    (sprite("dealer_certificate", 4), "Dealer Certificate", "Display it to draw pack flippers,\ndeck builders and graders"),
    (thumbnail("den_card", 200, 150), "The Downside Den", "A card shop after hours.\nPacks, singles and a back table"),
    (sprite("card_back_recursion", 7), "Aug Warriors", "A full card game against regulars\nwith hidden play styles"),
    (on_disc(sprite("scenario_card_dealer", 6)), "Card Dealer Start", "A new starting scenario\nwith the Den already open"),
    (sprite("booster_pack_fates", 4), "Three Tiers", "Unlock stronger sets through\nZamco licenses in Wild's Network"),
]
cols, rows = 4, 2
cw, ch = W // cols, (H - 110) // rows
for i, (icon, title, body) in enumerate(features):
    cx = cw * (i % cols) + cw // 2
    top = 110 + ch * (i // cols)
    icon_box = 150
    paste_center(img, icon, cx, top + (icon_box - icon.height) / 2)
    text(img, (cx, top + icon_box + 12), title, 26, BOLD, GOLD, "ma", 3)
    for j, line in enumerate(body.split("\n")):
        text(img, (cx, top + icon_box + 48 + j * 24), line, 18, REGULAR, CREAM, "ma", 2)
img.convert("RGB").save(f"{out_dir}/gallery_features.png", optimize=True)

for f in (f"{root}/preview.png", f"{out_dir}/gallery_sets.png", f"{out_dir}/gallery_features.png"):
    print(f, os.path.getsize(f) // 1024, "KB")
