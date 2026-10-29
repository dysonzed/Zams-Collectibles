using System;
using System.Collections.Generic;
using System.Linq;

namespace ZamsCollectibles
{
    /// <summary>
    /// A collection binder: a cheap container for single cards, laid out like a real binder.
    /// Each tab is a two-page spread of 3 x 3 pockets (a 6 x 3 grid); sort buttons re-sort the
    /// whole binder, filling the spreads in order.
    /// The pages are created in the same order every time, which is the order the game saves
    /// and restores their contents in: keep it that way once released (add pages at the end).
    /// </summary>
    public static class CardBinder
    {
        public const string BINDER_ID = ModEntry.MOD_ID + ".binder";

        private const int SPREADS = 8;
        private const int SPREAD_COLUMNS = 6;   // two pages of three pockets
        private const int SPREAD_ROWS = 3;
        private const long BINDER_VALUE = 12;

        public static void Register(ModItemDirectory dir, Dictionary<string, Action<GameItem>> textByItemId)
        {
            dir.Add(BINDER_ID, CreateBinder);
            textByItemId[BINDER_ID] = SetText;
        }

        private static GameItem CreateBinder()
        {
            GameItem item = null;
            MachineryTabs tabs = new MachineryTabs();
            GameGridInventory[] pages = new GameGridInventory[SPREADS];
            for (int i = 0; i < SPREADS; i++)
            {
                pages[i] = new GameGridInventory(SPREAD_COLUMNS, SPREAD_ROWS).SetMachineryStyle();
                tabs.AddTab(Loc.Get("binder_tab", i + 1), MachineHelper.WrapInIntake(pages[i]));
            }

            MachineryStack sortButtons = MachineryStack.Vertical(
                SortButton("binder_sort_number", pages, ByNumber),
                SortButton("binder_sort_rarity", pages, ByRarity),
                SortButton("binder_sort_value", pages, ByValue),
                SortButton("binder_sort_condition", pages, ByCondition));

            PixelWindow window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.Panel(Loc.Get("binder_panel_pages"), tabs, "pages"),
                MachineHelper.Panel(Loc.Get("binder_panel_sort"), sortButtons, "sort"));

            item = ItemDirectory.CreateEmptyItem(BINDER_ID)
                .SetContentWindow(window)
                .SetSpriteAndShapeFromMod(ModEntry.MOD_ID, "card_binder");
            SetText(item);
            item.SetGameItemType(Collectibles.TYPE);
            item.SetValue(BINDER_VALUE);
            foreach (GameGridInventory page in pages)
                ContainerHelper.InitContainerItem(page, item, allowedTags: new List<string> { CardItems.CARD_TAG });
            CardItems.TrackContainerValue(item, BINDER_VALUE, pages);
            return item;
        }

        private static void SetText(GameItem item)
        {
            item.name = Loc.Get("binder_name");
            item.shortDescription = Loc.Get("binder_desc", SPREADS * SPREAD_COLUMNS * SPREAD_ROWS);
        }

        private static MachineryButton SortButton(string key, GameGridInventory[] pages, Comparison<GameItem> order) =>
            new MachineryButton(Loc.Get(key), () => Sort(pages, order)).SetDescription(Loc.Get(key + "_desc"));

        /// <summary>Takes every card out of the binder and puts them back in order, page by page.</summary>
        private static void Sort(GameGridInventory[] pages, Comparison<GameItem> order)
        {
            List<GameItem> cards = new List<GameItem>();
            foreach (GameGridInventory page in pages)
            {
                foreach (GameItem card in page.childItems.ToList())
                {
                    page.Expel(card);
                    cards.Add(card);
                }
            }
            cards.Sort(order);

            int p = 0;
            foreach (GameItem card in cards)
            {
                while (p < pages.Length && GraphUtils.TryAcceptAll(pages[p], card) > 0) p++;
                if (p >= pages.Length) GraphUtils.EmporiumTryAdd(card);   // can't happen with the same cards, but never lose one
            }
        }

        // ------------------------------------------------------------ orders

        // Set, then card number, then best condition first.
        private static int ByNumber(GameItem a, GameItem b)
        {
            CardDef ca = CardItems.GetCardDef(a), cb = CardItems.GetCardDef(b);
            if (ca == null || cb == null) return (ca == null).CompareTo(cb == null);
            int set = CardDatabase.Sets.IndexOf(ca.set).CompareTo(CardDatabase.Sets.IndexOf(cb.set));
            if (set != 0) return set;
            int number = ca.number.CompareTo(cb.number);
            return number != 0 ? number : CardItems.GetCondition(b).CompareTo(CardItems.GetCondition(a));
        }

        // Rarest first, then by number.
        private static int ByRarity(GameItem a, GameItem b)
        {
            int rarity = RarityRank(b).CompareTo(RarityRank(a));
            return rarity != 0 ? rarity : ByNumber(a, b);
        }

        // Most valuable first.
        private static int ByValue(GameItem a, GameItem b)
        {
            int value = b.unitValue.CompareTo(a.unitValue);
            return value != 0 ? value : ByNumber(a, b);
        }

        // Best condition first.
        private static int ByCondition(GameItem a, GameItem b)
        {
            int condition = CardItems.GetCondition(b).CompareTo(CardItems.GetCondition(a));
            return condition != 0 ? condition : ByNumber(a, b);
        }

        // Rarities rank by their order in sets.json (common first).
        private static int RarityRank(GameItem item)
        {
            CardDef card = CardItems.GetCardDef(item);
            if (card == null) return -1;
            int i = 0;
            foreach (string id in CardDatabase.Rarities.Keys)
            {
                if (id == card.rarity) return i;
                i++;
            }
            return -1;
        }
    }
}
