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

# CardCondition.Labels: raw (ungraded) price multiplier by condition
RAW = [(9.5, 3.8), (8.5, 2.5), (7.0, 1.6), (5.0, 1.15), (3.0, 0.8), (0.0, 0.5)]


def raw_mult(c):
    return next(m for lo, m in RAW if c >= lo)


# CardCondition.GradedMultiplier: graded cards sell on the exact grade
GRADED = [(10, 16.0), (9.5, 7.0), (9, 4.5), (8.5, 3.2), (8, 2.4), (7, 1.85)]


def graded_mult(g):
    return next((m for lo, m in GRADED if g >= lo), None) or raw_mult(g) * 0.95


PREMIUM = [(10, 100), (9.5, 20), (9, 6)]   # CardCondition.GradePremium
if len(sys.argv) > 2:   # alternative premiums to try: "10:25,9.5:8,9:3,8:0"
    PREMIUM = [tuple(float(x) for x in p.split(":")) for p in sys.argv[2].split(",")]


def premium(g):
    return next((p for lo, p in PREMIUM if g >= lo), 0)


GEM_CHANCE = 0.005   # CardCondition.GEM_CHANCE: a perfect 10 is its own roll
FRESH_MEAN, FRESH_SD = 5.9, 1.75   # CardCondition.RollFreshCondition: a station's worth of wear


def condition():
    if random.random() < GEM_CHANCE:
        return 10.0
    return round(min(9.94, max(1, random.gauss(FRESH_MEAN, FRESH_SD))), 2)


def base(card, s):
    return max(1, round(rarity_value[card["rarity"]] * s.get("priceMultiplier", 1)))


def card_from(s, slot):
    rarity = random.choices(slot["rarities"], slot["weights"])[0]
    pool = [c for c in s["cards"] if c["rarity"] == rarity] or s["cards"]
    return random.choice(pool)


conds = [condition() for _ in range(N)]
labels = ["Mint 9.5+", "Near Mint 8.5+", "Lightly Played 7+", "Moderately Played 5+", "Heavily Played 3+", "Damaged"]
print("Fresh cards by condition:")
hi = 99
for (lo, m), name in zip(RAW, labels):
    share = sum(1 for c in conds if lo <= c < hi) / N
    hi = lo
    print(f"  {name:<22}{share:>7.1%}   raw x{m}")
print(f"  average raw multiplier {sum(raw_mult(c) for c in conds) / N:.3f}\n")

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

print(f"\nGrading (fee {GRADING_FEE}): average gain from grading a Tier 1 card, by how it looks")
bands = [("blind", 0, 11), ("looks 7+", 7, 11), ("looks 8.5+", 8.5, 11), ("looks 9.5+", 9.5, 11)]
s = data["sets"][0]
for rarity in ("common", "uncommon", "rare"):
    b = base({"rarity": rarity}, s)
    row = []
    for name, lo, hi in bands:
        gains = []
        while len(gains) < 20_000:
            c = condition()
            if lo <= c < hi:
                g = round(c, 1)
                gains.append(b * graded_mult(g) + premium(g) - b * raw_mult(c) - GRADING_FEE)
        row.append(f"{name} {sum(gains) / len(gains):+7.1f}")
    print(f"  {rarity:<9} base {b:>3}:  " + "   ".join(row))
print("\nGem 10 values (graded): " + ", ".join(
    f"{r} {round(rarity_value[r] * graded_mult(10)) + premium(10)}" for r in ("common", "uncommon", "rare", "ultra")) + "  (x set price multiplier on the card part)")
