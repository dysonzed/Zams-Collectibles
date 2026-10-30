using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;

namespace ZamsCollectibles
{
    /// <summary>
    /// Deck orders: once the Downside Den is open, a customer sometimes comes in wanting a deck
    /// built to spec, like the gun-licence customers who order custom firearms. They leave their
    /// own empty deck box on the counter; its tooltip lists the requirements and ticks them off
    /// as the player fills it. The customer only accepts the box once every requirement is met,
    /// and pays for the cards plus the order's bonus. Sent away, they take their box back and
    /// the cards in it go to the player's back counter.
    /// </summary>
    public static class DeckOrders
    {
        public const string ORDER_BOX_ID = ModEntry.MOD_ID + ".order_box";
        private const string ORDER_TAG = "ZAMS_DECK_ORDER_STRING";   // the requirements, as JSON
        private const string ORDER_ID_TAG = "ZAMS_DECK_ORDER_ID_INT";  // ties the box to its customer
        private const string BONUS_TAG = "ZAMS_DECK_ORDER_BONUS_INT";
        private const int COLUMNS = 5, ROWS = 2;   // up to 10 cards
        private const int GAME_CHANCE = 15;        // percent: asks for a game of Aug Warriors when sent off

        private static readonly Color Met = new Color32(0x67, 0xAA, 0xA5, 0xFF);
        private static readonly Color Unmet = new Color32(0xE0, 0x50, 0x5A, 0xFF);

        // ------------------------------------------------------------ requirements

        public class Requirement
        {
            public string kind;     // set, power, slot, type, rarity, size
            public string value;    // the set / slot / card type / rarity id
            public int count;       // how many cards
            public int power;       // power: at least this much Power
            public bool optional;   // an extra: not needed to complete the order, pays bonus if met
            public int bonus;

            public bool IsMet(List<GameItem> items) => Count(items) >= count;

            // Graded, condition and legal-deck requirements look at the card items themselves.
            public bool OnItems => kind == "graded" || kind == "condition" || kind == "grade9" || kind == "legal";

            public int Count(List<GameItem> items)
            {
                List<CardDef> cards = items.Select(CardItems.GetCardDef).Where(c => c != null).ToList();
                switch (kind)
                {
                    case "graded": return items.Count(CardItems.IsGraded);
                    case "grade9": return items.Count(i => CardItems.IsGraded(i) && CardCondition.GradeOf(CardItems.GetCondition(i)) >= 9f);
                    case "condition": return items.Count(i => CardItems.GetCondition(i) >= CLEAN);
                    case "legal": return AugWarriors.AugRules.IsLegalDeck(cards) ? 1 : 0;
                    default: return CountCards(cards);
                }
            }

            private int Tier => int.TryParse(value, out int t) ? t : 1;

            public int CountCards(List<CardDef> cards)
            {
                switch (kind)
                {
                    case "set": return cards.Count(c => c.set.id == value);
                    case "power": return cards.Count(c => c.kind == "augment" && c.power >= power);
                    case "slot": return cards.Count(c => c.kind == "augment" && c.slot == value);
                    case "type": return cards.Count(c => c.kind == value);
                    case "rarity": return cards.Count(c => c.rarity == value);
                    case "effect": return cards.Count(c => !string.IsNullOrEmpty(c.effect));
                    case "glitch_free": return cards.Count(c => c.kind == "augment" && c.glitch == 0);
                    case "risky": return cards.Count(c => c.kind == "augment" && c.glitch >= 3);
                    case "stability": return cards.Count(c => c.stability > 0);
                    case "variety": return cards.Select(c => c.set).Distinct().Count();
                    case "playset": return cards.GroupBy(c => c).Count(g => g.Count() >= 2);
                    case "tier": return cards.Count(c => c.set.tier >= Tier);
                    // whole-box asks: met (1) or not (0)
                    case "full_body":
                        return cards.Any(c => c.slot == "head") && cards.Any(c => c.slot == "torso") && cards.Any(c => c.slot == "legs")
                            && cards.Count(c => c.kind == "augment" && c.slot == "arm") >= 2 ? 1 : 0;
                    case "mono_set": return cards.Count > 0 && cards.All(c => c.set == cards[0].set) ? 1 : 0;
                    case "no_hacks": return cards.Count > 0 && cards.All(c => c.kind != "hack") ? 1 : 0;
                    case "budget": return cards.Count > 0 && cards.All(c => c.rarity == "common" || c.rarity == "uncommon") ? 1 : 0;
                    default: return cards.Count;
                }
            }

