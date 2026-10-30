using System;
using System.Collections.Generic;
using System.Linq;

namespace ZamsCollectibles
{
    /// <summary>
    /// Item factories for cards, booster packs, the collector's box and sealed sets.
    /// Per-copy data lives in tags (saved with the item); name, description and unitValue are
    /// also saved per item, so a factory only has to set the defaults and the behaviour.
    /// Because the game saves the names and descriptions too, RefreshText re-applies them in
    /// the current language after a save loads.
    /// </summary>
    public static class CardItems
    {
        public const string CARD_TAG = "ZAMS_CARD_TAG";
        public const string CARD_SET_STRING = "ZAMS_CARD_SET_STRING";
        public const string CARD_NUMBER_INT = "ZAMS_CARD_NUMBER_INT";
        public const string CARD_CONDITION_FLOAT = "ZAMS_CARD_CONDITION_FLOAT"; // hidden until graded
        public const string CARD_GRADED_TAG = "ZAMS_CARD_GRADED_TAG";
        public const string BOOSTER_SET_STRING = "ZAMS_BOOSTER_SET_STRING";
        public const string BUNDLE_TAG = "ZAMS_SET_BUNDLE_TAG";
        public const string BUNDLE_AVG_CONDITION_FLOAT = "ZAMS_BUNDLE_AVG_CONDITION_FLOAT";

        public const string COLLECTOR_BOX_ID = ModEntry.MOD_ID + ".collector_box";

        public static string CardId(CardDef card) => $"{ModEntry.MOD_ID}.card.{card.set.id}.{card.number:000}";
        public static string BoosterId(CardSetDef set) => $"{ModEntry.MOD_ID}.booster.{set.id}";
        public static string BundleId(CardSetDef set) => $"{ModEntry.MOD_ID}.set.{set.id}";
        // Booster boxes are sold only at the Downside Den; packs there, and by chance from a pack
        // flipper customer or Dr. Jackson (see Progression). Keep them out of loot tables.
        public static string BoosterBoxId(CardSetDef set) => $"{ModEntry.MOD_ID}.boosterbox.{set.id}";
        public static string OpenedBoxId(CardSetDef set) => $"{ModEntry.MOD_ID}.boosterbox_open.{set.id}";

        // Item id -> sets that item's name and descriptions in the current language.
        private static readonly Dictionary<string, Action<GameItem>> textByItemId = new Dictionary<string, Action<GameItem>>();

        public static void Register(ModItemDirectory dir)
        {
            CardDatabase.EnsureLoaded();
            foreach (CardSetDef set in CardDatabase.Sets)
            {
                foreach (CardDef card in set.cards)
                {
                    dir.Add(CardId(card), () => CreateCard(card));
                    // on load, also bring the icon and value up to date (slabs, price changes)
                    textByItemId[CardId(card)] = item => { SetCardText(item, card); ApplyIcon(item); UpdateCardValue(item); };
                }
                dir.Add(BoosterId(set), () => CreateBooster(set));
                textByItemId[BoosterId(set)] = item => SetBoosterText(item, set);
                dir.Add(BundleId(set), () => CreateBundle(set));
                textByItemId[BundleId(set)] = item => SetBundleText(item, set);
                dir.Add(BoosterBoxId(set), () => CreateBoosterBox(set));
                textByItemId[BoosterBoxId(set)] = item => SetBoosterBoxText(item, set);
                dir.Add(OpenedBoxId(set), () => CreateOpenedBox(set));
                textByItemId[OpenedBoxId(set)] = item => SetOpenedBoxText(item, set);
            }
            dir.Add(COLLECTOR_BOX_ID, CreateCollectorBox);
            textByItemId[COLLECTOR_BOX_ID] = SetBoxText;
            CardBinder.Register(dir, textByItemId);
            AugWarriors.DeckBox.Register(dir, textByItemId);
            DeckOrders.Register(dir, textByItemId);
            DealerCertificate.Register(dir, textByItemId);
            CardSleeves.Register(dir, textByItemId);
        }

