# Writes Data/sets.json: the card sets, their packs and prices. Edit here, then run: python make-sets.py <repo root>
import json, sys

root = sys.argv[1]
old = json.load(open(f"{root}/Data/sets.json", encoding="utf-8"))

rarities = old["rarities"]
values = {"common": 1, "uncommon": 4, "rare": 28, "ultra": 250}
for r in rarities:
    r["value"] = values[r["id"]]


def aug(n, name, rarity, slot, power, glitch, stability=0, effect=None, amount=0, text="", flavor=""):
    c = {"number": n, "name": name, "rarity": rarity, "kind": "augment", "slot": slot, "power": power, "glitch": glitch}
    if stability: c["stability"] = stability
    if effect: c["effect"] = effect; c["amount"] = amount
    c["text"] = text; c["flavor"] = flavor
    return c


def mod(n, name, rarity, modFor, power, glitch, stability=0, effect=None, amount=0, text="", flavor=""):
    c = {"number": n, "name": name, "rarity": rarity, "kind": "mod", "modFor": modFor, "power": power, "glitch": glitch}
    if stability: c["stability"] = stability
    if effect: c["effect"] = effect; c["amount"] = amount
    c["text"] = text; c["flavor"] = flavor
    return c


def hack(n, name, rarity, effect, amount=0, text="", flavor=""):
    return {"number": n, "name": name, "rarity": rarity, "kind": "hack", "power": 0, "glitch": 0,
            "effect": effect, "amount": amount, "text": text, "flavor": flavor}


def slot(count, rarities, weights=None):
    return {"count": count, "rarities": rarities, "weights": weights or [1] * len(rarities)}