            public string Describe()
            {
                switch (kind)
                {
                    case "set": return Loc.Get("order_req_set", count, CardDatabase.SetsById.TryGetValue(value, out CardSetDef s) ? Loc.SetName(s) : value);
                    case "power": return Loc.Get("order_req_power", count, power);
                    case "slot": return Loc.Get("order_req_slot", count, Loc.SlotName(value));
                    case "type": return Loc.Get(value == "hack" ? "order_req_hacks" : "order_req_mods", count);
                    case "rarity": return Loc.Get("order_req_rarity", count, Loc.RarityName(value));
                    case "graded": return Loc.Get("order_req_graded", count);
                    case "condition": return Loc.Get("order_req_condition", count);
                    case "legal": return Loc.Get("order_req_legal");
                    case "grade9": return Loc.Get("order_req_grade9");
                    case "effect": return Loc.Get("order_req_effect", count);
                    case "glitch_free": return Loc.Get("order_req_glitch_free", count);
                    case "risky": return Loc.Get("order_req_risky", count);
                    case "stability": return Loc.Get("order_req_stability", count);
                    case "variety": return Loc.Get("order_req_variety", count);
                    case "playset": return Loc.Get("order_req_playset", count);
                    case "tier": return Loc.Get("order_req_tier", count, Tier);
                    case "full_body": return Loc.Get("order_req_full_body");
                    case "mono_set": return Loc.Get("order_req_mono_set");
                    case "no_hacks": return Loc.Get("order_req_no_hacks");
                    case "budget": return Loc.Get("order_req_budget");
                    default: return Loc.Get("order_req_size", count);
                }
            }
        }

        private const float CLEAN = 7f;   // Lightly Played or better: about a third of fresh cards
        private const int LEGAL_DECK_CHANCE = 30;      // percent of orders that want a real, playable deck
        private const int COLLECTOR_EXTRA_CHANCE = 65; // percent of orders with one or two special extras

        /// <summary>
        /// A random order, like a gun order: required is a minimum size (or, sometimes, a legal
        /// 10-card Aug Warriors deck) and, half the time, one thing from the list; one to three
        /// optional extras each pay their own bonus if met, sometimes one about graded or pristine
        /// cards. All of them can be met from the licensed card pool. Pay scales with the tier.
        /// </summary>
        private static List<Requirement> RollOrder()
        {
            List<Requirement> picked = PickRequirements(RNG.GetRandomInt(1, 3) + (RNG.Roll(50) ? 1 : 0));
            int specials = RNG.Roll(COLLECTOR_EXTRA_CHANCE) ? (RNG.Roll(35) ? 2 : 1) : 0;
            picked.AddRange(PickSpecials(specials, picked));
            float scale = PayScale(picked);
            List<Requirement> order = new List<Requirement>();
            int required = picked.Count > 1 && !picked[0].OnItems && RNG.Roll(50) ? 1 : 0;
            for (int i = 0; i < picked.Count; i++)
            {
                Requirement r = picked[i];
                r.optional = i >= required;
                if (r.optional) r.bonus = (int)Math.Round(ExtraBonus(r) * scale);
                order.Add(r);
            }
            if (RNG.Roll(LEGAL_DECK_CHANCE))
            {
                order.Insert(0, new Requirement { kind = "legal", count = 1 });
                order.Insert(0, new Requirement { kind = "size", count = COLUMNS * ROWS });
            }
            else
            {
                int size = Math.Max(RNG.GetRandomInt(5, 8), order.Where(o => !o.optional).Sum(o => o.count));
                order.Insert(0, new Requirement { kind = "size", count = Math.Min(size, COLUMNS * ROWS) });
            }
            return order;
        }