        /// <summary>Re-applies an item's name and descriptions in the current language; false if it isn't ours.</summary>
        public static bool RefreshText(GameItem item)
        {
            if (item?.identifier == null || !textByItemId.TryGetValue(item.identifier, out Action<GameItem> setText)) return false;
            setText(item);
            return true;
        }

        // ---------------------------------------------------------------- cards

        private static GameItem CreateCard(CardDef card)
        {
            GameItem item = ItemDirectory.CreateEmptyItem(CardId(card))
                .SetSpriteAndShapeFromMod(ModEntry.MOD_ID, card.set.cardSprite);
            SetCardText(item, card);
            item.SetGameItemType(Collectibles.TYPE);
            item.EnableTag(CARD_TAG);
            item.ModifyTag(CARD_SET_STRING, s => s.Enable().SetString(card.set.id));
            item.ModifyTag(CARD_NUMBER_INT, s => s.Enable().SetInt(card.number));

            // Activating a card (double-click by default) opens the card viewer.
            item.canActivateSlotItemFunc = (_, _, _) => true;
            item.mayActivateSlotItemFunc = (_, _, _) => true;
            item.onActivateSlotItemFunc = (s, _, _) => CardViewer.Open(s);

            // Rolled for every new copy. When a save loads, the saved tags, value and sprite replace these.
            SetCondition(item, CardCondition.RollFreshCondition());
            return item;
        }

        private static void SetCardText(GameItem item, CardDef card)
        {
            item.name = CardName(card);
            item.shortDescription = CardText(card);
            item.flavorText = Loc.CardFlavor(card);
        }

        /// <summary>Sets a card's hidden condition, with the value and inventory icon that follow from it.</summary>
        public static void SetCondition(GameItem item, float condition)
        {
            CardDef card = GetCardDef(item);
            if (card == null) return;
            item.ModifyTag(CARD_CONDITION_FLOAT, s => s.Enable().SetFloat(condition));
            UpdateCardValue(item);
            ApplyIcon(item);
        }

        /// <summary>
        /// The card's inventory icon: graded cards are sealed in a slab (card_back_red -> card_slab_red);
        /// otherwise worn cards get a worn icon (card_back_red_worn...). Sets without the variants keep the plain one.
        /// </summary>
        public static void ApplyIcon(GameItem item)
        {
            CardDef card = GetCardDef(item);
            if (card == null) return;
            string iconKey = IsGraded(item)
                ? card.set.cardSprite.Replace("card_back", "card_slab")
                : card.set.cardSprite + CardCondition.IconSuffix(GetCondition(item));
            if (!HasModSprite(iconKey)) iconKey = card.set.cardSprite;
            item.SetSpriteAndShapeFromMod(ModEntry.MOD_ID, iconKey);
        }

        public static bool HasModSprite(string key)
        {
            LoadedMod mod = ModLoader.Mods.Find(m => m.Manifest.ID == ModEntry.MOD_ID);
            return mod != null && mod.Sprites.ContainsKey(key);
        }

        /// <summary>The card number padded to the set's size, at least two digits ("01").</summary>
        public static string CardNumber(CardDef card) =>
            card.number.ToString().PadLeft(Math.Max(2, card.set.cards.Length.ToString().Length), '0');

        /// <summary>"#01 - Rust Rat"</summary>
        public static string CardName(CardDef card) => Loc.Get("card_name", CardNumber(card), Loc.CardTitle(card));

        /// <summary>Type, cost, stats and rules in one block: the item description and the plain viewer.</summary>
        public static string CardText(CardDef card)
        {
            string stats = StatLine(card, includePower: true);
            stats = string.IsNullOrEmpty(stats) ? Loc.CardType(card) : $"{Loc.CardType(card)} · {stats}";
            string rules = Loc.CardRules(card);
            return string.IsNullOrEmpty(rules) ? stats : $"{stats}\n{rules}";
        }

