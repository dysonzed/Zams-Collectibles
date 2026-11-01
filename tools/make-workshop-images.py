# Steam Workshop images, composed from the mod's own pixel art:
#   preview.png                     main Workshop preview (uploaded by publish-workshop.ps1)
#   workshop/gallery_sets.png       every set's pack, box, card back and graded slab, by tier
#   workshop/gallery_features.png   the mod's features at a glance
#   workshop/gallery_multiplayer.png  deck codes, step by step (the first gallery image)
#   workshop/banner_multiplayer.png   the "Multiplayer(-ish)!" banner at the top of the description; the
#                                     description loads it from GitHub, so push it before publishing
# Gallery images are added by hand on the Workshop page (steamcmd only uploads the preview).
# Usage: python make-workshop-images.py <repo root>
import json
import math
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
MONO = f"{FONTS}/consolab.ttf"
WHITE, CREAM, GOLD, DIM = (255, 255, 255), (232, 228, 214), (255, 210, 70), (150, 156, 176)
INK = (20, 16, 30)
TEAL, TEAL_DARK = (60, 196, 196), (26, 110, 116)
PAPER = (232, 228, 214)


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


# ------------------------------------------------------------ multiplayer pieces
# Frame ramps (dark to light) from CardFace.cs, by the set's frameColor.
FRAMES = {
    "red": [(0x64, 0x34, 0x3b), (0x85, 0x37, 0x37), (0x9c, 0x37, 0x38), (0xcc, 0x77, 0x55)],
    "teal": [(0x27, 0x56, 0x54), (0x2f, 0x75, 0x6f), (0x45, 0x91, 0x8b), (0x67, 0xaa, 0xa5)],
    "purple": [(0x45, 0x39, 0x43), (0x6b, 0x52, 0x69), (0x8c, 0x6a, 0x95), (0xa2, 0x72, 0xac)],
    "slate": [(0x30, 0x3a, 0x41), (0x44, 0x46, 0x4c), (0x5f, 0x68, 0x68), (0x80, 0x92, 0x9b)],
}
CARD_INK, CARD_CREAM = (0x29, 0x22, 0x34), (0xe8, 0xe4, 0xd6)


def card_face(set_id, number, scale):
    """A simplified card face in the game's layout: frame, art, name plate and text box, in hard pixels."""
    s = next(x for x in sets if x["id"] == set_id)
    ramp = FRAMES.get(s["frameColor"], FRAMES["slate"])
    w, h = 71, 100
    c = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(c)
    d.rectangle([0, 0, w - 1, h - 1], fill=ramp[2], outline=CARD_INK)
    d.line([(1, 1), (w - 2, 1)], fill=ramp[3])
    d.line([(1, 1), (1, h - 2)], fill=ramp[3])
    d.line([(1, h - 2), (w - 2, h - 2)], fill=ramp[1])
    d.line([(w - 2, 1), (w - 2, h - 2)], fill=ramp[1])
    for x, y in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
        c.putpixel((x, y), (0, 0, 0, 0))
    d.rectangle([3, 3, 67, 50], fill=(0x1a, 0x16, 0x20), outline=CARD_INK)
    c.alpha_composite(Image.open(f"{items}/art_{set_id}_{number:02d}.png").convert("RGBA"), (4, 4))
    d.rectangle([3, 53, 67, 62], fill=CARD_CREAM, outline=CARD_INK)
    d.rectangle([3, 65, 67, 96], fill=CARD_CREAM, outline=CARD_INK)
    for i, length in enumerate((52, 44, 48, 30)):
        d.line([(7, 70 + i * 6), (7 + length, 70 + i * 6)], fill=(0xb5, 0xab, 0x94))
    c = c.resize((w * scale, h * scale), Image.NEAREST)
    name = next(x["name"] for x in s["cards"] if x["number"] == number)
    f = ImageFont.truetype(BOLD, int(5.2 * scale))
    ImageDraw.Draw(c).text((w * scale / 2, 57.5 * scale), name, font=f, fill=CARD_INK, anchor="mm")
    return c


def fan(cards, scale, spread, flip=False):
    """Cards held in a fan, like a hand; flip mirrors the angles for the other player."""
    faces = [card_face(sid, n, scale) for sid, n in cards]
    angles = [spread * (i - (len(faces) - 1) / 2) for i in range(len(faces))]
    if flip:
        angles = [-a for a in angles]
    cw, ch = faces[0].size
    canvas = Image.new("RGBA", (int(cw * 2.8), int(ch * 1.6)), (0, 0, 0, 0))
    pivot = (canvas.width / 2, canvas.height * 0.98)
    for face, a in zip(faces, angles):
        layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
        layer.alpha_composite(face, (int(pivot[0] - cw / 2), int(pivot[1] - ch * 1.3)))
        canvas.alpha_composite(layer.rotate(-a, resample=Image.BICUBIC, center=pivot))
    return canvas.crop(canvas.getbbox())