        /// <summary>
        /// Special extras: deck-building and collector asks beyond the basic list. Always optional,
        /// never two that contradict each other or the rest of the order, and only ones the
        /// licensed card pool can meet.
        /// </summary>
        private static List<Requirement> PickSpecials(int wanted, List<Requirement> order)
        {
            List<CardDef> pool = Progression.AvailableCards();
            var candidates = new List<Func<Requirement>>
            {
                () => new Requirement { kind = "graded", count = 1 },
                () => new Requirement { kind = "grade9", count = 1 },
                () => new Requirement { kind = "condition", count = RNG.GetRandomInt(2, 3) },
                () => new Requirement { kind = "effect", count = RNG.GetRandomInt(2, 4) },
                () => new Requirement { kind = "glitch_free", count = RNG.GetRandomInt(2, 3) },
                () => new Requirement { kind = "risky", count = RNG.GetRandomInt(1, 2) },
                () => new Requirement { kind = "stability", count = 2 },
                () => new Requirement { kind = "variety", count = 3 },
                () => new Requirement { kind = "playset", count = RNG.GetRandomInt(1, 2) },
                () => new Requirement { kind = "full_body", count = 1 },
                () => new Requirement { kind = "mono_set", count = 1 },
                () => new Requirement { kind = "no_hacks", count = 1 },
                () => new Requirement { kind = "budget", count = 1 },
            };
            if (Progression.MaxTier >= 2)
                candidates.Add(() => new Requirement { kind = "tier", value = RNG.GetRandomInt(2, Progression.MaxTier).ToString(), count = 2 });

            List<Requirement> picked = new List<Requirement>();
            int guard = 0;
            while (picked.Count < wanted && guard++ < 40)
            {
                Requirement r = RNG.GetRandomElement(candidates)();
                if (order.Concat(picked).Any(o => o.kind == r.kind || Conflicts(o, r))) continue;
                bool countable = r.kind == "effect" || r.kind == "glitch_free" || r.kind == "risky" || r.kind == "stability"
                    || r.kind == "variety" || r.kind == "tier";
                if (countable && r.CountCards(pool) < r.count) continue;   // the licensed cards can't meet it
                picked.Add(r);
            }
            return picked;
        }

        // Asks that can't both be met.
        private static bool Conflicts(Requirement a, Requirement b)
        {
            bool Pair(string x, string y) => (a.kind == x && b.kind == y) || (a.kind == y && b.kind == x);
            bool HackAsk(Requirement r) => r.kind == "type" && r.value == "hack";
            bool RareAsk(Requirement r) => r.kind == "rarity" && r.value == "rare";
            return Pair("mono_set", "variety")
                || (a.kind == "no_hacks" && HackAsk(b)) || (b.kind == "no_hacks" && HackAsk(a))
                || (a.kind == "budget" && RareAsk(b)) || (b.kind == "budget" && RareAsk(a))
                || (a.kind == "budget" && b.kind == "grade9") || (b.kind == "budget" && a.kind == "grade9");
        }

        // What an extra pays at Tier 1 prices.
        private static int ExtraBonus(Requirement r)
        {
            switch (r.kind)
            {
                case "grade9": return 60;
                case "full_body": case "mono_set": case "tier": return 25;
                case "variety": case "playset": return 20;
                case "effect": case "glitch_free": case "risky": case "stability": case "no_hacks": case "budget": return 15;
                case "graded": return 35;
                case "condition": return 10 + 5 * r.count;
                case "rarity": return r.value == "rare" ? 30 : 15;
                default: return RNG.GetRandomInt(2, 4) * 5;
            }
        }

        /// <summary>
        /// How much the order's pay is scaled, like card prices: by the requested set's price
        /// multiplier, or the average of the licensed sets when no set is named.
        /// </summary>
        private static float PayScale(List<Requirement> order)
        {
            Requirement set = order.FirstOrDefault(r => r.kind == "set");
            if (set != null && CardDatabase.SetsById.TryGetValue(set.value, out CardSetDef s)) return Math.Max(1f, s.priceMultiplier);
            List<CardSetDef> sets = Progression.AvailableSets();
            return sets.Count == 0 ? 1f : Math.Max(1f, sets.Average(x => x.priceMultiplier));
        }

        /// <summary>The order's base pay: the job, scaled with the tier; extras pay on top.</summary>
        private static int BasePay(List<Requirement> order) =>
            (int)Math.Round((15 + 10 * order.Count(r => !r.optional) + (order.Any(r => r.kind == "legal") ? 20 : 0))
                * PayScale(order));

