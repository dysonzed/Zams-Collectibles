using System;
using System.Collections.Generic;
using System.Linq;

namespace ZamsCollectibles
{
    /// <summary>
    /// Card sleeves, and the wear that makes them worth buying. Every Aug Warriors game wears the
    /// cards in the player's deck box a little; a sleeved card (or a graded one, in its slab) takes
    /// no wear. Sleeves come in packs of 10 from the Downside Den: open the pack, put cards in its
    /// tray and press Sleeve. A sleeve stays on for good (the grader takes it off to slab a card).
    /// No art of their own: being sleeved is a tag, shown in the tooltip.
    /// </summary>
    public static class CardSleeves
    {
        public const string PACK_ID = ModEntry.MOD_ID + ".sleeves";
        public const string SLEEVED_TAG = "ZAMS_CARD_SLEEVED_TAG";
        private const string SLEEVES_LEFT_INT = "ZAMS_SLEEVES_LEFT_INT";

        public const int PER_PACK = 10;
        private const long PACK_VALUE = 8;                 // under a credit a sleeve: not worth it on a common
        private const int TRAY_COLUMNS = 5, TRAY_ROWS = 2;

        // Wear per game on an unsleeved card in the deck box: shuffling, handling, the table.
        private const float WEAR_MIN = 0.05f, WEAR_MAX = 0.25f;

        public static bool IsSleeved(GameItem item) => item != null && item.IsTag(SLEEVED_TAG);

        public static void Register(ModItemDirectory dir, Dictionary<string, Action<GameItem>> textByItemId)
        {
            dir.Add(PACK_ID, CreatePack);
            textByItemId[PACK_ID] = SetText;
        }

        private static int Left(GameItem pack) => pack.GetTagReadonly(SLEEVES_LEFT_INT).GetInt();

        private static GameItem CreatePack()
        {
            GameGridInventory tray = new GameGridInventory(TRAY_COLUMNS, TRAY_ROWS);
            MachineryText status = new MachineryText();
            GameItem item = null;
            MachineryButton sleeve = new MachineryButton(Loc.Get("sleeves_button"), () => Sleeve(item, tray, status))
                .SetDescription(Loc.Get("sleeves_button_desc"));

            PixelWindow window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.Panel(Loc.Get("sleeves_panel"), MachineryStack.Vertical(tray, status, sleeve), "sleeves"));
            item = ItemDirectory.CreateEmptyItem(PACK_ID)
                .SetContentWindow(window)
                .SetSpriteAndShapeFromMod(ModEntry.MOD_ID, "card_sleeves");
            item.ModifyTag(SLEEVES_LEFT_INT, s => s.Enable().SetInt(PER_PACK));
            SetText(item);
            item.SetGameItemType(Collectibles.TYPE);
            ContainerHelper.InitContainerItem(tray, item, allowedTags: new List<string> { CardItems.CARD_TAG });
            // only cards that can take a sleeve: not sleeved yet, not slabbed, not a match card
            tray.mayInventoryAddItemFunc = (card, _) => GeneralHelper.IsItemOwned(card) && CardItems.IsCard(card)
                && !IsSleeved(card) && !CardItems.IsGraded(card) && !AugWarriors.AugBoard.IsMatchCard(card);
            CardItems.TrackContainerValue(item, () => PackValue(item), tray);
            window.openCallback += () => Refresh(item, tray, status);
            tray.onSlotAddItemEndFunc += (_, _, _) => Refresh(item, tray, status);
            tray.onSlotRemoveItemEndFunc += (_, _, _) => Refresh(item, tray, status);
            // a used-up pack goes away once its tray is emptied and closed
            window.closeCallback += () => { if (Left(item) == 0 && tray.childItems.Count == 0 && item.parentInventory != null) item.Destroy(); };
            item.SetValue(PackValue(item));
            return item;
        }

        private static long PackValue(GameItem pack) => (long)Math.Round(PACK_VALUE * (double)Left(pack) / PER_PACK);

        private static void SetText(GameItem item)
        {
            item.name = Loc.Get("sleeves_name", Left(item));
            item.shortDescription = Loc.Get("sleeves_desc", PER_PACK);
        }

        private static void Refresh(GameItem pack, GameGridInventory tray, MachineryText status)
        {
            int waiting = tray.childItems.Count(c => !IsSleeved(c));
            status.SetText(Loc.Get("sleeves_status", Left(pack), waiting));
        }

        // Sleeves the tray's unsleeved cards, as many as there are sleeves left.
        private static void Sleeve(GameItem pack, GameGridInventory tray, MachineryText status)
        {
            int left = Left(pack);
            foreach (GameItem card in tray.childItems.Where(c => !IsSleeved(c)).Take(left).ToList())
            {
                card.EnableTag(SLEEVED_TAG);
                left--;
            }
            pack.ModifyTag(SLEEVES_LEFT_INT, s => s.SetInt(left));
            SetText(pack);
            pack.SetValue(PackValue(pack) + tray.childItems.Sum(c => c.unitValue * Math.Max(1, c.unitCount)));
            Refresh(pack, tray, status);
        }

        /// <summary>
        /// After a game: every card in the deck box that is neither sleeved nor slabbed loses a
        /// little condition (and with it some value). Returns how many cards took wear.
        /// </summary>
        public static int WearDeck(GameItem deckBox)
        {
            GameGridInventory grid = deckBox == null ? null : GraphUtils.FindChildType<GameGridInventory>(deckBox);
            if (grid == null) return 0;
            int worn = 0;
            foreach (GameItem card in grid.childItems.Where(CardItems.IsCard).ToList())
            {
                if (IsSleeved(card) || CardItems.IsGraded(card)) continue;
                float wear = WEAR_MIN + (float)RNG.random.NextDouble() * (WEAR_MAX - WEAR_MIN);
                float condition = Math.Max(1f, CardItems.GetCondition(card) - wear);
                CardItems.SetCondition(card, (float)Math.Round(condition, 2));
                worn++;
            }
            return worn;
        }
    }
}
