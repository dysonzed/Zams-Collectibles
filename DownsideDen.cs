using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace ZamsCollectibles
{
    /// <summary>
    /// The Downside Den, a local game store visited after hours, run by Downside Dan.
    /// It works like Dr. Jackson's shop: tonight's stock (a few booster packs and singles,
    /// mostly commons) hangs on a display board; dragging an item onto the counter opens the
    /// game's own sale window with Dan, who also buys collectibles from the player.
    ///
    /// Dan is painted into the background, like Jackson; the store client that runs the trades
    /// is hidden. The stock is rolled once per day and saved with the run.
    ///
    /// The board and counter are the store's own counters, driven the way Jackson's visit
    /// drives them. If that ever fails (a game update), the visit falls back to a shop list in
    /// the location panel, which only uses the supported custom-UI API.
    /// </summary>
    public class DownsideDen : AfterhourLocation
    {
        public const string LOCATION_ID = ModEntry.MOD_ID + ".downside_den";
        private const string STOCK_KEY = "den_stock";
        private const string PANEL_WINDOW_ID = "afterhour_location";   // the game's id for location panels

        private const int MIN_PACKS = 2, MAX_PACKS = 5;
        private const int BOX_CHANCE = 20;              // percent of nights with a booster box in stock
        private const int MIN_SINGLES = 6, MAX_SINGLES = 9;
        private const int MARKUP_PERCENT = 25;          // Dan's markup on what he sells
        private const float LIST_SINGLE_MARKUP = 1.3f;  // fallback shop list prices
        private const float LIST_PACK_MARKUP = 1.15f;

        // Layout, in the game's location-panel units (about screen pixels from the centre).
        private static readonly Vector2 PanelPosition = new Vector2(745f, -385f);   // bottom right, like Jackson's Return
        private static readonly Vector2 ListPanelPosition = new Vector2(-640f, 120f);
        private static readonly Vector2 CreditPosition = new Vector2(700f, 440f);   // on the wall above the stock board
        private const string CREDIT_COLOR = "#D5D2B8";                              // cream, readable on the dark wall

        // The stock board: a 12 x 18 grid sized to the frame in den_scene.png (Jackson's is 14 x 21).
        private const int BOARD_COLUMNS = 12, BOARD_ROWS = 18;

        public static void Register() => ModHelper.AddAfterhourLocation(new DownsideDen());

        public override string Id => LOCATION_ID;
        public override string Source => ModEntry.MOD_ID;
        public override string ImageKey => $"{ModEntry.MOD_ID}:den_card";
        public override string BackgroundKey => $"{ModEntry.MOD_ID}:den_scene";
        public override string GetTitle() => Loc.Get("den_name");
        public override bool IsDiscovered() => Progression.DenUnlocked;   // found through Wild's Network

        // The board sits inside the frame painted on the right of den_scene.png; the counter
        // (where items are dropped to trade) is in the same place as Jackson's.
        public override Vector2? GetFrontCounterPosition(int w, int h) => new Vector2((float)(w / 100) * 80f, -(float)(h / 100) * 14.3f);
        public override Vector2? GetBackCounterPosition(int w, int h) => new Vector2((float)(w / 2) - (float)(w / 100) * 18f, 0f - (float)(h / 100) * 55f);

        private bool boardOpen;      // the board and Dan are up (false: shop list fallback)
        private bool panelPlaced;
        private Stock stock;
        private readonly List<(GameItem item, PackStock pack, SingleStock single)> onBoard = new List<(GameItem, PackStock, SingleStock)>();

        // ------------------------------------------------------------ visit

        public override void Arrive()
        {
            panelPlaced = false;
            stock = LoadStock();
            try
            {
                OpenBoard();
                boardOpen = true;
            }
            catch (Exception e)
            {
                ModEntry.Instance.Log.Warning($"Downside Den: the shop board could not open ({e.GetType().Name}: {e.Message}). Using the shop list.");
                boardOpen = false;
                try { CloseBoard(); } catch (Exception) { }
            }
        }

        public override void Leave()
        {
            // Walking out concedes a match in progress; the table hands back its deck and ante.
            AugWarriors.AugBoard.CloseIfOpen();
            AugWarriors.AugMatchWindow.CloseIfOpen();
            AugWarriors.AugTable.Close();
            if (!boardOpen) return;
            try
            {
                CloseBoard();
            }
            catch (Exception e)
            {
                ModEntry.Instance.Log.Warning($"Downside Den: closing the shop board failed ({e.GetType().Name}: {e.Message}).");
            }
            boardOpen = false;
        }

        // The game positions the panel after building it; move it once it exists.
        public override void Tick()
        {
            if (panelPlaced || CustomUIManager.Instance == null) return;
            CustomUIWindow panel = CustomUIManager.Instance.GetWindow(PANEL_WINDOW_ID);
            if (panel == null || !panel.IsAlive) return;
            panel.SetPosition(boardOpen ? PanelPosition : ListPanelPosition);
            panelPlaced = true;
        }

        // Mirrors Jackson's arrival: counters out, stock on the board, the shopkeeper at the counter.
        private void OpenBoard()
        {
            EmporiumEntry store = EmporiumEntry.Instance;
            MapUIManager map = MapUIManager.Instance;
            PlayerStore player = PlayerStore.Instance;

            player.playerLocation = PlayerStore.PlayerLocation.INVENTOR;
            store.frontInvWindow.Show();
            store.backInvWindow.Show();
            map.TransferBackInvToCounter();
            store.frontInvinvElement.SetShape(BOARD_COLUMNS, BOARD_ROWS);
            store.frontInvinvElement.Validate();
            PlaceStock();

            player.currentClientInstance = StoreClientInstance.CreateClientInstance(CreateDan());
            StoreUIManager.Instance.HideClient();   // Dan is part of the scene
            player.isClientArrived = true;

            var credits = StoreUIManager.Instance.storeCreditDisplay.creditDisplayText;
            credits.gameObject.SetActive(true);
            credits.rectTransform.anchoredPosition = CreditPosition;
            ColorUtility.TryParseHtmlString(CREDIT_COLOR, out Color screen);
            credits.color = screen;
        }

        // Mirrors Jackson's departure: what the player owns goes back to their back counter, the rest goes.
        private void CloseBoard()
        {
            RecordSales();

            EmporiumEntry store = EmporiumEntry.Instance;
            MapUIManager map = MapUIManager.Instance;
            NegociationUIManager.Instance.CloseUI();

            List<GameItem> owned = new List<GameItem>();
            foreach (GameInventory counter in new GameInventory[] { store.frontInvinvElement, store.backInvinvElement })
            {
                foreach (GameItem item in counter.childItems.ToList())
                {
                    if (!GeneralHelper.IsItemOwned(item)) continue;
                    item.CloseContentWindow(includeChildren: true);
                    counter.Expel(item);
                    owned.Add(item);
                }
            }
            store.ResetFrontCounterGrid();
            store.ClearFrontInv();
            store.ClearBackInv();
            map.TransferCounterToBackInv();
            foreach (GameItem item in owned) GraphUtils.EmporiumTryAdd(store.backInvWindow, item);
            foreach (GameItem item in store.afterhourInventory.childItems.ToList()) GraphUtils.EmporiumTryAdd(store.backInvWindow, item);

            map.ResetCreditDisplayPosition();
            map.DeactivateClientOutsideStore();
            onBoard.Clear();
        }

        private void PlaceStock()
        {
            onBoard.Clear();
            foreach (PackStock pack in stock.packs)
            {
                if (!CardDatabase.SetsById.TryGetValue(pack.set, out CardSetDef set)) continue;
                for (int i = 0; i < pack.count; i++)
                {
                    GameItem item = DirectoryMaster.Item(pack.ItemId(set));
                    if (PlayerStore.Instance.TryAddDirectSellingItemToTable(item)) onBoard.Add((item, pack, null));
                }
            }
            // A binder and a deck box are always in stock: supplies, not collectibles, so they are not tracked.
            PlayerStore.Instance.TryAddDirectSellingItemToTable(DirectoryMaster.Item(CardBinder.BINDER_ID));
            PlayerStore.Instance.TryAddDirectSellingItemToTable(DirectoryMaster.Item(AugWarriors.DeckBox.DECKBOX_ID));

            foreach (SingleStock single in stock.singles)
            {
                CardDef card = CardOf(single);
                if (card == null || single.sold) continue;
                GameItem item = MakeCard(single, card);
                if (PlayerStore.Instance.TryAddDirectSellingItemToTable(item)) onBoard.Add((item, null, single));
            }
        }

        // Anything from the board the player now owns was bought.
        private void RecordSales()
        {
            if (stock == null) return;
            bool changed = false;
            foreach (var entry in onBoard)
            {
                if (entry.item == null || !GeneralHelper.IsItemOwned(entry.item)) continue;
                if (entry.pack != null && entry.pack.count > 0) { entry.pack.count--; changed = true; }
                if (entry.single != null && !entry.single.sold) { entry.single.sold = true; changed = true; }
            }
            if (changed) SaveStock(stock);
        }

        /// <summary>
        /// Downside Dan as a store client: sells his stock at a markup and buys collectibles,
        /// set up like the game's own shopkeeper (Dr. Jackson).
        /// </summary>
        private static StoreClient CreateDan()
        {
            StoreClient dan = new StoreClient();
            dan.identifier = ModEntry.MOD_ID + ".downside_dan";
            dan.displayName = Loc.Get("den_dan_name");
            dan.spriteName = SpriteDict.GetInventorSpriteName();   // never shown: Dan is painted into the scene
            dan.clientFaction = "FACTION_LOWER_LEVEL";
            dan.isBargainAvailable = false;
            dan.nonShootable = true;
            dan.SetBudget(60 + StoreStation.Instance.dayCounter * 3);
            dan.budgetConversionRate = 100;
            dan.clientIntent = StoreClient.ClientIntent.SELLNBUY;
            dan.mainDialogue.SetText(dan.displayName, Loc.Get("den_dan_greeting"));
            dan.clientBuyingTagList.Add(Collectibles.TYPE);
            dan.onItemAddedToWeighted = item =>
            {
                if (!GeneralHelper.IsItemOwned(item)) item.AddItemFeature(Markup());
            };
            dan.CompleteClientCreation();
            return dan;
        }

        private static ItemFeature Markup()
        {
            ItemFeature markup = new ItemFeature(ItemFeature.FeatureType.ClientTemporary)
            {
                identifier = "zamsDenMarkup",
                category = "zamsDenMarkup",
                valueStage = ItemFeature.ValueStage.Market,
                isFeatureMatch = true,
            };
            string label = Loc.Get("den_markup");
            markup.SetPublicDisplay(label);
            markup.SetActualDisplay(label);
            markup.SetPreExposedValueMod(MARKUP_PERCENT);
            return markup;
        }

        // ------------------------------------------------------------ panel

        public override void BuildPanel(CustomUIBuilder builder)
        {
            if (stock == null) stock = LoadStock();
            if (boardOpen)
            {
                builder.SetSize(360f, 250f);
                builder.AddLabel(Loc.Get("den_board_hint"));
            }
            else
            {
                BuildShopList(builder);
            }

            // An Aug Warriors match against one of the regulars, for an ante.
            builder.AddSeparator();
            builder.AddButton(Loc.Get("den_challenge"), AugWarriors.AugTable.Open);
        }

        // Fallback when the board can't open: the stock as a list with Buy and View buttons.
        private void BuildShopList(CustomUIBuilder builder)
        {
            builder.SetSize(420f, 600f);
            builder.AddLabel(Loc.Get("den_greeting"));
            builder.AddLabel(CreditsText());
            CustomUIElement credits = builder.Last;
            builder.AddLabel("");
            CustomUIElement status = builder.Last;

            builder.BeginScrollFlexible(200f);
            builder.AddHeader(Loc.Get("den_packs"));
            foreach (PackStock pack in stock.packs)
            {
                if (pack.count <= 0 || !CardDatabase.SetsById.TryGetValue(pack.set, out CardSetDef set)) continue;
                long price = pack.ListPrice(set);
                builder.BeginRow(6f, TextAnchor.MiddleLeft);
                builder.AddImage($"{ModEntry.MOD_ID}:{(string.IsNullOrEmpty(set.packSprite) ? "booster_pack" : set.packSprite)}", 24f, 48f);
                builder.AddLabel(PackLabel(set, pack));
                CustomUIElement label = builder.Last;
                builder.AddFlexibleSpace();
                builder.AddLabel(Price(price));
                CustomUIElement buy = null;
                builder.AddButton(Loc.Get("den_buy"), () =>
                {
                    if (pack.count <= 0 || !TryPay(price, status)) return;
                    pack.count--;
                    SaveStock(stock);
                    Deliver(DirectoryMaster.Item(pack.ItemId(set)));
                    label.SetText(PackLabel(set, pack));
                    if (pack.count <= 0) buy.SetInteractable(false);
                    credits.SetText(CreditsText());
                });
                buy = builder.Last;
                builder.End();
            }

            builder.AddHeader(Loc.Get("den_singles"));
            foreach (SingleStock single in stock.singles)
            {
                CardDef card = CardOf(single);
                if (card == null || single.sold) continue;
                long price = Math.Max(1L, (long)Math.Round(CardItems.ValueFor(card, single.condition, false) * LIST_SINGLE_MARKUP));
                string icon = card.set.cardSprite + CardCondition.IconSuffix(single.condition);
                if (!CardItems.HasModSprite(icon)) icon = card.set.cardSprite;

                builder.BeginRow(6f, TextAnchor.MiddleLeft);
                builder.AddImage($"{ModEntry.MOD_ID}:{icon}", 32f, 32f);
                builder.AddLabel($"{CardItems.CardName(card)}\n{Loc.Get("den_single_detail", Loc.RarityName(card.rarity), CardCondition.Label(single.condition))}");
                builder.AddFlexibleSpace();
                builder.AddLabel(Price(price));
                builder.AddButton(Loc.Get("den_view"), () => CardViewer.Open(MakeCard(single, card)));
                CustomUIElement buy = null;
                builder.AddButton(Loc.Get("den_buy"), () =>
                {
                    if (single.sold || !TryPay(price, status)) return;
                    single.sold = true;
                    SaveStock(stock);
                    Deliver(MakeCard(single, card));
                    buy.SetText(Loc.Get("den_sold"));
                    buy.SetInteractable(false);
                    credits.SetText(CreditsText());
                });
                buy = builder.Last;
                builder.End();
            }
            builder.End();
        }

        private static bool TryPay(long price, CustomUIElement status)
        {
            if (PlayerStore.Instance.GetCash() < price)
            {
                status.SetText(Loc.Get("den_not_enough"));
                return false;
            }
            ModHelper.AddCredit(-(int)price);
            status.SetText("");
            return true;
        }

        private static void Deliver(GameItem item)
        {
            GeneralHelper.SetItemOwned(item, true);
            GraphUtils.EmporiumTryAdd(EmporiumEntry.Instance.afterhourInventory, item);
        }

        private static string PackLabel(CardSetDef set, PackStock pack) =>
            Loc.Get("den_pack_row", Loc.Get(pack.box ? "boosterbox_name" : "booster_name", Loc.SetName(set)), pack.count);

        private static string Price(long price) => price + GeneralHelper.CreditIcon();

        private static string CreditsText() => Loc.Get("den_credits", Price(PlayerStore.Instance.GetCash()));

        // ------------------------------------------------------------ stock

        private class Stock
        {
            public int day;
            public List<PackStock> packs = new List<PackStock>();
            public List<SingleStock> singles = new List<SingleStock>();
        }

        // Sealed product: booster packs, or a booster box when box is set.
        private class PackStock
        {
            public string set;
            public int count;
            public bool box;

            public string ItemId(CardSetDef s) => box ? CardItems.BoosterBoxId(s) : CardItems.BoosterId(s);
            public long ListPrice(CardSetDef s) => Math.Max(1L, (long)Math.Round((box ? s.boxValue : s.packValue) * LIST_PACK_MARKUP));
        }

        private class SingleStock
        {
            public string set;
            public int number;
            public float condition;
            public int uid;      // the copy's unique id: its wear in the viewer stays the same, bought or not
            public bool sold;
        }

        private static Stock LoadStock()
        {
            int today = StoreStation.Instance != null ? StoreStation.Instance.dayCounter : 0;
            Stock saved = null;
            try
            {
                saved = JsonConvert.DeserializeObject<Stock>(ModHelper.GetModData(ModEntry.MOD_ID, STOCK_KEY, "") ?? "");
            }
            catch (JsonException e)
            {
                ModEntry.Instance.Log.Warning($"Downside Den: saved stock unreadable ({e.Message}); restocking.");
            }
            if (saved == null || saved.day != today)
            {
                saved = RollStock(today);
                SaveStock(saved);
            }
            return saved;
        }

        private static void SaveStock(Stock s) =>
            ModHelper.SetModData(ModEntry.MOD_ID, STOCK_KEY, JsonConvert.SerializeObject(s));

        private static Stock RollStock(int day)
        {
            Stock s = new Stock { day = day };
            if (CardDatabase.Sets.Count == 0) return s;

            int packs = RNG.GetRandomInt(MIN_PACKS, MAX_PACKS);
            for (int i = 0; i < packs; i++)
            {
                CardSetDef set = RNG.GetRandomElement(CardDatabase.Sets);
                PackStock existing = s.packs.FirstOrDefault(p => p.set == set.id && !p.box);
                if (existing != null) existing.count++;
                else s.packs.Add(new PackStock { set = set.id, count = 1 });
            }
            if (RNG.Roll(BOX_CHANCE))
                s.packs.Add(new PackStock { set = RNG.GetRandomElement(CardDatabase.Sets).id, count = 1, box = true });

            int singles = RNG.GetRandomInt(MIN_SINGLES, MAX_SINGLES);
            for (int i = 0; i < singles; i++)
            {
                CardDef card = RollSingle();
                if (card == null) continue;
                s.singles.Add(new SingleStock
                {
                    set = card.set.id,
                    number = card.number,
                    condition = RollUsedCondition(),
                    uid = PlayerStore.Instance.GetUniqueID(),
                });
            }
            // rarest first, like a store's display case
            s.singles = s.singles.OrderByDescending(x => CardOf(x) == null ? 0 : CardItems.ValueFor(CardOf(x), x.condition, false)).ToList();
            return s;
        }

        // A rarity by shop weight, then a random card of that rarity from any set.
        private static CardDef RollSingle()
        {
            List<RarityDef> rarities = CardDatabase.Rarities.Values.Where(r => r.shopWeight > 0f).ToList();
            float total = rarities.Sum(r => r.shopWeight);
            float roll = RNG.GetRandomFloat(0f, total);
            string rarity = rarities.Count > 0 ? rarities[rarities.Count - 1].id : null;
            foreach (RarityDef r in rarities)
            {
                roll -= r.shopWeight;
                if (roll <= 0f) { rarity = r.id; break; }
            }
            List<CardDef> pool = CardDatabase.Sets.SelectMany(x => x.cards).Where(c => c.rarity == rarity).ToList();
            if (pool.Count == 0) pool = CardDatabase.Sets.SelectMany(x => x.cards).ToList();
            return pool.Count > 0 ? RNG.GetRandomElement(pool) : null;
        }

        // Second-hand cards: mostly decent, sometimes rough.
        private static float RollUsedCondition()
        {
            double u1 = 1.0 - RNG.random.NextDouble();
            double u2 = RNG.random.NextDouble();
            double gaussian = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            float condition = 10f - (float)Math.Abs(gaussian) * 2.4f;
            return (float)Math.Round(Math.Max(1f, Math.Min(10f, condition)), 2);
        }

        private static CardDef CardOf(SingleStock s) =>
            CardDatabase.SetsById.TryGetValue(s.set, out CardSetDef set) ? set.cards.FirstOrDefault(c => c.number == s.number) : null;

        // The exact copy on offer: same card, condition and unique id every time.
        private static GameItem MakeCard(SingleStock s, CardDef card)
        {
            GameItem item = DirectoryMaster.Item(CardItems.CardId(card));
            item.uniqueId = s.uid;
            CardItems.SetCondition(item, s.condition);
            return item;
        }
    }
}