        // Distinct requirements from the list, each fillable from the card pool.
        private static List<Requirement> PickRequirements(int wanted)
        {
            List<CardDef> pool = Progression.AvailableCards();
            var candidates = new List<Func<Requirement>>
            {
                () => new Requirement { kind = "set", value = RNG.GetRandomElement(Progression.AvailableSets()).id, count = RNG.GetRandomInt(2, 4) },
                // Power asks rise with the licensed tier: 3-5 at Tier 1, 5-7 at Tier 3
                () => new Requirement { kind = "power", power = RNG.GetRandomInt(2, 4) + Progression.MaxTier, count = RNG.GetRandomInt(1, 2) },
                () => new Requirement { kind = "slot", value = "arm", count = RNG.GetRandomInt(2, 4) },
                () => new Requirement { kind = "slot", value = "torso", count = 1 },
                () => new Requirement { kind = "slot", value = RNG.Roll(50) ? "head" : "legs", count = RNG.GetRandomInt(1, 2) },
                () => new Requirement { kind = "type", value = RNG.Roll(50) ? "hack" : "mod", count = RNG.GetRandomInt(1, 2) },
                () => new Requirement { kind = "rarity", value = RNG.Roll(30) ? "rare" : "uncommon", count = 1 },
            };
            List<Requirement> picked = new List<Requirement>();
            int guard = 0;
            while (picked.Count < wanted && guard++ < 30)
            {
                Requirement r = RNG.GetRandomElement(candidates)();
                if (picked.Any(o => o.kind == r.kind && o.value == r.value)) continue;
                if (r.CountCards(pool) == 0) continue;   // nothing in the game could meet it
                picked.Add(r);
            }
            return picked;
        }

        /// <summary>The extras this box earns as filled: the bonuses of the optional requirements it meets.</summary>
        private static int ExtrasEarned(GameItem box)
        {
            List<GameItem> cards = Cards(box);
            return Requirements(box).Where(r => r.optional && r.IsMet(cards)).Sum(r => r.bonus);
        }

        // ------------------------------------------------------------ the order box

        public static void Register(ModItemDirectory dir, Dictionary<string, Action<GameItem>> textByItemId)
        {
            dir.Add(ORDER_BOX_ID, CreateOrderBox);
            textByItemId[ORDER_BOX_ID] = SetText;
        }

        private static GameItem CreateOrderBox()
        {
            (PixelWindow window, GameGridInventory grid) = DirectoryUtils.CreateGridWindow(COLUMNS, ROWS);
            GameItem item = ItemDirectory.CreateEmptyItem(ORDER_BOX_ID)
                .SetContentWindow(window)
                .SetSpriteAndShapeFromMod(ModEntry.MOD_ID, "deck_box");
            SetText(item);
            item.SetGameItemType(Collectibles.TYPE);
            ContainerHelper.InitContainerItem(grid, item, allowedTags: new List<string> { CardItems.CARD_TAG });
            // worth the cards, the order's base pay and the extras it meets so far
            CardItems.TrackContainerValue(item, () => item.GetTagReadonly(BONUS_TAG).GetInt() + ExtrasEarned(item), grid);
            return item;
        }

        private static void SetText(GameItem item)
        {
            item.name = Loc.Get("order_box_name");
            item.shortDescription = Loc.Get("order_box_desc");
        }

        public static bool IsOrderBox(GameItem item) => item != null && item.identifier == ORDER_BOX_ID;

        public static List<Requirement> Requirements(GameItem box)
        {
            try { return JsonConvert.DeserializeObject<List<Requirement>>(box.GetTagReadonly(ORDER_TAG).GetString() ?? "") ?? new List<Requirement>(); }
            catch (JsonException) { return new List<Requirement>(); }
        }

        private static List<GameItem> Cards(GameItem box)
        {
            GameGridInventory grid = GraphUtils.FindChildType<GameGridInventory>(box);
            return grid == null ? new List<GameItem>() : grid.childItems.Where(CardItems.IsCard).ToList();
        }

        public static bool IsFilled(GameItem box)
        {
            List<GameItem> cards = Cards(box);
            List<Requirement> reqs = Requirements(box);
            return reqs.Count > 0 && reqs.Where(r => !r.optional).All(r => r.IsMet(cards));
        }

        /// <summary>Tooltip: the required list and the optional extras with what each pays, ticked or not.</summary>
        public static void Tooltip(RichTextBuilder builder, GameItem box)
        {
            List<GameItem> cards = Cards(box);
            List<Requirement> reqs = Requirements(box);
            builder.AddLine(Loc.Get("order_tooltip_header"), bold: true);
            foreach (Requirement r in reqs.Where(r => !r.optional)) Line(builder, r, cards, "");
            if (reqs.Any(r => r.optional))
            {
                builder.AddLine(Loc.Get("order_tooltip_extras"), bold: true);
                foreach (Requirement r in reqs.Where(r => r.optional)) Line(builder, r, cards, $" +{r.bonus}{GeneralHelper.CreditIcon()}");
            }
            builder.AddLine(Loc.Get("order_tooltip_bonus", box.GetTagReadonly(BONUS_TAG).GetInt() + ExtrasEarned(box) + GeneralHelper.CreditIcon()));
        }

