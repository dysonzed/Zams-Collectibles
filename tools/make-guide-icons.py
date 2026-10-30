# Renders the mini card symbols for the Zamco guide's "Reading a Card" page, straight from the
# bitmaps in AugWarriors/MiniCards.cs, so the guide always matches the board. Each icon is drawn
# at 4x (paper books show art at 2x of their 2x design units) into Textures/Paper/guide_icon_*.png.
# Usage: python make-guide-icons.py <repo root>
import re
import sys
from PIL import Image

root = sys.argv[1]
src = open(f"{root}/AugWarriors/MiniCards.cs", encoding="utf-8").read()


def bitmaps(block_name):
    block = src[src.index(block_name):]
    block = block[:block.index("};")]
    found = {}
    for m in re.finditer(r'\[(?:"(\w+)"|\'(.)\')\] = new\[\] \{(.*?)\}', block, re.S):
        found[m.group(1) or m.group(2)] = re.findall(r'"([.#]+)"', m.group(3))
    return found


icons = bitmaps("Icons = new")
glyphs = bitmaps("Glyphs = new")

INK = (41, 34, 52, 255)
RED = (192, 64, 74, 255)
TEAL = (69, 145, 139, 255)
CREAM = (232, 228, 214, 255)
SCALE = 4


def draw(canvas, rows, x0, y0, color):
    p = canvas.load()
    for y, row in enumerate(rows):
        for x, v in enumerate(row):
            if v == "#":
                p[x0 + x, y0 + y] = color


def save(name, size, paint):
    img = Image.new("RGBA", size, (0, 0, 0, 0))
    paint(img)
    img.resize((size[0] * SCALE, size[1] * SCALE), Image.NEAREST).save(f"{root}/Textures/Paper/guide_icon_{name}.png")


# the card types, 11 x 11
for kind in ("arm", "torso", "head", "legs", "mod", "hack"):
    save(kind, (11, 11), lambda img, k=kind: draw(img, icons[k], 0, 0, INK))

# stats: the 5-wide icon centred in an 11 x 11 square, like the types
save("stability", (11, 11), lambda img: draw(img, icons["shield"], 3, 3, TEAL))
save("glitch", (11, 11), lambda img: draw(img, icons["bolt"], 3, 2, RED))
save("scrap", (11, 11), lambda img: draw(img, glyphs["X"], 4, 3, RED))


# the Power plate, as on the card's corner
def plate(img):
    p = img.load()
    for y in range(11):
        for x in range(11):
            edge = x in (1, 9) or y in (1, 9)
            if 1 <= x <= 9 and 1 <= y <= 9:
                p[x, y] = INK if edge else CREAM
    draw(img, glyphs["2"], 4, 3, INK)


save("power", (11, 11), plate)
print("ok")