        /// <summary>"Power 5 · Glitch 3 · Stability +4". Mods show signed changes; zeroes are left out.</summary>
        public static string StatLine(CardDef card, bool includePower)
        {
            bool mod = card.kind == "mod";
            List<string> parts = new List<string>();
            if (includePower && (card.kind == "augment" || card.power != 0)) parts.Add(Loc.Get("stat_power", mod ? Signed(card.power) : card.power.ToString()));
            if (card.kind == "augment" || card.glitch != 0) parts.Add(Loc.Get("stat_glitch", mod ? Signed(card.glitch) : card.glitch.ToString()));
            if (card.stability != 0) parts.Add(Loc.Get("stat_stability", Signed(card.stability)));
            return string.Join(" · ", parts);
        }

        private static string Signed(int v) => v > 0 ? "+" + v : v < 0 ? "−" + (-v) : "0";

        /// <summary>What the card face's gem shows: an augment's Power, a mod's Power change, nothing for hacks.</summary>
        public static string GemText(CardDef card) =>
            card.kind == "augment" ? card.power.ToString() : card.kind == "mod" && card.power != 0 ? Signed(card.power) : "";

        /// <summary>The card face's type line ("Creature · 1/1"); the cost has its own gem.</summary>
        public static string TypeLine(CardDef card) =>
            string.IsNullOrEmpty(StatLine(card, includePower: false)) ? Loc.CardType(card) : $"{Loc.CardType(card)} · {StatLine(card, includePower: false)}";

        /// <summary>"SCR 001/012"</summary>
        public static string SetNumber(CardDef card) => $"{card.set.code} {card.number:000}/{card.set.cards.Length:000}";

        public static bool IsCard(GameItem item) => item != null && item.IsTag(CARD_TAG);

        public static CardDef GetCardDef(GameItem item)
        {
            if (!IsCard(item)) return null;
            CardDatabase.SetsById.TryGetValue(item.GetTagReadonly(CARD_SET_STRING).GetString() ?? "", out CardSetDef set);
            int number = item.GetTagReadonly(CARD_NUMBER_INT).GetInt();
            return set?.cards.FirstOrDefault(c => c.number == number);
        }

        public static float GetCondition(GameItem item) => item.GetTagReadonly(CARD_CONDITION_FLOAT).GetFloat();

        public static bool IsGraded(GameItem item) => item.IsTag(CARD_GRADED_TAG);

        public static void UpdateCardValue(GameItem item)
        {
            CardDef card = GetCardDef(item);
            if (card == null) return;
            item.SetValue(ValueFor(card, GetCondition(item), IsGraded(item)));
        }

        public static long ValueFor(CardDef card, float condition, bool graded)
        {
            float grade = CardCondition.GradeOf(condition);
            float multiplier = graded ? CardCondition.GradedMultiplier(grade) : CardCondition.RawMultiplier(condition);
            long premium = graded ? CardCondition.GradePremium(grade) : 0;
            return Math.Max(1L, (long)Math.Round(CardDatabase.BaseValue(card) * multiplier) + premium);
        }

        /// <summary>Reveals the exact condition. Returns false when the card was already graded.</summary>
        public static bool Grade(GameItem item)
        {
            if (!IsCard(item) || IsGraded(item)) return false;
            item.EnableTag(CARD_GRADED_TAG);
            UpdateCardValue(item);
            ApplyIcon(item);
            return true;
        }

        // ---------------------------------------------------------------- booster packs

