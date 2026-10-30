# Booster pack icons for the card sets, on the shared 16 x 32 Zamco pack shape (crimped ends,
# a shaded foil body, a highlight strip and an emblem window), themed per set:
#   booster_pack_rustwave  rusted orange metal, green patina, a rising sun over a wave
#   booster_pack_doomed    sickly purple with toxic drips, a radiation emblem on hazard yellow
#   booster_pack_oxygen    pale icy blue with rising bubbles, a bubble emblem
#   booster_pack_grace     white and gold with gold flecks, a halo emblem
#   booster_pack_brood     olive chitin with egg spots, an egg cluster emblem
#   booster_pack_neural    magenta with cyan circuit traces, a circuit emblem
#   booster_pack_fates     near-black with crimson cracks, an hourglass emblem
#   booster_pack_ascension pale violet sky with stars, a winged emblem
#   booster_pack_singularity  deep indigo with an accretion glow, a black hole emblem
#   booster_pack_recursion terminal green on black with code streaks, a loop emblem
# Usage: python make-pack-art.py <repo root>
import sys
from PIL import Image

root = sys.argv[1]

# A outline, B cream, C/D crimp, E highlight, F body, G shade, H dark, I emblem shadow
TEMPLATE = [
    "................",
    "..AAAAAAAAAAAA..",
    "..ABCBCBCBCBCA..",
    "..ACDCDCDCDCDA..",
    "..AAAAAAAAAAAA..",
    "..AEEFFFFFGGHA..",
    "..AEBFFFFFGGHA..",
    "..AEBFFFFFGGHA..",
    "..AEAAAAAAAAHA..",
    "..AEABBBBBBAHA..",
    "..AEABBBBBBAHA..",
    "..AEABBBBBBAHA..",
    "..AEABBBBBBAHA..",
    "..AEAIIIIIIAHA..",
    "..AEAAAAAAAAHA..",
    "..AEFFFFFFGGHA..",
    "..AEBFFFFFGGHA..",
    "..AEBFFFFFGGHA..",
    "..AHHHHHHHHHHA..",
    "..AEBFFFFFGGHA..",
    "..AEFFFFFFGGHA..",
    "..AEFFFFFFGGHA..",
    "..AFFFFFFGGHHA..",
    "..AFFFFFGGGHHA..",
    "..AFFGGGGGHHHA..",
    "..AGGGGGGHHHHA..",
    "..AAAAAAAAAAAA..",
    "..ABCBCBCBCBCA..",
    "..ACDCDCDCDCDA..",
    "..AAAAAAAAAAAA..",
    "................",
    "................",
]
SHARED = {"A": (41, 34, 52), "C": (128, 146, 155), "D": (95, 104, 104), "B": (213, 210, 184), "I": (170, 169, 130)}