        private static void Line(RichTextBuilder builder, Requirement r, List<GameItem> cards, string suffix)
        {
            bool met = r.IsMet(cards);
            RenderHandler.ColorPalette color = met ? RenderHandler.ColorPalette.Green : r.optional ? RenderHandler.ColorPalette.Gray : RenderHandler.ColorPalette.Red;
            builder.AddLine($"{(met ? "+" : "-")} {r.Describe()} ({Math.Min(r.Count(cards), r.count)}/{r.count}){suffix}", color: color);
        }

        // ------------------------------------------------------------ the customer

        public static StoreClient CreateCustomer()
        {
            List<Requirement> order = RollOrder();
            int orderId = RNG.GetRandomInt(1, int.MaxValue - 1);
            int bonus = BasePay(order);

            StoreClient client = new StoreClient();
            client.identifier = ModEntry.MOD_ID + ".deck_order";
            client.displayName = Loc.Get("client_deck_order");
            StoreClientFactionSetup.InitLowerLevelCitizen(client);
            client.spriteName = SpriteDict.GetRandomScavSpriteName();
            client.clientIntent = StoreClient.ClientIntent.BUY;
            client.isBargainAvailable = false;
            client.SetBudget(1000);
            // Buys only their own box, once it meets the order, and the cards inside it.
            client.willAcceptItem = item =>
                (IsOrderBox(item) && item.GetTagReadonly(ORDER_ID_TAG).GetInt() == orderId && IsFilled(item))
                || (CardItems.IsCard(item) && IsOrderBox(item.parentInventory?.GetParentItem()));

            // The details are on the tablet and the box's tooltip; the customer just asks.
            client.mainDialogue.SetText(client.displayName, Loc.Get("client_deck_order_line"))
                .NextDialogue().SetText(client.displayName, Loc.Get("client_deck_order_wants"))
                .SetEndAction(() => HandOverBox(orderId, order, bonus));
            client.OnDismiss = () => TakeBackBox(orderId);
            AugWarriors.StoreChallenge.Offer(client, GAME_CHANCE);
            client.CompleteClientCreation();
            Orders.Add(client, new Order { id = orderId, requirements = order });
            return client;
        }

        // ------------------------------------------------------------ the shop tablet

        private class Order
        {
            public int id;
            public List<Requirement> requirements;
        }

        // The order each deck builder came in with, for the tablet to show while they're at the counter.
        private static readonly ConditionalWeakTable<StoreClient, Order> Orders = new ConditionalWeakTable<StoreClient, Order>();

        private static List<GameItem> OrderCards(int orderId)
        {
            GameItem box = EmporiumEntry.Instance.GetAllItems().FirstOrDefault(i => IsOrderBox(i) && i.GetTagReadonly(ORDER_ID_TAG).GetInt() == orderId);
            return box == null ? new List<GameItem>() : Cards(box);
        }

        /// <summary>
        /// The order sheet on the negotiation tablet, like the gunsmith's: the requirements and the
        /// extras with what each pays, turning green as the box meets them. Drawn above the box row.
        /// </summary>
        public static void DrawTabletSheet(NegotiationTabletFrame frame, StoreClient client)
        {
            if (frame == null || client == null || !Orders.TryGetValue(client, out Order order)) return;
            List<GameItem> cards = OrderCards(order.id);
            TabletUI.SubHeader(frame, frame.content, Loc.Get("tablet_order_required"), 28f);
            foreach (Requirement r in order.requirements.Where(r => !r.optional)) TabletRow(frame, r, cards, "");
            if (!order.requirements.Any(r => r.optional)) return;
            TabletUI.SubHeader(frame, frame.content, Loc.Get("tablet_order_extras"), 28f);
            foreach (Requirement r in order.requirements.Where(r => r.optional)) TabletRow(frame, r, cards, "+" + TabletUI.Credits(r.bonus));
        }

