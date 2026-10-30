# Cheesy Zamco Electronics logo: chunky italic gradient letters, starburst, ribbon. Pixel art,
# upscaled 2x nearest so it stays crisp when the paper book shows it at 2x.
import sys
from PIL import Image, ImageDraw

out = sys.argv[1]
W, H = 168, 64
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
px = img.load()

INK = (38, 30, 52, 255)
SHADOW = (38, 30, 52, 140)
TEAL, TEAL_D = (64, 196, 196, 255), (34, 120, 138, 255)
WHITE = (255, 250, 230, 255)
GRAD = [(255, 236, 110), (255, 210, 70), (255, 168, 48), (250, 120, 40), (232, 72, 52), (200, 40, 70)]

# 5x7 glyphs, scaled 3x
GLYPHS = {
    "Z": ["11111", "00001", "00010", "00100", "01000", "10000", "11111"],
    "A": ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
    "M": ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
    "C": ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
    "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
}
SMALL = {  # 3x5 for the ribbon
    "E": ["111", "100", "110", "100", "111"], "L": ["100", "100", "100", "100", "111"],
    "C": ["011", "100", "100", "100", "011"], "T": ["111", "010", "010", "010", "010"],
    "R": ["110", "101", "110", "101", "101"], "O": ["010", "101", "101", "101", "010"],
    "N": ["110", "101", "101", "101", "101"], "I": ["111", "010", "010", "010", "111"],
    "S": ["011", "100", "010", "001", "110"],
}

# --- starburst behind the letters
cx, cy = 84, 27
d = ImageDraw.Draw(img)
import math
pts = []
for i in range(32):
    r = 30 if i % 2 == 0 else 22
    a = math.pi * 2 * i / 32
    pts.append((cx + r * 2.3 * math.cos(a), cy + r * math.sin(a)))
d.polygon(pts, fill=TEAL_D)
pts2 = [(cx + (x - cx) * 0.9, cy + (y - cy) * 0.86) for x, y in pts]
d.polygon(pts2, fill=TEAL)

# --- letters into a mask, sheared to italic
S = 3
mask = Image.new("L", (W, H), 0)
mp = mask.load()
x0, y0 = 30, 14
for gi, ch in enumerate("ZAMCO"):
    gx = x0 + gi * (5 * S + 6)
    for r, row in enumerate(GLYPHS[ch]):
        for c, v in enumerate(row):
            if v == "1":
                for yy in range(S):
                    for xx in range(S):
                        mp[gx + c * S + xx, y0 + r * S + yy] = 255
# thicken strokes by 1 px to the right for chunkiness
thick = mask.copy(); tp = thick.load()
for y in range(H):
    for x in range(1, W):
        if mp[x - 1, y]: tp[x, y] = 255
# colour the upright letters (gradient, top-edge shine), then shear mask and colour together
top, bottom = y0, y0 + 21
color = {}
for y in range(H):
    for x in range(W):
        if tp[x, y]:
            t = min(len(GRAD) - 1, max(0, (y - top) * len(GRAD) // (bottom - top)))
            color[(x, y)] = WHITE if (y == 0 or not tp[x, y - 1]) else GRAD[t] + (255,)
sheared = Image.new("L", (W, H), 0); sp = sheared.load()
scol = {}
for y in range(H):
    shift = (y0 + 21 - y) // 4
    for x in range(W):
        if tp[x, y] and 0 <= x + shift < W:
            sp[x + shift, y] = 255
            scol[(x + shift, y)] = color[(x, y)]

def inside(m, x, y): return 0 <= x < W and 0 <= y < H and m[x, y]

# drop shadow (+3,+3), outline, gradient fill, highlight
for y in range(H):
    for x in range(W):
        if inside(sp, x - 3, y - 3) and not inside(sp, x, y): px[x, y] = SHADOW
for y in range(H):
    for x in range(W):
        if not inside(sp, x, y) and any(inside(sp, x + dx, y + dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1)):
            px[x, y] = INK
for (x, y), c in scol.items():
    px[x, y] = c

# --- ribbon with ELECTRONICS
rw, rh = 76, 11
rx, ry = cx - rw // 2, 42
d.rectangle([rx - 4, ry + 3, rx, ry + rh + 2], fill=(150, 30, 60, 255))      # tails
d.rectangle([rx + rw, ry + 3, rx + rw + 4, ry + rh + 2], fill=(150, 30, 60, 255))
d.rectangle([rx, ry, rx + rw, ry + rh], fill=(214, 48, 78, 255), outline=INK)
d.line([rx + 1, ry + 1, rx + rw - 1, ry + 1], fill=(255, 120, 130, 255))
text = "ELECTRONICS"
tx = cx - (len(text) * 4 - 1) // 2
for i, ch in enumerate(text):
    for r, row in enumerate(SMALL[ch]):
        for c, v in enumerate(row):
            if v == "1": px[tx + i * 4 + c, ry + 3 + r] = WHITE

# --- sparkles and a registered mark
def sparkle(x, y, s):
    for k in range(-s, s + 1):
        px[x + k, y] = WHITE; px[x, y + k] = WHITE
    px[x, y] = (255, 255, 255, 255)
sparkle(24, 10, 3); sparkle(146, 40, 2); sparkle(140, 8, 2); sparkle(18, 40, 1)
R = ["0111110", "1000001", "1011001", "1010101", "1011001", "1010101", "0111110"]   # (R)
for r, row in enumerate(R):
    for c, v in enumerate(row):
        if v == "1": px[132 + c, 10 + r] = INK

img = img.resize((W * 2, H * 2), Image.NEAREST)
img.save(out)
print(out, img.size)