def starburst(w, h, points=18, depth=0.16, seed=3):
    """The ZAMCO logo's teal starburst, jagged and hand-cut."""
    rnd = random.Random(seed)
    jitter = [rnd.random() * 0.06 for _ in range(points * 2)]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx, cy = w / 2, h / 2

    def poly(rx, ry):
        pts = []
        for i in range(points * 2):
            a = math.pi * i / points
            k = 1 if i % 2 == 0 else 1 - depth - jitter[i]
            pts.append((cx + math.cos(a) * rx * k, cy + math.sin(a) * ry * k))
        return pts

    d.polygon(poly(w / 2, h / 2), fill=INK)
    d.polygon(poly(w / 2 - 8, h / 2 - 8), fill=TEAL_DARK)
    d.polygon(poly(w / 2 - 14, h / 2 - 14), fill=TEAL)
    return img


def sticker(w, h, size, angle=-6, sub=None, label="MULTIPLAYER(-ISH)!", star=False):
    """The multiplayer sticker: gold Impact on the teal starburst, tilted. star adds a big raised
    asterisk after the label, pointing at the fine print."""
    img = starburst(w, h)
    d = ImageDraw.Draw(img)
    f = ImageFont.truetype(TITLE, size)
    y = h / 2 - (size * 0.22 if sub else 0)
    fs = ImageFont.truetype(TITLE, int(size * 1.7))   # Impact's asterisk is small: draw it big
    star_w = fs.getlength("*") + size * 0.06 if star else 0
    x = w / 2 - star_w / 2
    d.text((x + 4, y + 5), label, font=f, fill=INK, anchor="mm", stroke_width=6, stroke_fill=INK)
    d.text((x, y), label, font=f, fill=GOLD, anchor="mm", stroke_width=5, stroke_fill=INK)
    if star:
        sx = x + f.getlength(label) / 2 + size * 0.06
        d.text((sx + 3, y - size * 0.05 + 4), "*", font=fs, fill=INK, anchor="lm", stroke_width=6, stroke_fill=INK)
        d.text((sx, y - size * 0.05), "*", font=fs, fill=WHITE, anchor="lm", stroke_width=5, stroke_fill=INK)
    if sub:
        fs = ImageFont.truetype(BOLD, int(size * 0.34))
        d.text((w / 2, y + size * 0.68), sub, font=fs, fill=WHITE, anchor="mm", stroke_width=3, stroke_fill=INK)
    return img.rotate(-angle, resample=Image.BICUBIC, expand=True)


