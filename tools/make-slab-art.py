# Inventory icons for graded cards: the card sealed in a clear plastic slab with a red grade label
# on top, one per card back (card_back_rustwave -> card_slab_rustwave, ...). 16 x 16, like the card icons.
# Usage: python make-slab-art.py <repo root>
import sys
from PIL import Image

root = sys.argv[1]
INK = (41, 34, 52, 255)
PLASTIC = (196, 214, 222, 255)
PLASTIC_HI = (240, 248, 252, 255)
GLASS = (200, 225, 235, 70)
LABEL = (214, 48, 78, 255)
LABEL_HI = (255, 236, 230, 255)

for colour in ("red", "blue", "rustwave", "doomed", "oxygen", "grace", "brood", "neural", "fates"):
    back = Image.open(f"{root}/Textures/Items/card_back_{colour}.png").convert("RGBA")
    card = back.crop(back.getbbox()).resize((8, 9), Image.NEAREST)
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    p = img.load()
    for y in range(0, 16):
        for x in range(2, 14):
            edge = x in (2, 13) or y in (0, 15)
            p[x, y] = INK if edge else GLASS
    for x in range(3, 13):                       # plastic rim inside the outline
        p[x, 1] = PLASTIC_HI; p[x, 14] = PLASTIC
    for y in range(1, 15):
        p[3, y] = PLASTIC_HI; p[12, y] = PLASTIC
    for y in range(2, 5):                        # grade label
        for x in range(4, 12):
            p[x, y] = LABEL
    for x in (5, 6, 7, 9, 10):
        p[x, 3] = LABEL_HI                       # label text specks
    img.alpha_composite(card, (4, 5))
    p[4, 5] = PLASTIC_HI                         # glint on the case
    img.save(f"{root}/Textures/Items/card_slab_{colour}.png")
print("ok")
