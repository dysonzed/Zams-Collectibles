# Inventory icon for a pack of card sleeves (Textures/Items/card_sleeves.png, 16 x 16):
# a stack of clear plastic sleeves in a teal header card, a card showing through.
# Usage: python make-sleeve-art.py <repo root>
import sys
from PIL import Image

root = sys.argv[1]
W, H = 16, 16
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
p = img.load()

INK = (41, 34, 52, 255)
PLASTIC, PLASTIC_HI, PLASTIC_LO = (196, 214, 222, 200), (240, 248, 252, 230), (152, 172, 184, 210)
TEAL, TEAL_HI, CARD = (46, 150, 150, 255), (110, 205, 200, 255), (176, 96, 52, 255)


def rect(x0, y0, x1, y1, c):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            p[x, y] = c


# the stack: back sleeves peeking out above and to the right
rect(4, 1, 13, 13, INK); rect(5, 2, 12, 12, PLASTIC_LO)
rect(3, 2, 12, 14, INK); rect(4, 3, 11, 13, PLASTIC)
# the card inside the front sleeve
rect(5, 6, 10, 12, CARD); rect(6, 7, 9, 9, (230, 200, 150, 255))
# glare down the plastic
for i in range(4):
    p[4 + i, 9 - i] = PLASTIC_HI
# the header card across the top
rect(2, 2, 13, 5, INK); rect(3, 3, 12, 4, TEAL)
for x in range(3, 13):
    p[x, 3] = TEAL_HI
p[7, 4] = p[8, 4] = (255, 244, 238, 255)   # a tiny label

img.save(f"{root}/Textures/Items/card_sleeves.png")
print("ok", img.size)