def code_slip(code, size, pad=18):
    """A deck code on a cream slip of paper."""
    f = ImageFont.truetype(MONO, size)
    slip = Image.new("RGBA", (int(f.getlength(code)) + pad * 2, size + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(slip)
    d.rectangle([0, 0, slip.width - 1, slip.height - 1], fill=PAPER, outline=INK, width=3)
    d.text((pad, slip.height / 2), code, font=f, fill=INK, anchor="lm")
    return slip


def bubble(message, code, size, pad=20):
    """A chat message with a tail, for "send it to a friend"."""
    f = ImageFont.truetype(BOLD, size)
    fm = ImageFont.truetype(MONO, int(size * 0.9))
    w = max(int(f.getlength(message)), int(fm.getlength(code))) + pad * 2
    h = size * 2 + pad * 2 + 10
    fill = (70, 78, 140)
    img = Image.new("RGBA", (w + 4, h + 26), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(28, h - 4), (60, h - 4), (22, h + 24)], fill=fill, outline=INK, width=3)
    d.rounded_rectangle([0, 0, w, h], radius=18, fill=fill, outline=INK, width=3)
    d.rectangle([31, h - 6, 57, h - 1], fill=fill)
    d.text((pad, pad), message, font=f, fill=WHITE, anchor="la")
    d.text((pad, pad + size + 12), code, font=fm, fill=GOLD, anchor="la")
    return img


def arrow(w, h, colour=GOLD):
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(img).polygon(
        [(0, h * 0.35), (w * 0.6, h * 0.35), (w * 0.6, 0), (w, h / 2), (w * 0.6, h), (w * 0.6, h * 0.65), (0, h * 0.65)],
        fill=colour, outline=INK, width=3)
    return img


YOURS = [("rustwave", 12), ("doomed", 13), ("oxygen", 12)]
THEIRS = [("brood", 13), ("grace", 1), ("doomed", 12)]


# ------------------------------------------------------------ main preview
# The Workshop thumbnail and Mods menu banner (under 1 MB): the title, two hands facing off, the sticker.
W, H = 1024, 576
img = background(W, H)
logo = Image.open(f"{paper}/zamco_logo.png").convert("RGBA")   # the 168 x 64 art at 2x
paste_center(img, logo, W / 2, 8)
text(img, (W / 2, 136), "ZAM'S COLLECTIBLES", 60, TITLE, WHITE, "ma", 4)
text(img, (W / 2, 206), "The Aug Warriors trading card game, on the station", 24, BOLD, CREAM, "ma", 3)
left, right = fan(YOURS, 2, 14), fan(THEIRS, 2, 14, flip=True)
img.alpha_composite(left, (W // 2 - 64 - left.width, 250))
img.alpha_composite(right, (W // 2 + 64, 250))
text(img, (W / 2, 326), "VS", 76, TITLE, GOLD, "ma", 5)
tag = sticker(380, 136, 48, angle=-7, label="MULTIPLAYER", star=True)
img.alpha_composite(tag, (W - tag.width + 4, -22))   # the empty corner beside the logo
text(img, (W / 2, 500), "SEND YOUR DECK TO A FRIEND  ·  10 SETS  ·  120+ CARDS  ·  A CARD SHOP AFTER HOURS", 20, BOLD, GOLD, "ma", 3)
# Zamco's fine print, for the asterisk
for i, line in enumerate((
        "*Zamco Electronics defines multiplayer as one (1) player versus one (1) deck code supplied by a second player.",
        "Second player not included. Opponent operated by Zamco-certified decision circuitry. Friendships not guaranteed.")):
    text(img, (W / 2, 534 + i * 17), line, 13, REGULAR, DIM, "ma", 2)
img.convert("RGB").save(f"{root}/preview.png", optimize=True)

# ------------------------------------------------------------ multiplayer, step by step
# The first gallery image: deck codes in four steps.
W, H = 1280, 720
img = background(W, H, 31)
tag = sticker(780, 230, 74, angle=-4)
paste_center(img, tag, W / 2, -14)
text(img, (W / 2, 236), "Send your Aug Warriors deck to a friend. They play against it at the Downside Den.", 26, BOLD, CREAM, "ma", 3)
steps = [
    (sprite("deck_box", 11), "Build your deck", ["Fill a deck box with", "10 to 20 cards"]),
    (code_slip("AW1:RWR=1,4,7;DDY=3/MC", 19), "Copy its deck code", ["One button in the deck box", "puts it on your clipboard"]),
    (bubble("Beat this, I dare you", "AW1:RWR=1,1,4,7...", 22), "Send it to a friend", ["Discord, Steam chat,", "anywhere text goes"]),
    (fan(THEIRS, 2, 12), "They play it", ["The AI plays your deck the way", "you built it. Free practice"]),
]
cw = W // len(steps)
top, box = 296, 220
for i, (icon, title, body) in enumerate(steps):
    cx = cw * i + cw // 2
    paste_center(img, icon, cx, top + (box - icon.height) / 2)
    text(img, (cx, top + box + 22), f"{i + 1}. {title}", 28, BOLD, GOLD, "ma", 3)
    for j, line in enumerate(body):
        text(img, (cx, top + box + 68 + j * 28), line, 21, REGULAR, CREAM, "ma", 2)
    if i < len(steps) - 1:
        img.alpha_composite(arrow(44, 34), (cw * (i + 1) - 22, top + box // 2 - 17))
text(img, (W / 2, 672), "Paste a friend's code at the Den any night: no ante, and it doesn't use up your game", 20, BOLD, DIM, "ma", 2)
img.convert("RGB").save(f"{out_dir}/gallery_multiplayer.png", optimize=True)

# ------------------------------------------------------------ description banner
# Wide and short for the Workshop description, which shows images about 630 px wide.
W, H = 1200, 300
img = background(W, H, 41)
left, right = fan(YOURS, 2, 12), fan(THEIRS, 2, 12, flip=True)
img.alpha_composite(left, (20, H - left.height + 60))
img.alpha_composite(right, (W - right.width - 20, H - right.height + 60))
tag = sticker(660, 220, 64, angle=-4, sub="Trade deck codes with your friends")
paste_center(img, tag, W / 2, (H - tag.height) / 2)
img.convert("RGB").save(f"{out_dir}/banner_multiplayer.png", optimize=True)

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

for f in (f"{root}/preview.png", f"{out_dir}/gallery_sets.png", f"{out_dir}/gallery_features.png",
          f"{out_dir}/gallery_multiplayer.png", f"{out_dir}/banner_multiplayer.png"):
    print(f, os.path.getsize(f) // 1024, "KB")