        private static GameItem CreateBooster(CardSetDef set)
        {
            GameItem item = ItemDirectory.CreateEmptyItem(BoosterId(set))
                .SetSpriteAndShapeFromMod(ModEntry.MOD_ID, string.IsNullOrEmpty(set.packSprite) ? "booster_pack" : set.packSprite);
            SetBoosterText(item, set);
            item.SetGameItemType(Collectibles.TYPE);
            item.ModifyTag(BOOSTER_SET_STRING, s => s.Enable().SetString(set.id));
            item.SetValue(set.packValue);
            item.canActivateSlotItemFunc = (s, _, _) => GeneralHelper.IsItemOwned(s);
            item.mayActivateSlotItemFunc = (s, _, _) => GeneralHelper.IsItemOwned(s);
            item.onActivateSlotItemFunc = (_, curInv, _) =>
            {
                item.Destroy();
                foreach (PackSlotDef slot in set.packSlots)
                    for (int i = 0; i < slot.count; i++)
                        GraphUtils.EmporiumTryAdd(curInv, DirectoryMaster.Item(CardId(CardDatabase.RollCard(set, slot))));
            };
            return item;
        }

        private static void SetBoosterText(GameItem item, CardSetDef set)
        {
            item.name = Loc.Get("booster_name", Loc.SetName(set));
            item.shortDescription = Loc.Get("booster_desc", PackSize(set), Loc.SetName(set));
            item.flavorText = Loc.Get("booster_flavor");
        }

        private static int PackSize(CardSetDef set) => set.packSlots.Sum(s => s.count);

        public static CardSetDef GetBoosterSet(GameItem item)
        {
            if (item == null || !item.IsTag(BOOSTER_SET_STRING)) return null;
            CardDatabase.SetsById.TryGetValue(item.GetTagReadonly(BOOSTER_SET_STRING).GetString() ?? "", out CardSetDef set);
            return set;
        }

        /// <summary>"3 Common, 1 Uncommon, 1 Rare or Ultra Rare"</summary>
        public static string PackContents(CardSetDef set) => string.Join(Loc.Get("list_separator"), set.packSlots.Select(slot =>
            Loc.Get("pack_slot", slot.count, string.Join(Loc.Get("list_or"), slot.rarities.Select(Loc.RarityName)))));

        // ---------------------------------------------------------------- booster boxes

        // Packs are 1 x 2 cells, so the opened box is two rows of nine.
        private const int OPENED_BOX_COLUMNS = 9;
        private const long OPENED_BOX_VALUE = 2;

        public static int BoxPacks(CardSetDef set) => set.boxPacks > 0 ? set.boxPacks : 18;

        private static string BoxSprite(CardSetDef set, bool opened)
        {
            // the set's own box art (booster_box_rustwave), else one by frame colour, else the purple one
            string open = opened ? "_open" : "";
            foreach (string key in new[] { $"booster_box{open}_{set.id}", $"booster_box{open}_{set.frameColor}" })
                if (HasModSprite(key)) return key;
            return opened ? "booster_box_open_purple" : "booster_box_purple";
        }

        /// <summary>A sealed booster box: activating it (after a confirmation) turns it into an opened box full of packs.</summary>
        private static GameItem CreateBoosterBox(CardSetDef set)
        {
            GameItem item = ItemDirectory.CreateEmptyItem(BoosterBoxId(set))
                .SetSpriteAndShapeFromMod(ModEntry.MOD_ID, BoxSprite(set, false));
            SetBoosterBoxText(item, set);
            item.SetGameItemType(Collectibles.TYPE);
            item.ModifyTag(BOOSTER_SET_STRING, s => s.Enable().SetString(set.id));
            item.SetValue(set.boxValue > 0 ? set.boxValue : set.packValue * BoxPacks(set));
            item.canActivateSlotItemFunc = (s, _, _) => GeneralHelper.IsItemOwned(s);
            item.mayActivateSlotItemFunc = (s, _, _) => GeneralHelper.IsItemOwned(s);
            item.onActivateSlotItemFunc = (_, curInv, _) =>
            {
                Confirm(Loc.Get("boosterbox_open_confirm", Loc.SetName(set), BoxPacks(set)), () =>
                {
                    item.Destroy();
                    GameItem opened = DirectoryMaster.Item(OpenedBoxId(set));
                    GameGridInventory grid = GraphUtils.FindChildType<GameGridInventory>(opened);
                    for (int i = 0; i < BoxPacks(set); i++)
                    {
                        GameItem pack = DirectoryMaster.Item(BoosterId(set));
                        if (grid == null || GraphUtils.TryAcceptAll(grid, pack) > 0) GraphUtils.EmporiumTryAdd(curInv, pack);
                    }
                    GraphUtils.EmporiumTryAdd(curInv, opened);
                });
            };
            return item;
        }

