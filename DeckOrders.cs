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

            public bool IsMet(List<CardDef> cards) => Count(cards) >= count;

            public int Count(List<CardDef> cards)
            {
                switch (kind)
                {
                    case "set": return cards.Count(c => c.set.id == value);
                    case "power": return cards.Count(c => c.kind == "augment" && c.power >= power);
                    case "slot": return cards.Count(c => c.kind == "augment" && c.slot == value);
                    case "type": return cards.Count(c => c.kind == value);
                    case "rarity": return cards.Count(c => c.rarity == value);
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
                    default: return Loc.Get("order_req_size", count);
                }
            }
        }

        /// <summary>
        /// A random order, like a gun order: required is a minimum size and, half the time, one
        /// thing from the list; one to three optional extras each pay their own bonus if met.
        /// All of them can be met from the card pool.
        /// </summary>
        private static List<Requirement> RollOrder()
        {
            List<Requirement> picked = PickRequirements(RNG.GetRandomInt(1, 3) + (RNG.Roll(50) ? 1 : 0));
            List<Requirement> order = new List<Requirement>();
            int required = picked.Count > 1 && RNG.Roll(50) ? 1 : 0;
            for (int i = 0; i < picked.Count; i++)
            {
                Requirement r = picked[i];
                r.optional = i >= required;
                if (r.optional) r.bonus = r.kind == "rarity" && r.value == "rare" ? 30 : RNG.GetRandomInt(2, 4) * 5;
                order.Add(r);
            }
            int size = Math.Max(RNG.GetRandomInt(5, 8), order.Where(o => !o.optional).Sum(o => o.count));
            order.Insert(0, new Requirement { kind = "size", count = Math.Min(size, COLUMNS * ROWS) });
            return order;
        }

        // Distinct requirements from the list, each fillable from the card pool.
        private static List<Requirement> PickRequirements(int wanted)
        {
            List<CardDef> pool = CardDatabase.Sets.SelectMany(s => s.cards).ToList();
            var candidates = new List<Func<Requirement>>
            {
                () => new Requirement { kind = "set", value = RNG.GetRandomElement(CardDatabase.Sets).id, count = RNG.GetRandomInt(2, 4) },
                () => new Requirement { kind = "power", power = RNG.GetRandomInt(3, 5), count = RNG.GetRandomInt(1, 2) },
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
                if (r.Count(pool) == 0) continue;   // nothing in the game could meet it
                picked.Add(r);
            }
            return picked;
        }

        /// <summary>The extras this box earns as filled: the bonuses of the optional requirements it meets.</summary>
        private static int ExtrasEarned(GameItem box)
        {
            List<CardDef> cards = Cards(box);
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

        private static List<CardDef> Cards(GameItem box)
        {
            GameGridInventory grid = GraphUtils.FindChildType<GameGridInventory>(box);
            return grid == null ? new List<CardDef>() : grid.childItems.Select(CardItems.GetCardDef).Where(c => c != null).ToList();
        }

        public static bool IsFilled(GameItem box)
        {
            List<CardDef> cards = Cards(box);
            List<Requirement> reqs = Requirements(box);
            return reqs.Count > 0 && reqs.Where(r => !r.optional).All(r => r.IsMet(cards));
        }

        /// <summary>Tooltip: the required list and the optional extras with what each pays, ticked or not.</summary>
        public static void Tooltip(RichTextBuilder builder, GameItem box)
        {
            List<CardDef> cards = Cards(box);
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

        private static void Line(RichTextBuilder builder, Requirement r, List<CardDef> cards, string suffix)
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
            int bonus = 15 + 10 * order.Count(r => !r.optional);   // base pay for the job; extras pay on top

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

        private static List<CardDef> OrderCards(int orderId)
        {
            GameItem box = EmporiumEntry.Instance.GetAllItems().FirstOrDefault(i => IsOrderBox(i) && i.GetTagReadonly(ORDER_ID_TAG).GetInt() == orderId);
            return box == null ? new List<CardDef>() : Cards(box);
        }

        /// <summary>
        /// The order sheet on the negotiation tablet, like the gunsmith's: the requirements and the
        /// extras with what each pays, turning green as the box meets them. Drawn above the box row.
        /// </summary>
        public static void DrawTabletSheet(NegotiationTabletFrame frame, StoreClient client)
        {
            if (frame == null || client == null || !Orders.TryGetValue(client, out Order order)) return;
            List<CardDef> cards = OrderCards(order.id);
            TabletUI.SubHeader(frame, frame.content, Loc.Get("tablet_order_required"), 28f);
            foreach (Requirement r in order.requirements.Where(r => !r.optional)) TabletRow(frame, r, cards, "");
            if (!order.requirements.Any(r => r.optional)) return;
            TabletUI.SubHeader(frame, frame.content, Loc.Get("tablet_order_extras"), 28f);
            foreach (Requirement r in order.requirements.Where(r => r.optional)) TabletRow(frame, r, cards, "+" + TabletUI.Credits(r.bonus));
        }

        private static void TabletRow(NegotiationTabletFrame frame, Requirement r, List<CardDef> cards, string reward)
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
            List<CardDef> cards = OrderCards(order.id);
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
