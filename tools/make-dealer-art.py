# Pixel art for the card dealer content, in the style of the vanilla sprites:
#   Textures/Items/dealer_certificate.png  48x32, a framed Zamco Authorized Dealer certificate
#   Textures/Items/scenario_card_dealer.png 19x19, the starting scenario icon (dark ring, drawn
#                                           for the game's teal disc like the vanilla icons)
# Usage: python make-dealer-art.py <repo root>
import sys
from PIL import Image

root = sys.argv[1]

INK = (41, 34, 52, 255)
PAPER = (213, 210, 184, 255)
TEXT = (55, 53, 65, 255)
TEXT2 = (95, 104, 104, 255)
FRAME = (150, 30, 60, 255)
FRAME_HI = (214, 48, 78, 255)
TEAL = (64, 196, 196, 255)
TEAL_D = (34, 120, 138, 255)
GOLD = (255, 210, 70, 255)
GOLD_D = (232, 150, 48, 255)


def rect(px, x0, y0, x1, y1, c):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            px[x, y] = c


# ---- certificate
cert = Image.new("RGBA", (48, 32), (0, 0, 0, 0))
p = cert.load()
rect(p, 4, 1, 43, 30, INK)            # outline
rect(p, 5, 2, 42, 29, FRAME)          # frame
rect(p, 5, 2, 42, 2, FRAME_HI)        # frame highlight, top
rect(p, 5, 27, 42, 27, FRAME_HI)      # and bottom edge of the paper
rect(p, 7, 5, 40, 26, PAPER)          # paper
# heading: "ZAMCO" squiggle in red, then a line of small print
for x in range(15, 33):
    p[x, 7] = FRAME if x % 3 else FRAME_HI
    p[x, 8] = FRAME_HI if x % 2 else FRAME
# starburst emblem with a Z
burst = ["..#.#..", ".#####.", "##ooo##", ".#ooo#.", "##ooo##", ".#####.", "..#.#.."]
for r, row in enumerate(burst):
    for c, v in enumerate(row):
        if v == "#": p[20 + c, 10 + r] = TEAL_D if (r + c) % 2 else TEAL
        if v == "o": p[20 + c, 10 + r] = GOLD
for (x, y) in [(22, 12), (23, 12), (24, 12), (23, 13), (22, 14), (23, 14), (24, 14)]:
    p[x, y] = FRAME   # the Z
# "AUTHORIZED DEALER" and small print lines
for x in range(13, 36):
    p[x, 18] = TEXT if x % 4 else TEXT2
for x in range(11, 24):
    p[x, 20] = TEXT
for x in range(25, 37):
    p[x, 20] = TEXT
# gold seal with ribbon, bottom left, and a signature, bottom right
seal = [".###.", "#ooo#", "#ooo#", ".###."]
for r, row in enumerate(seal):
    for c, v in enumerate(row):
        if v == "#": p[13 + c, 22 + r] = GOLD_D
        if v == "o": p[13 + c, 22 + r] = GOLD
p[14, 26] = FRAME_HI; p[16, 26] = FRAME_HI
for x, y in [(27, 24), (28, 23), (29, 24), (30, 23), (31, 24), (32, 24), (33, 23), (34, 24)]:
    p[x, y] = (39, 52, 86, 255)   # signature in blue ink
cert.save(f"{root}/Textures/Items/dealer_certificate.png")

# ---- scenario icon: the vanilla ring, with two fanned cards inside
icon = Image.new("RGBA", (19, 19), (0, 0, 0, 0))
q = icon.load()
ring = [
    ".......#####.......", ".....##.....##.....", "...##.........##...", "..##...........##..",
    "..#.............#..", ".#...............#.", ".#...............#.", "#.................#",
    "#.................#", "#.................#", "#.................#", "#.................#",
    ".#...............#.", ".#...............#.", "..#.............#..", "..##...........##..",
    "...##.........##...", ".....##.....##.....", ".......#####.......",
]
cards = [
    "...................", "...................", "...................", "...................",
    "........######.....", "......#######.#....", "......#....##.#....", "......#.##.##.#....",
    "......#.#..##.#....", "......#.##.##.#....", "......#....##.#....", "......#.##.##......",
    "......#....#.......", "......######.......", "...................", "...................",
    "...................", "...................", "...................",
]
for y in range(19):
    for x in range(19):
        if ring[y][x] == "#" or cards[y][x] == "#":
            q[x, y] = INK
icon.save(f"{root}/Textures/Items/scenario_card_dealer.png")
print("ok")