        private static void SetBoosterBoxText(GameItem item, CardSetDef set)
        {
            item.name = Loc.Get("boosterbox_name", Loc.SetName(set));
            item.shortDescription = Loc.Get("boosterbox_desc", BoxPacks(set), Loc.SetName(set));
            item.flavorText = Loc.Get("boosterbox_flavor");
        }

        /// <summary>
        /// The opened box: a container holding only this set's packs, worth its packs plus a
        /// little cardboard. It is an ordinary owned item, so it can go in the trash once empty.
        /// </summary>
        private static GameItem CreateOpenedBox(CardSetDef set)
        {
            int rows = 2 * ((BoxPacks(set) + OPENED_BOX_COLUMNS - 1) / OPENED_BOX_COLUMNS);
            (PixelWindow window, GameGridInventory grid) = DirectoryUtils.CreateGridWindow(OPENED_BOX_COLUMNS, rows);
            GameItem item = ItemDirectory.CreateEmptyItem(OpenedBoxId(set))
                .SetContentWindow(window)
                .SetSpriteAndShapeFromMod(ModEntry.MOD_ID, BoxSprite(set, true));
            SetOpenedBoxText(item, set);
            item.SetGameItemType(Collectibles.TYPE);
            item.SetValue(OPENED_BOX_VALUE);
            // An empty tag list matters: with no tag list at all the game lets anything in.
            ContainerHelper.InitContainerItem(grid, item, allowedTags: new List<string>(), allowedId: new List<string> { BoosterId(set) });
            // Emptied boxes go away when their window is closed (not the moment the last pack
            // leaves: a cancelled drag of that pack would have nowhere to return to).
            window.closeCallback += () => { if (grid.childItems.Count == 0 && item.parentInventory != null) item.Destroy(); };
            TrackContainerValue(item, OPENED_BOX_VALUE, grid);
            return item;
        }

        private static void SetOpenedBoxText(GameItem item, CardSetDef set)
        {
            item.name = Loc.Get("boosterbox_opened_name", Loc.SetName(set));
            item.shortDescription = Loc.Get("boosterbox_opened_desc", BoxPacks(set), Loc.SetName(set));
        }

        // ---------------------------------------------------------------- collector's box and sealed sets

        private const int BOX_GRID_WIDTH = 6;
        private const long BOX_VALUE = 10;

        /// <summary>
        /// A display box with its own card grid. It opens with one slot and grows a slot each time
        /// one is filled, up to the size of the set being collected. The Seal Set button works when
        /// it holds one of each card of a single set; the ultra rare is optional, and sealing with
        /// it makes a Master Set, worth more.
        /// </summary>
        private static GameItem CreateCollectorBox()
        {
            int largestSet = CardDatabase.Sets.Count > 0 ? CardDatabase.Sets.Max(s => s.cards.Length) : BOX_GRID_WIDTH;
            int rows = (largestSet + BOX_GRID_WIDTH - 1) / BOX_GRID_WIDTH;
            GameGridInventory grid = new GameGridInventory(BOX_GRID_WIDTH, rows);
            Grow(grid, rows);
            MachineryText status = MachineryText.Status(Loc.Get("box_status"), Loc.Get("box_status_empty"));
            GameItem item = null;
            MachineryButton seal = new MachineryButton(Loc.Get("box_seal_button"), () => TrySeal(item, grid, status))
                .SetDescription(Loc.Get("box_seal_button_desc"));

            PixelWindow window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.Panel(Loc.Get("box_panel_cards"), grid, "cards"),
                MachineHelper.Panel(Loc.Get("box_panel_seal"), MachineryStack.Vertical(status, seal), "seal"));
            // a loaded save refills the grid without the add callbacks below, so refresh on open too
            window.openCallback += () => { Grow(grid, rows); RefreshStatus(grid, status); };

