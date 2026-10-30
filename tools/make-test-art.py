# Test-pattern placeholder art for the first card (Scrap-Iron Chassis), to check how card art crops:
#   - the card face (viewer) shows the centre 63 x 46: inside the green line
#   - the board's mini cards show the centre 52 x 44, halved: inside the yellow line
#   - red stripes: never shown on the face
# Same 63 x 88 canvas as art_placeholder.png. Usage: python make-test-art.py <repo root>
import sys
from PIL import Image

root = sys.argv[1]
W, H = 63, 88
FACE_W, FACE_H = 63, 46
MINI_W, MINI_H = 52, 44

img = Image.new("RGBA", (W, H))
p = img.load()

# sky gradient, then a scrapyard floor
for y in range(H):
    for x in range(W):
        t = y / H
        p[x, y] = (int(40 + 60 * t), int(50 + 40 * t), int(80 + 30 * t), 255)
for y in range(58, H):
    for x in range(W):
        p[x, y] = (88, 72, 60, 255) if (x + y) % 5 else (70, 56, 48, 255)

# a chunky robot arm: shoulder ball, upper arm, elbow, forearm, claw
STEEL, STEEL_D, RUST, INK = (170, 176, 186, 255), (110, 116, 128, 255), (176, 96, 52, 255), (30, 26, 36, 255)


def disc(cx, cy, r, c, rim):
    for y in range(cy - r - 1, cy + r + 2):
        for x in range(cx - r - 1, cx + r + 2):
            d = (x - cx) ** 2 + (y - cy) ** 2
            if d <= r * r: p[x, y] = c
            elif d <= (r + 1) ** 2: p[x, y] = rim


def bar(x0, y0, x1, y1, thick, c):
    n = max(abs(x1 - x0), abs(y1 - y0))
    for i in range(n + 1):
        x = round(x0 + (x1 - x0) * i / n); y = round(y0 + (y1 - y0) * i / n)
        for dy in range(-thick, thick + 1):
            for dx in range(-thick, thick + 1):
                p[x + dx, y + dy] = c


bar(14, 36, 30, 46, 4, INK); bar(14, 36, 30, 46, 3, STEEL)       # upper arm
bar(30, 46, 46, 34, 3, INK); bar(30, 46, 46, 34, 2, STEEL_D)     # forearm
disc(14, 36, 6, RUST, INK)                                       # shoulder
disc(30, 46, 4, STEEL, INK)                                      # elbow
for (x, y) in [(47, 31), (48, 30), (49, 29), (50, 29), (48, 37), (49, 38), (50, 38), (51, 37)]:
    p[x, y] = INK                                                # claw
for (x, y) in [(46, 33), (47, 32), (46, 35), (47, 36)]:
    p[x, y] = STEEL
p[13, 34] = (255, 220, 160, 255); p[29, 44] = (255, 255, 255, 255)   # glints

# crop guides
fx, fy = (W - FACE_W) // 2, (H - FACE_H) // 2
mx, my = (W - MINI_W) // 2, (H - MINI_H) // 2
for y in range(H):
    for x in range(W):
        if not (fy <= y < fy + FACE_H) and (x + y) % 4 < 2:
            r, g, b, a = p[x, y]
            p[x, y] = (min(255, r + 110), g // 2, b // 2, 255)    # red stripes: cut from the face


def outline(x0, y0, w, h, c):
    for x in range(x0, x0 + w):
        p[x, y0] = c; p[x, y0 + h - 1] = c
    for y in range(y0, y0 + h):
        p[x0, y] = c; p[x0 + w - 1, y] = c


outline(fx, fy, FACE_W, FACE_H, (80, 230, 120, 255))     # card face
outline(mx, my, MINI_W, MINI_H, (255, 220, 60, 255))     # mini card
# centre cross
cx, cy = W // 2, H // 2
for d in range(-2, 3):
    p[cx + d, cy] = (255, 255, 255, 255); p[cx, cy + d] = (255, 255, 255, 255)

img.save(f"{root}/Textures/Items/art_rustwave_01.png")
print("ok", img.size)
