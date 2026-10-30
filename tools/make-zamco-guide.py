# Generates Books/zamco_guide.json and its localization lines (en + zh) for Zam's Collectibles.
# Run once; it appends the strings to the csv files.
import json, sys
root = sys.argv[1]
# one list per page: (key, style, en, zh)
pages = [
 [("p1_title", "section", "A Message to Our Valued Stockists", "致尊贵的经销商"),
  ("p1_1", "body", "Since the first Zamco toaster rolled off the line, one promise has guided everything we do: more product, for more people, more often.",
   "自第一台 Zamco 烤面包机下线以来，我们始终坚守一个承诺：更多产品，给更多人，更频繁地。"),
  ("p1_2", "body", "Today Zamco proudly delivers joy to over forty habitation rings. Our family of products includes:",
   "如今，Zamco 自豪地为四十多个居住环带去欢乐。我们的产品家族包括："),
  ("p1_3", "body", "- Aug Warriors, the trading card game. The station's #1 card game!*", "- Aug Warriors 集换式卡牌游戏。空间站第一卡牌游戏！*"),
  ("p1_4", "body", "- CyberNocts, collectible buildable robot figures. Coming soon!", "- CyberNocts 可收藏拼装机器人模型。即将上市！"),
  ("p1_5", "body", "- Lil' Debtors, plush pals that grow with your outstanding balance.", "- 小欠债鬼：会随你的欠款一起长大的毛绒伙伴。"),
  ("p1_6", "body", "- Zamco SmartCrib. It watches the baby so you can work a second shift.", "- Zamco 智能婴儿床。它看孩子，你去上第二个班。"),
  ("p1_7", "body", "Thank you for stocking Zamco. Remember: you're not just selling toys. You're selling the feeling of almost having enough.",
   "感谢你经销 Zamco。请记住：你卖的不只是玩具，而是那种“差一点就够了”的感觉。"),
  ("p1_8", "serif_title", "Zamco Electronics. Better living through hardware.", "Zamco 电子。硬件让生活更美好。"),
  ("p1_9", "caption", "*According to Zamco. Zamco is not responsible for lost limbs, cards or credits. Product may contain product.",
   "*数据来自 Zamco。Zamco 不对丢失的肢体、卡牌或信用点负责。产品可能含有产品。")],
 [("p1b_title", "title", "Introducing: Aug Warriors", "隆重推出：Aug Warriors"),
  ("p1b_1", "body", "The card game the whole station is talking about. Build the ultimate mercenary one augment at a time, then outplay your rivals at the showdown.",
   "整个空间站都在谈论的卡牌游戏。一次装一个义体，打造终极佣兵，然后在对决中击败对手。"),
  ("p1b_2", "body", "Every booster pack is factory sealed at Zamco's orbital plant and holds a random assortment of cards: commons, uncommons and, if fortune smiles, something rare.** Collect them, trade them, grade them, play them.",
   "每个补充包都在 Zamco 轨道工厂密封出厂，内含随机卡牌：普通、非普通，运气好的话还有稀有卡。** 收集、交易、评级、对战，随你。"),
  ("p1b_3", "body", "Booster boxes, Collector's Boxes, binders and deck boxes are available from participating retailers. Collect the whole set!",
   "补充盒、收藏盒、卡册和卡组盒可在参与活动的零售商处购买。集齐全套！"),
  ("p1b_4", "caption", "**Rarity not guaranteed. Collecting the whole set not guaranteed.", "**不保证稀有度。不保证能集齐全套。")],
 [("p2_title", "title", "Where to Buy: the Downside Den", "购买地点：Downside Den"),
  ("p2_1", "body", "Zamco products are sold by fine retailers across the station, and by one that isn't so fine.",
   "Zamco 产品在空间站各处的正规商店有售，也在一家不太正规的店里有售。"),
  ("p2_2", "body", "Take the service lift down past the recycling decks. Step off where the lights stop working properly, follow the smell of fried noodles and the sound of shuffling, and look for the hand-painted sign: DOWNSIDE DEN.",
   "乘维修电梯下行，经过回收甲板。在灯光开始不正常的地方下来，循着炸面的味道和洗牌的声音走，找那块手绘招牌：DOWNSIDE DEN。"),
  ("p2_3", "body", "Ask for Dan. He stocks fresh booster packs and boxes every night, keeps singles in the case, and buys collectibles over the counter. The Den opens after hours: drop by once your own shop has closed for the night.",
   "找 Dan。他每晚进新的补充包和补充盒，柜子里有单卡，也在柜台收购收藏品。Den 只在打烊后营业：你的店关门后过来就行。"),
  ("p2_4", "body", "The back table belongs to the station's Aug Warriors regulars. Bring a deck.",
   "后面那张桌子是空间站 Aug Warriors 常客的地盘。带上卡组。")],
 [("p3_title", "title", "How to Play: the Goal", "玩法：目标"),
  ("p3_1", "body", "Two mercenaries race to build the strongest body. Each body has five slots: head, torso, two arms and legs.",
   "两名佣兵比拼谁能打造最强的身体。每具身体有五个槽位：头部、躯干、两条手臂和腿部。"),
  ("p3_2", "body", "Every augment you install adds Power, and most add Glitch too. Your Stability starts at 3; torsos and some mods raise it.",
   "你安装的每个义体都会增加力量，大多数也会增加故障。你的稳定度从 3 开始；躯干和部分改装会提高它。"),
  ("p3_3", "body", "At the showdown the higher Power wins. But if your total Glitch is over your Stability, you are Compromised and keep only half your Power.",
   "对决时力量高者获胜。但如果你的故障总值超过稳定度，你就会被入侵，力量减半。"),
  ("p3_4", "body", "The art of the game: push your Power as high as you dare without letting the Glitch get away from you.",
   "游戏的精髓：在不让故障失控的前提下，把力量推到你敢推的最高。")],
 [("p4_title", "title", "How to Play: Your Turn", "玩法：你的回合"),
  ("p4_1", "body", "Both players start with 4 cards, and a coin toss decides who goes first.", "双方各以 4 张手牌开始，掷硬币决定先手。"),
  ("p4_2", "body", "At the start of your turn, draw a card if you hold fewer than 4. Then do one thing:",
   "回合开始时，如果手牌少于 4 张就抽一张。然后做一件事："),
  ("p4_3", "body", "- Play a card: install an augment, attach a mod, or hack your opponent.",
   "- 打出一张牌：安装义体、加装改装，或骇入对手。"),
  ("p4_4", "body", "- Call the showdown. Your opponent gets one last turn, and draws 2 extra cards for it.",
   "- 宣布对决。对手获得最后一个回合，并为此额外抽 2 张牌。"),
  ("p4_5", "body", "If you have nothing you can play, you must call. On the last turn, if there's nothing to play, the turn simply passes.",
   "如果没有可打出的牌，就必须宣布对决。最后一个回合若无牌可打，回合直接结束。"),
  ("p4_6", "body", "The showdown also happens on its own once both players have taken 6 turns. A tie goes to whoever did not call; if nobody called, it's a draw.",
   "双方都进行完 6 个回合后，对决会自动发生。平局时未宣布对决的一方获胜；若无人宣布，则为平局。")],
 [("p5_title", "title", "How to Play: the Cards", "玩法：卡牌"),
  ("p5_1", "body", "AUGMENTS go into the matching slot of your own body; an arm augment fits either arm. Installing over an augment replaces it, and its mods are lost.",
   "义体装进你自己身体对应的槽位；手臂义体两条手臂都能装。在已有义体上安装会替换它，原有改装随之丢失。"),
  ("p5_2", "body", "MODS attach to one of your installed augments, up to 2 on each. Some only fit certain slots. A good mod soaks up Glitch or adds Stability.",
   "改装加装在你已安装的义体上，每个义体最多 2 个。有些只适用于特定槽位。好的改装能吸收故障或增加稳定度。"),
  ("p5_3", "body", "HACKS are played on your opponent's installed augments. Most add Glitch to the target, and it stacks. Scrap hacks remove the augment and its mods entirely.",
   "骇入卡打在对手已安装的义体上。大多数会给目标增加故障，且可叠加。报废类骇入会把义体连同改装一起移除。"),
  ("p5_4", "section", "Special effects", "特殊效果"),
  ("p5_5", "body", "Some augments do more: draw cards when installed, make you immune to hacks, gain Power for each mod on them, for each of your Glitch-free augments or if you called the showdown, or soften the blow of being Compromised. The numbers are printed on each card.",
   "有些义体效果更多：安装时抽牌、让你免疫骇入、按其上的改装数、按你无故障的义体数或在你宣布对决时获得额外力量，或减轻被入侵的惩罚。具体数值印在每张卡上。")],
 [("p6_title", "title", "Deck Building", "组建卡组"),
  ("p6_1", "body", "A legal deck has 10 to 20 cards, with no more than 2 copies of any one card. Keep it in a deck box: the box's Validate button checks it for you.",
   "合规卡组有 10 到 20 张牌，同名卡最多 2 张。把卡组放进卡组盒：盒子的验证按钮会帮你检查。"),
  ("p6_2", "section", "Tips from the regulars", "常客的建议"),
  ("p6_3", "body", "- Bring a torso. It's your best source of Stability.", "- 带上躯干，它是稳定度的最佳来源。"),
  ("p6_4", "body", "- Pack at least 2 arm augments, a head and legs, so you can fill your body.", "- 至少带 2 个手臂义体、一个头部和腿部，才能把身体装满。"),
  ("p6_5", "body", "- Don't run more mods than augments: mods need something to sit on.", "- 改装不要比义体多：改装得有地方装。"),
  ("p6_6", "body", "- A few hacks keep greedy opponents honest.", "- 带几张骇入卡，让贪心的对手老实点。"),
  ("p6_7", "body", "- Watch your Glitch. Half of a big number is often a small number.", "- 注意故障。大数字的一半往往是个小数字。"),
  ("p6_8", "body", "Deck builders may also come to your shop with a box and a wish list. Fill it to their order and they'll pay well for the work.",
   "组卡客也可能带着卡组盒和需求清单来你的店。按要求装好，他们会付个好价钱。")],
 [("p7_title", "title", "Playing for Stakes", "下注对战"),
  ("p7_1", "body", "At the Den's back table you can challenge the regulars: Rook plays it safe, Vex goes all in, and Patch loves a good hack. Fans of the game may also ask for a quick match at your counter.",
   "在 Den 后面的桌子上你可以挑战常客：Rook 打法稳健，Vex 全力进攻，Patch 最爱骇入。喜欢这游戏的顾客也可能在你的柜台邀你来一局。"),
  ("p7_2", "body", "Put your deck box in the Deck slot, then choose your stakes:", "把卡组盒放进卡组槽，然后选择赌注："),
  ("p7_3", "body", "- Credits: the winner takes the pot.", "- 信用点：赢家通吃。"),
  ("p7_4", "body", "- A card: win and you keep it, plus a new card of the same rarity (sometimes better, often one you don't own yet). Lose and your opponent keeps it.",
   "- 卡牌：赢了保留自己的卡，再得一张同稀有度的新卡（有时更好，常常是你还没有的）。输了卡归对手。"),
  ("p7_5", "body", "Or play for nothing at all, just for the glory.", "也可以什么都不押，只为荣誉而战。")],
]
chrome = [
    # this game build can't resolve the vanilla paper art by name, so the book ships its own copies
    {"type": "button", "id": "close", "action": "close", "x": 297, "y": 2.5, "w": 14, "h": 15, "sprite": "paper_close", "hoverSprite": "paper_close_hover"},
    {"type": "button", "id": "flip_left", "action": "flip_left", "x": 124, "y": 380, "w": 14, "h": 15, "sprite": "paper_prev", "hoverSprite": "paper_prev_hover"},
    {"type": "button", "id": "flip_right", "action": "flip_right", "x": 176, "y": 380.5, "w": 14, "h": 15, "sprite": "paper_next", "hoverSprite": "paper_next_hover"},
    {"type": "text", "id": "page_indicator", "action": "page_indicator", "style": "page_number", "align": "center", "valign": "middle", "x": 140.5, "y": 374, "w": 33, "h": 25},
]
book = {"id": "zamco_guide", "title": "Zamco Stockist Guide", "background": "zamco_notebook", "width": 628, "height": 804,
        "posX": -450, "posY": 0, "chrome": chrome, "pages": [],
        "item": {"id": "zams-collectibles.zamco_guide", "nameLoc": "guide_item_name", "descriptionLoc": "guide_item_desc", "value": 5}}