THEMES = {
    "rustwave": {
        "body": {"E": (232, 140, 72), "F": (168, 84, 44), "G": (130, 62, 38), "H": (88, 44, 34)},
        "emblem_bg": (226, 214, 180),
        # rising sun over a wave
        "emblem": ["..oo..", ".oooo.", "~w~w~w", "w~w~w~"],
        "emblem_colors": {"o": (232, 120, 52), "~": (64, 150, 150), "w": (34, 104, 116)},
        # verdigris patina and rust pits
        "dots": {(5, 6): (84, 150, 130), (9, 5): (84, 150, 130), (10, 16): (84, 150, 130), (6, 21): (84, 150, 130),
                 (8, 23): (110, 170, 150), (11, 20): (84, 150, 130), (7, 16): (100, 46, 30), (9, 22): (100, 46, 30),
                 (4, 24): (84, 150, 130)},
    },
    "doomed": {
        "body": {"E": (150, 100, 170), "F": (92, 58, 110), "G": (70, 44, 86), "H": (48, 32, 60)},
        "emblem_bg": (214, 196, 72),
        # radiation trefoil
        "emblem": ["##..##", "#.##.#", "..##..", ".#..#."],
        "emblem_colors": {"#": (41, 34, 52)},
        # toxic drips running down from the top
        "drips": [(5, 5, 4), (8, 15, 6), (10, 5, 3), (6, 19, 3), (11, 15, 2)],
        "drip": (150, 200, 70), "drip_tip": (200, 236, 110),
    },
    "oxygen": {
        "body": {"E": (220, 244, 252), "F": (120, 180, 210), "G": (92, 150, 190), "H": (62, 112, 150)},
        "emblem_bg": (240, 250, 255),
        # a big bubble, small ones rising beside it
        "emblem": [".##..#", "#..#..", "#..#.#", ".##..."],
        "emblem_colors": {"#": (40, 90, 140)},
        # bubbles
        "dots": {(6, 6): (235, 250, 255), (10, 5): (200, 232, 245), (8, 7): (235, 250, 255), (5, 16): (200, 232, 245),
                 (9, 17): (235, 250, 255), (7, 20): (235, 250, 255), (11, 21): (200, 232, 245), (5, 23): (235, 250, 255),
                 (9, 24): (200, 232, 245)},
    },
    "grace": {
        "body": {"E": (255, 252, 240), "F": (232, 226, 210), "G": (206, 196, 168), "H": (164, 150, 118)},
        "emblem_bg": (200, 226, 242),
        # a halo
        "emblem": [".####.", "#....#", ".####.", "......"],
        "emblem_colors": {"#": (226, 180, 60)},
        "dots": {(6, 6): (232, 190, 80), (10, 5): (232, 190, 80), (9, 16): (232, 190, 80), (5, 20): (232, 190, 80),
                 (11, 22): (232, 190, 80), (7, 24): (232, 190, 80)},
    },
    "brood": {
        "body": {"E": (150, 170, 90), "F": (96, 110, 60), "G": (74, 86, 46), "H": (52, 60, 34)},
        "emblem_bg": (64, 40, 52),
        # a cluster of eggs
        "emblem": [".#..#.", "##.###", "##.###", ".#..#."],
        "emblem_colors": {"#": (232, 222, 172)},
        "dots": {(6, 6): (232, 222, 172), (10, 6): (232, 222, 172), (5, 17): (232, 222, 172), (9, 16): (170, 210, 90),
                 (11, 20): (232, 222, 172), (7, 21): (232, 222, 172), (6, 24): (170, 210, 90), (10, 24): (232, 222, 172)},
    },
    "neural": {
        "body": {"E": (240, 120, 220), "F": (170, 60, 160), "G": (130, 40, 130), "H": (90, 30, 100)},
        "emblem_bg": (30, 30, 52),
        # circuit
        "emblem": ["#.##.#", "######", "#.##.#", ".#..#."],
        "emblem_colors": {"#": (80, 220, 230)},
        "drips": [(6, 15, 4), (9, 19, 5), (11, 5, 3), (7, 5, 2)],
        "drip": (80, 220, 230), "drip_tip": (200, 250, 255),
    },
    "fates": {
        "body": {"E": (180, 44, 56), "F": (52, 32, 42), "G": (40, 26, 34), "H": (28, 18, 26)},
        "emblem_bg": (214, 204, 180),
        # an hourglass
        "emblem": ["######", ".#..#.", "..##..", "######"],
        "emblem_colors": {"#": (170, 30, 44)},
        "drips": [(7, 5, 3), (10, 16, 4), (5, 19, 3), (8, 21, 4)],
        "drip": (150, 30, 42), "drip_tip": (210, 60, 70),
    },
    "ascension": {
        "body": {"E": (236, 228, 255), "F": (186, 170, 232), "G": (150, 134, 204), "H": (110, 96, 166)},
        "emblem_bg": (250, 244, 214),
        # wings
        "emblem": ["#....#", "##..##", "######", "..##.."],
        "emblem_colors": {"#": (214, 168, 60)},
        "dots": {(6, 6): (255, 250, 220), (10, 5): (255, 250, 220), (8, 16): (255, 250, 220), (11, 20): (255, 250, 220),
                 (5, 22): (255, 250, 220), (9, 24): (255, 250, 220)},
    },
    "singularity": {
        "body": {"E": (110, 90, 200), "F": (44, 34, 90), "G": (32, 24, 68), "H": (20, 16, 44)},
        "emblem_bg": (250, 170, 70),
        # a black hole: a dark core inside a bright ring
        "emblem": [".####.", "##..##", "##..##", ".####."],
        "emblem_colors": {"#": (20, 16, 44)},
        "dots": {(6, 6): (250, 170, 70), (10, 6): (200, 120, 220), (5, 17): (250, 170, 70), (10, 17): (200, 120, 220),
                 (7, 21): (250, 170, 70), (11, 22): (200, 120, 220), (8, 24): (250, 170, 70)},
    },
    "recursion": {
        "body": {"E": (70, 150, 90), "F": (24, 44, 32), "G": (18, 34, 24), "H": (12, 24, 18)},
        "emblem_bg": (18, 30, 22),
        # a loop arrow
        "emblem": [".###..", "#...#.", "#..###", ".##.#."],
        "emblem_colors": {"#": (90, 240, 120)},
        "drips": [(5, 15, 5), (8, 5, 3), (10, 17, 6), (7, 20, 4)],
        "drip": (60, 190, 90), "drip_tip": (160, 255, 170),
    },
}

for name, t in THEMES.items():
    img = Image.new("RGBA", (16, 32), (0, 0, 0, 0))
    p = img.load()
    colors = dict(SHARED, **t["body"])
    for y, row in enumerate(TEMPLATE):
        for x, k in enumerate(row):
            if k != ".":
                p[x, y] = colors[k] + (255,)
    # emblem window: inner 6 x 4 at (5, 9)
    for y in range(4):
        for x in range(6):
            k = t["emblem"][y][x]
            c = t["emblem_colors"].get(k, t["emblem_bg"])
            p[5 + x, 9 + y] = c + (255,)

    def body(x, y):   # only paint over the foil, not the outline, strip or emblem
        return TEMPLATE[y][x] in "FGH"

    for (x, y), c in t.get("dots", {}).items():
        if body(x, y): p[x, y] = c + (255,)
    for (x0, y0, length) in t.get("drips", []):
        for i in range(length):
            if body(x0, y0 + i): p[x0, y0 + i] = (t["drip_tip"] if i == length - 1 else t["drip"]) + (255,)
    for (cx, cy) in t.get("rings", []):
        for dx, dy in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
            if body(cx + dx, cy + dy): p[cx + dx, cy + dy] = (235, 250, 255, 255)
    img.save(f"{root}/Textures/Items/booster_pack_{name}.png")
print("ok")