            item = ItemDirectory.CreateEmptyItem(COLLECTOR_BOX_ID)
                .SetContentWindow(window)
                .SetSpriteAndShapeFromMod(ModEntry.MOD_ID, "collector_box");
            SetBoxText(item);
            item.SetGameItemType(Collectibles.TYPE);
            item.SetValue(BOX_VALUE);

            ContainerHelper.InitContainerItem(grid, item, allowedTags: new List<string> { CARD_TAG });
            TrackContainerValue(item, BOX_VALUE, grid);
            grid.onSlotAddItemEndFunc += (_, _, _) => { Grow(grid, rows); RefreshStatus(grid, status); };
            grid.onSlotRemoveItemEndFunc += (_, _, _) => { Grow(grid, rows); RefreshStatus(grid, status); };
            return item;
        }

        /// <summary>
        /// Opens one more slot than the box has cards, in reading order, never closing a slot that
        /// holds a card; once the set is known, no more than that set has cards.
        /// </summary>
        private static void Grow(GameGridInventory grid, int rows)
        {
            int cells = BOX_GRID_WIDTH * rows;
            List<GameItem> cards = grid.childItems.Where(IsCard).ToList();
            int highest = -1;
            foreach (GameItem card in cards)
            {
                GridShape at = grid.GetIncompleteItemInventorySlot(card)?.itemGridShape;
                if (at != null) highest = Math.Max(highest, at.minY * BOX_GRID_WIDTH + at.minX);
            }
            CardSetDef set = cards.Select(GetCardDef).FirstOrDefault(d => d != null)?.set;
            int limit = set != null ? Math.Min(cells, set.cards.Length) : cells;
            int open = Math.Min(cells, Math.Max(highest + 1, Math.Min(limit, cards.Count + 1)));
            open = Math.Max(1, open);
            grid.SetShape(new string('0', open) + new string('1', cells - open), BOX_GRID_WIDTH);
        }

        private static void SetBoxText(GameItem item)
        {
            item.name = Loc.Get("box_name");
            item.shortDescription = Loc.Get("box_desc");
        }

        /// <summary>The set the box holds exactly, or null with the reason it can't be sealed.</summary>
        private static CardSetDef CheckContents(GameInventory grid, out string problem)
        {
            List<GameItem> cards = grid.childItems.Where(IsCard).ToList();
            problem = null;
            if (cards.Count == 0) { problem = Loc.Get("box_status_empty"); return null; }

            List<CardDef> defs = cards.Select(GetCardDef).ToList();
            if (defs.Any(d => d == null)) { problem = Loc.Get("box_status_unknown"); return null; }

            CardSetDef set = defs[0].set;
            if (defs.Any(d => d.set != set)) { problem = Loc.Get("box_status_mixed"); return null; }

            CardDef duplicate = defs.GroupBy(d => d).Where(g => g.Count() > 1).Select(g => g.Key).FirstOrDefault();
            if (duplicate != null) { problem = Loc.Get("box_status_duplicate", CardNumber(duplicate)); return null; }

            int needed = set.cards.Count(c => !IsUltra(c));
            int have = defs.Count(d => !IsUltra(d));
            if (have < needed) { problem = Loc.Get("box_status_progress", set.code, have, needed); return null; }
            return set;
        }

        private static bool IsUltra(CardDef card) => card.rarity == "ultra";

        // A complete set with its ultra rare is a Master Set: double the sealed bonus.
        private static bool IsMaster(GameInventory grid) => grid.childItems.Where(IsCard).Select(GetCardDef).Any(d => d != null && IsUltra(d));

        private static void RefreshStatus(GameInventory grid, MachineryText status)
        {
            CardSetDef set = CheckContents(grid, out string problem);
            status.SetStatus(Loc.Get("box_status"), set == null ? problem
                : IsMaster(grid) ? Loc.Get("box_status_master", set.code) : Loc.Get("box_status_complete", set.code));
        }