strings = [("guide_item_name", "Zamco Stockist Guide", "Zamco 经销商指南"),
           ("guide_item_desc", "Welcome pack from Zamco Electronics: where to find the Downside Den, and how to play and build decks for Aug Warriors.",
            "Zamco 电子的欢迎资料：Downside Den 的位置，以及 Aug Warriors 的玩法和组卡。")]
for n, page in enumerate(pages, 1):
    flow = f"flow{n}"
    top = 26
    elements = []
    if n == 1:
        elements.append({"type": "image", "x": 81, "y": 18, "w": 168, "h": 64, "sprite": "zamco_logo", "fit": "stretch"})
        top = 86
    elements.append({"type": "flow", "id": flow, "x": 40, "y": top, "w": 254, "h": 366 - top, "spacing": 6})
    for key, style, en, zh in page:
        align = "center" if style in ("header", "title", "serif_title", "caption") else "left"
        elements.append({"type": "text", "flow": flow, "x": 40, "y": top, "w": 254, "h": 14, "loc": "guide_" + key,
                         "style": style, "align": align, "wrap": True, "autoHeight": True})
        strings.append(("guide_" + key, en, zh))
    book["pages"].append({"name": f"Page {n}", "elements": elements})

with open(root + "/Books/zamco_guide.json", "w", encoding="utf-8") as f:
    json.dump(book, f, ensure_ascii=False, indent=1)

def cell(v):
    return '"' + v.replace('"', '""') + '"' if any(c in v for c in ',"') else v

for lang, i in (("en", 1), ("zh", 2)):
    with open(f"{root}/Localization/{lang}.csv", "a", encoding="utf-8") as f:
        for s in strings:
            f.write(f"{s[0]},{cell(s[i])}\n")
print(len(book["pages"]), "pages,", len(strings), "strings")
