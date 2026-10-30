# Economy check: what packs, boxes and grading are worth on average, from Data/sets.json and the
# condition/grade rules in CardCondition.cs (mirrored here; keep in sync).
# Usage: python economy.py <repo root>
import json, random, sys

root = sys.argv[1]
data = json.load(open(f"{root}/Data/sets.json", encoding="utf-8"))
rarity_value = {r["id"]: r["value"] for r in data["rarities"]}
DEN_MARKUP = 1.25
GRADING_FEE = 12
N = 200_000

RAW = [(9.5, 1.5), (8.5, 1.25), (7.0, 1.0), (5.0, 0.75), (3.0, 0.5), (0.0, 0.25)]


def raw_mult(c):
    return next(m for lo, m in RAW if c >= lo)


def graded_mult(g):
    if g >= 10: return 6.0
    if g >= 9.5: return 3.5
    if g >= 9: return 2.25
    if g >= 8: return 1.5
    if g >= 7: return 1.1
    return raw_mult(g) * 0.9


PREMIUM = [(10, 60), (9.5, 10), (9, 3)]
if len(sys.argv) > 2:   # alternative premiums to try: "10:25,9.5:8,9:3,8:0"
    PREMIUM = [tuple(float(x) for x in p.split(":")) for p in sys.argv[2].split(",")]


def premium(g):
    return next((p for lo, p in PREMIUM if g >= lo), 0)


GEM_CHANCE = 0.005   # CardCondition.GEM_CHANCE: a perfect 10 is its own roll


def condition():
    if random.random() < GEM_CHANCE:
        return 10.0
    c = 9.94 - abs(random.gauss(0, 1)) * 1.2
    return round(min(9.94, max(1, c)), 2)


def base(card, s):
    return max(1, round(rarity_value[card["rarity"]] * s.get("priceMultiplier", 1)))


def card_from(s, slot):
    rarity = random.choices(slot["rarities"], slot["weights"])[0]
    pool = [c for c in s["cards"] if c["rarity"] == rarity] or s["cards"]
    return random.choice(pool)


print(f"{'set':<18}{'pack':>6}{'Dan':>6}{'open EV':>9}{'EV/Dan':>8}{'best 5%':>9}{'box':>6}{'box EV':>8}{'EV/Dan':>8}")
for s in data["sets"]:
    totals = []
    for _ in range(N // 10):
        v = 0
        for slot in s["packSlots"]:
            for _ in range(slot["count"]):
                v += base(card_from(s, slot), s) * raw_mult(condition())
        totals.append(v)
    ev = sum(totals) / len(totals)
    top = sorted(totals)[int(len(totals) * 0.95)]
    pack, box = s["packValue"], s["boxValue"]
    box_ev = ev * s["boxPacks"]
    print(f"{s['name']:<18}{pack:>6}{pack * DEN_MARKUP:>6.0f}{ev:>9.1f}{ev / (pack * DEN_MARKUP):>8.0%}{top:>9.0f}"
          f"{box:>6}{box_ev:>8.0f}{box_ev / (box * DEN_MARKUP):>8.0%}")

print(f"\nGrading (fee {GRADING_FEE}): average gain from grading a card, blind / when it looks pristine (8.5+)")
for s in data["sets"][:1]:
    for rarity in ("common", "uncommon", "rare"):
        b = base({"rarity": rarity}, s)
        blind, pristine, n_pristine = 0, 0, 0
        for _ in range(N):
            c = condition()
            g = round(c, 1)
            gain = b * graded_mult(g) + premium(g) - b * raw_mult(c) - GRADING_FEE
            blind += gain
            if c >= 8.5: pristine += gain; n_pristine += 1
        print(f"  {rarity:<9} base {b:>3}: blind {blind / N:+6.1f}   pristine {pristine / n_pristine:+6.1f}")