        private static void TrySeal(GameItem box, GameInventory grid, MachineryText status)
        {
            if (box == null || !GeneralHelper.IsItemOwned(box)) return;
            CardSetDef set = CheckContents(grid, out string problem);
            if (set == null)
            {
                status.SetStatus(Loc.Get("box_status_cant_seal"), problem);
                return;
            }

            List<GameItem> cards = grid.childItems.Where(IsCard).ToList();
            long loose = cards.Sum(c => c.unitValue);
            long sealedValue = (long)Math.Round(loose * (1f + set.sealedMarkup * (IsMaster(grid) ? 2f : 1f)));
            Confirm(Loc.Get("box_seal_confirm", Loc.SetName(set), loose, sealedValue), () =>
            {
                float avgCondition = cards.Average(GetCondition);
                GameInventory parent = box.parentInventory;
                cards.ForEach(c => c.Destroy());
                box.Destroy();

                GameItem sealedSet = DirectoryMaster.Item(BundleId(set));
                sealedSet.SetValue(sealedValue);
                sealedSet.ModifyTag(BUNDLE_AVG_CONDITION_FLOAT, s => s.Enable().SetFloat(avgCondition));
                if (parent != null) GraphUtils.EmporiumTryAdd(parent, sealedSet);
                else GraphUtils.EmporiumTryAdd(sealedSet);
            });
        }

        private static GameItem CreateBundle(CardSetDef set)
        {
            GameItem item = ItemDirectory.CreateEmptyItem(BundleId(set))
                .SetSpriteAndShapeFromMod(ModEntry.MOD_ID, "complete_set");
            SetBundleText(item, set);
            item.SetGameItemType(Collectibles.TYPE);
            item.EnableTag(BUNDLE_TAG);
            // Real value is set when a box is sealed; this is the fallback for spawned copies.
            item.SetValue((long)Math.Round(set.cards.Where(c => !IsUltra(c)).Sum(CardDatabase.BaseValue) * (1f + set.sealedMarkup)));
            return item;
        }

        private static void SetBundleText(GameItem item, CardSetDef set)
        {
            item.name = Loc.Get("sealed_name", Loc.SetName(set));
            item.shortDescription = Loc.Get("sealed_desc", set.cards.Length, Loc.SetName(set));
        }

        // ---------------------------------------------------------------- shared

        /// <summary>
        /// Keeps a container's value at its own value plus everything inside it. Recomputed from
        /// scratch on every change: the game re-adds the contents when a save loads, so adding
        /// and subtracting deltas (ContainerHelper's value rule) would count them twice.
        /// </summary>
        public static void TrackContainerValue(GameItem container, long ownValue, params GameGridInventory[] grids) =>
            TrackContainerValue(container, () => ownValue, grids);

        /// <summary>As above, with the container's own value read when it changes (e.g. from a tag).</summary>
        public static void TrackContainerValue(GameItem container, Func<long> ownValue, params GameGridInventory[] grids)
        {
            void Recompute(GameItem _, GameInventory __, SlotMarker ___) =>
                container.SetValue(ownValue() + grids.Sum(g => g.childItems.Sum(c => c.unitValue * Math.Max(1, c.unitCount))));
            foreach (GameGridInventory grid in grids)
            {
                grid.onSlotAddItemEndFunc += Recompute;
                grid.onSlotRemoveItemEndFunc += Recompute;
            }
        }


        // Yes / No come from the game's own strings, already translated.
        private static void Confirm(string message, Action onConfirm)
        {
            GenericConfirmationUIManager.Instance.OpenPanel(message, LocHelper.GetLocalizedMainMenu("yes"), LocHelper.GetLocalizedMainMenu("no"), () =>
            {
                GenericConfirmationUIManager.Instance.ClosePanel();
                onConfirm();
            }, () => GenericConfirmationUIManager.Instance.ClosePanel());
        }
    }
}