        private static void TabletRow(NegotiationTabletFrame frame, Requirement r, List<GameItem> cards, string reward)
        {
            bool met = r.IsMet(cards);
            Color color = met ? TabletStyle.GREEN : TabletStyle.INK;
            RectTransform row = TabletUI.TextRow(frame, frame.content, $"{r.Describe()} ({Math.Min(r.Count(cards), r.count)}/{r.count})", 18f, color);
            if (reward.Length > 0)
                TabletUI.NewText(frame, row, "Reward", 368f, 0f, 130f, 28f, FontLoader.FontRole.Noto_Sans, 20f, color, TextAlignmentOptions.MidlineRight).text = reward;
        }

        /// <summary>The tablet redraws when its signature changes: add how far along the order is.</summary>
        public static string TabletSignature(StoreClient client)
        {
            if (client == null || !Orders.TryGetValue(client, out Order order)) return "";
            List<GameItem> cards = OrderCards(order.id);
            return "|deck:" + string.Join(",", order.requirements.Select(r => r.Count(cards)));
        }

        // The customer's empty deck box goes on the counter, the player's to fill.
        private static void HandOverBox(int orderId, List<Requirement> order, int bonus)
        {
            GameItem box = DirectoryMaster.Item(ORDER_BOX_ID);
            box.ModifyTag(ORDER_TAG, s => s.Enable().SetString(JsonConvert.SerializeObject(order)));
            box.ModifyTag(ORDER_ID_TAG, s => s.Enable().SetInt(orderId));
            box.ModifyTag(BONUS_TAG, s => s.Enable().SetInt(bonus));
            box.SetValue(bonus);
            if (GraphUtils.TryAcceptAllMid(EmporiumEntry.Instance.frontInvinvElement, box) > 0)
                GraphUtils.EmporiumTryAdd(EmporiumEntry.Instance.backInvinvElement, box);
        }

        // Leaving: a box still in the player's hands goes back to its owner, the cards in it don't.
        private static void TakeBackBox(int orderId)
        {
            foreach (GameItem box in EmporiumEntry.Instance.GetAllItems().Where(i => IsOrderBox(i) && i.GetTagReadonly(ORDER_ID_TAG).GetInt() == orderId).ToList())
            {
                if (!GeneralHelper.IsItemOwned(box)) continue;   // sold: the customer has it
                GameGridInventory grid = GraphUtils.FindChildType<GameGridInventory>(box);
                foreach (GameItem card in grid?.childItems.ToList() ?? new List<GameItem>())
                {
                    grid.Expel(card);
                    GraphUtils.EmporiumTryAdd(EmporiumEntry.Instance.backInvinvElement, card);
                }
                box.Destroy();
            }
        }
    }

    // The deck builder is a buying customer, so the tablet shows its buying page; the order sheet goes on top.
    // The tablet isn't part of the modding API, so this reaches into its private code. If a game update
    // changes that code the patch skips itself (Prepare) or swallows its error, and the order box's
    // tooltip is still there.
    [HarmonyPatch(typeof(NegociationUIManager), "BuildBuying")]
    public static class DeckOrderTabletPatch
    {
        private static readonly System.Reflection.FieldInfo FrameField = AccessTools.Field(typeof(NegociationUIManager), "frame");
        private static bool failed;

        static bool Prepare() =>
            FrameField != null && typeof(NegotiationTabletFrame).IsAssignableFrom(FrameField.FieldType)
            && AccessTools.Method(typeof(NegociationUIManager), "BuildBuying", new[] { typeof(StoreClient), typeof(bool) }) != null;

        static void Prefix(NegociationUIManager __instance, StoreClient storeClient)
        {
            if (failed) return;
            try { DeckOrders.DrawTabletSheet(FrameField.GetValue(__instance) as NegotiationTabletFrame, storeClient); }
            catch (Exception e) { failed = true; ModEntry.Instance.Log.Log($"Deck order tablet sheet disabled: {e.Message}"); }
        }
    }

    [HarmonyPatch(typeof(NegociationUIManager), "BuildSignature")]
    public static class DeckOrderTabletSignaturePatch
    {
        static bool Prepare() =>
            AccessTools.Method(typeof(NegociationUIManager), "BuildSignature", new[] { typeof(StoreClient) })?.ReturnType == typeof(string);

        static void Postfix(StoreClient storeClient, ref string __result)
        {
            try { __result += DeckOrders.TabletSignature(storeClient); }
            catch (Exception) { }
        }
    }
}