sets = [
    {
        "id": "rustwave", "name": "Rustwave Rising", "code": "RWR",
        "cardSprite": "card_back_rustwave", "packSprite": "booster_pack_rustwave", "frameColor": "red",
        "priceMultiplier": 1.0, "packValue": 34, "boxPacks": 18, "boxValue": 566, "sealedMarkup": 0.2,
        "packSlots": [slot(3, ["common"]), slot(1, ["uncommon"]), slot(1, ["rare", "uncommon"], [75, 25])],
        "cards": [
            aug(1, "Scrap-Iron Chassis", "common", "torso", 2, 0, 4, flavor="Heavy, ugly, and still standing."),
            aug(2, "Oxidized Piston", "common", "arm", 3, 1, flavor="Squeals on every swing."),
            mod(3, "Tetanus Spike", "common", "any", 2, 2, flavor="Get your shots first."),
            aug(4, "Rusty Greaves", "common", "legs", 2, 0, flavor="They creak. They hold."),
            aug(5, "Patchwork Visor", "common", "head", 1, 0, effect="draw", amount=1,
                text="When installed, draw a card.", flavor="Six lenses, none of them matching."),
            aug(6, "Ironclad Will", "uncommon", "head", 1, 0, effect="ironclad",
                text="Hacks can't lower your Stability.", flavor="Some minds rust shut."),
            hack(7, "Feedback Loop", "uncommon", "glitch_modded", 2,
                 text="+2 Glitch on the target, +3 if it has a mod.", flavor="Turn it up until it screams."),
            mod(8, "Overclocked Furnace", "uncommon", "torso", 3, 2, flavor="Runs hot. Runs hotter."),
            aug(9, "Magnetic Grapple", "uncommon", "arm", 2, 1, effect="power_per_mod", amount=1,
                text="+1 Power for each mod attached to it.", flavor="Everything sticks eventually."),
            mod(10, "Shunt Wire", "uncommon", "any", 0, -2, effect="clean_bonus", amount=1,
                text="+1 Power if the augment it's on has no Glitch.", flavor="Ground it. Then ground it again."),
            hack(11, "Corrosive Emitter", "rare", "sacrifice_glitch", 4,
                 text="Scrap your weakest mod to give the target +4 Glitch.", flavor="Eats through everything. Even yours."),
            aug(12, "The Red Menace", "rare", "torso", 3, 2, 5, effect="power_per_other_mod", amount=1,
                text="+1 Power for each mod on your other augments.", flavor="The whole rig answers to it."),
            mod(13, "Overdrive Coil", "rare", "any", 0, 2, effect="double_base",
                text="Doubles the Power printed on its augment.", flavor="Rated for twice the load. Briefly."),
        ],
    },
    {
        "id": "doomed", "name": "Doomed Decay", "code": "DDC",
        "cardSprite": "card_back_doomed", "packSprite": "booster_pack_doomed", "frameColor": "purple",
        "priceMultiplier": 1.1, "packValue": 39, "boxPacks": 20, "boxValue": 722, "sealedMarkup": 0.2,
        "packSlots": [slot(2, ["common"]), slot(2, ["uncommon", "common"], [80, 20]), slot(1, ["rare", "uncommon"], [75, 25])],
        "cards": [
            aug(1, "Lead-Lined Boots", "common", "legs", 1, 0, 2, flavor="Walk through the fallout. Slowly."),
            aug(2, "Rotting Framework", "common", "torso", 4, 2, 2, flavor="Strong today. Ask again tomorrow."),
            hack(3, "Decay Spores", "common", "stability_drain", 2,
                 text="The opponent loses 2 Stability.", flavor="It gets into the joints."),
            aug(4, "Brittle Blades", "common", "arm", 4, 2, flavor="Sharp enough. Once."),
            mod(5, "Siphon Tube", "common", "any", 0, -1, effect="siphon", amount=1,
                text="When attached, the opponent gains 1 Glitch.", flavor="Your problem now."),
            aug(6, "Toxic Filter", "uncommon", "head", 1, 0, effect="hack_backlash", amount=2,
                text="When the opponent hacks you, they gain 2 Glitch.", flavor="Breathe in. They choke."),
            mod(7, "Isotope Battery", "uncommon", "any", 2, 1, flavor="Glows a little. That's normal."),
            hack(8, "Radiation Leak", "uncommon", "strip_mods",
                 text="Remove all mods from the target.", flavor="Everything bolted on falls off."),
            aug(9, "Hazmat Visor", "uncommon", "head", 2, 1, effect="hack_shield", amount=1,
                text="Blocks the next hack played on you.", flavor="Rated for one bad day."),
            hack(10, "Malignant Code", "rare", "move_hack_glitch", 2,
                 text="+2 Glitch on the target, and move all hack Glitch from your augments onto it.",
                 flavor="Pass it on."),
            aug(11, "Half-Life Generator", "rare", "torso", 3, 1, 5, effect="punish_compromised", amount=4,
                text="+4 Power at the showdown if the opponent is Compromised.", flavor="Patient. Inevitable."),
            aug(12, "Fission Fist", "rare", "arm", 4, 3, effect="compromise_proof",
                text="Its Power is never halved when you're Compromised.", flavor="It hits just as hard on fire."),
            aug(13, "Plague Walker", "rare", "legs", 2, 1, effect="hack_amp", amount=1,
                text="Your hacks that add Glitch add 1 more.", flavor="Leaves a trail. Everyone gets sick."),
        ],
    },
    {
        "id": "oxygen", "name": "Oxygen Leak", "code": "OXL",
        "cardSprite": "card_back_oxygen", "packSprite": "booster_pack_oxygen", "frameColor": "teal",
        "priceMultiplier": 1.2, "packValue": 43, "boxPacks": 16, "boxValue": 636, "sealedMarkup": 0.2,
        "packSlots": [slot(4, ["common"]), slot(1, ["uncommon"]), slot(1, ["rare", "uncommon"], [80, 20])],
        "cards": [
            aug(1, "Emergency Tank", "common", "torso", 2, 0, 3, flavor="Break glass. Breathe."),
            mod(2, "Vacuum Seals", "common", "any", 1, 0, flavor="Nothing in, nothing out."),
            aug(3, "Pressurized Struts", "common", "legs", 1, 0, effect="late_bloomer", amount=1,
                text="+1 Power for each of your turns before it was installed.", flavor="Pressure builds."),
            aug(4, "Airtight Helmet", "common", "head", 2, 1, flavor="Fogs up when you panic."),
            aug(5, "Pneumatic Punch", "common", "arm", 2, 0, flavor="Clean, quiet, and very fast."),
            mod(6, "Thruster Pack", "common", "legs", 3, 1, effect="late_only", amount=5,
                text="Only from your 5th turn.", flavor="For the last push."),
            hack(7, "Suffocate", "uncommon", "suffocate",
                 text="The target gains no Power from mods.", flavor="Cut the air. Watch it stall."),
            mod(8, "Oxygen Recycler", "uncommon", "torso", 1, -2, flavor="Every breath, twice."),
            aug(9, "Zero-G Calibration", "uncommon", "head", 1, 0, effect="extra_torso_mod", amount=1,
                text="Your torso can hold 3 mods.", flavor="Room to float. Room to build."),
            hack(10, "Airlock Override", "rare", "void",
                 text="Target an augment with no mods: it counts 0 Power.", flavor="Cycling. Goodbye."),
            aug(11, "Cryo-Sleep Chamber", "rare", "torso", 0, 0, 5, effect="power_per_stable", amount=2,
                text="+2 Power for each of your other augments with no Glitch.", flavor="Cold, still, perfect."),
            aug(12, "Kinetic Harpoon", "rare", "arm", 2, 1, effect="steal_mod",
                text="When installed, steals the opponent's best mod onto this arm.", flavor="Reel it in."),
            hack(13, "Pressure Drop", "common", "glitch", 1,
                 text="+1 Glitch on the target.", flavor="Ears pop. Systems stutter."),
        ],
    },
]

json.dump({"rarities": rarities, "sets": sets}, open(f"{root}/Data/sets.json", "w", encoding="utf-8"), ensure_ascii=False, indent=2)
print("ok")
