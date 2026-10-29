using System;
using System.Collections.Generic;
using System.Linq;

namespace ZamsCollectibles
{
    /// <summary>
    /// Item factories for cards, booster packs, the collector's box and complete sets.
    /// Per-copy data lives in tags (saved with the item); name, description and unitValue are
    /// also saved per item, so a factory only has to set the defaults and the behaviour.
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

        public static void Register(ModItemDirectory dir)
        {
            CardDatabase.EnsureLoaded();
            foreach (CardSetDef set in CardDatabase.Sets)
            {
                foreach (CardDef card in set.cards)
                    dir.Add(CardId(card), () => CreateCard(card));
                dir.Add(BoosterId(set), () => CreateBooster(set));
                dir.Add(BundleId(set), () => CreateBundle(set));
            }
            dir.Add(COLLECTOR_BOX_ID, CreateCollectorBox);
        }

        // ---------------------------------------------------------------- cards

        private static GameItem CreateCard(CardDef card)
        {
            GameItem item = ItemDirectory.CreateEmptyItem(CardId(card))
                .SetSpriteAndShapeFromMod(ModEntry.MOD_ID, card.set.cardSprite);
            item.name = CardName(card);
            item.shortDescription = CardText(card);
            item.flavorText = card.flavor;
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

        /// <summary>Sets a card's hidden condition, with the value and inventory icon that follow from it.</summary>
        public static void SetCondition(GameItem item, float condition)
        {
            CardDef card = GetCardDef(item);
            if (card == null) return;
            item.ModifyTag(CARD_CONDITION_FLOAT, s => s.Enable().SetFloat(condition));
            UpdateCardValue(item);

            // Worn cards get a worn icon (card_back_red_worn...); sets without the variants keep the plain one.
            string iconKey = card.set.cardSprite + CardCondition.IconSuffix(condition);
            if (!HasModSprite(iconKey)) iconKey = card.set.cardSprite;
            item.SetSpriteAndShapeFromMod(ModEntry.MOD_ID, iconKey);
        }

        public static bool HasModSprite(string key)
        {
            LoadedMod mod = ModLoader.Mods.Find(m => m.Manifest.ID == ModEntry.MOD_ID);
            return mod != null && mod.Sprites.ContainsKey(key);
        }

        /// <summary>"#01 - Rust Rat": the number padded to the set's size, at least two digits.</summary>
        public static string CardName(CardDef card) =>
            $"#{card.number.ToString().PadLeft(Math.Max(2, card.set.cards.Length.ToString().Length), '0')} - {card.name}";

        /// <summary>Type, cost, stats and rules in one block: the item description and the simple viewer.</summary>
        public static string CardText(CardDef card)
        {
            string stats = card.type == "Creature" ? $"{card.type} · Cost {card.cost} · {card.power}/{card.defense}" : $"{card.type} · Cost {card.cost}";
            return string.IsNullOrEmpty(card.text) ? stats : $"{stats}\n{card.text}";
        }

        /// <summary>The card face's type line ("Creature · 1/1"); the cost has its own gem.</summary>
        public static string TypeLine(CardDef card) =>
            card.type == "Creature" ? $"{card.type} · {card.power}/{card.defense}" : card.type;

        public static string RulesText(CardDef card) => card.text ?? "";

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
            float condition = GetCondition(item);
            float multiplier = IsGraded(item)
                ? CardCondition.GradedMultiplier(CardCondition.GradeOf(condition))
                : CardCondition.RawMultiplier(condition);
            item.SetValue(Math.Max(1L, (long)Math.Round(CardDatabase.BaseValue(card) * multiplier)));
        }

        /// <summary>Reveals the exact condition. Returns false when the card was already graded.</summary>
        public static bool Grade(GameItem item)
        {
            if (!IsCard(item) || IsGraded(item)) return false;
            item.EnableTag(CARD_GRADED_TAG);
            UpdateCardValue(item);
            return true;
        }

        // ---------------------------------------------------------------- booster packs

        private static GameItem CreateBooster(CardSetDef set)
        {
            GameItem item = ItemDirectory.CreateEmptyItem(BoosterId(set))
                .SetSpriteAndShapeFromMod(ModEntry.MOD_ID, string.IsNullOrEmpty(set.packSprite) ? "booster_pack" : set.packSprite);
            item.name = $"{set.name} Booster Pack";
            item.shortDescription = $"A sealed pack of {PackSize(set)} {set.name} cards. Activate to open it.";
            item.flavorText = "Sealed packs are worth more to collectors. Probably.";
            item.SetGameItemType(Collectibles.TYPE);
            item.ModifyTag(BOOSTER_SET_STRING, s => s.Enable().SetString(set.id));
            item.SetValue(set.packValue);
            item.canActivateSlotItemFunc = (s, _, _) => GeneralHelper.IsItemOwned(s);
            item.mayActivateSlotItemFunc = (s, _, _) => GeneralHelper.IsItemOwned(s);
            item.onActivateSlotItemFunc = (_, curInv, _) =>
            {
                Confirm($"Open the {set.name} booster pack?\nIt can't be resealed.", () =>
                {
                    item.Destroy();
                    foreach (PackSlotDef slot in set.packSlots)
                        for (int i = 0; i < slot.count; i++)
                            GraphUtils.EmporiumTryAdd(curInv, DirectoryMaster.Item(CardId(CardDatabase.RollCard(set, slot))));
                });
            };
            return item;
        }

        private static int PackSize(CardSetDef set) => set.packSlots.Sum(s => s.count);

        public static CardSetDef GetBoosterSet(GameItem item)
        {
            if (item == null || !item.IsTag(BOOSTER_SET_STRING)) return null;
            CardDatabase.SetsById.TryGetValue(item.GetTagReadonly(BOOSTER_SET_STRING).GetString() ?? "", out CardSetDef set);
            return set;
        }

        /// <summary>"3 Common, 1 Uncommon, 1 Rare or Ultra Rare"</summary>
        public static string PackContents(CardSetDef set) => string.Join(", ", set.packSlots.Select(slot =>
            $"{slot.count} {string.Join(" or ", slot.rarities.Select(r => CardDatabase.GetRarity(r)?.name ?? r))}"));

        // ---------------------------------------------------------------- collector's box and complete sets

        private const int BOX_GRID_WIDTH = 6;
        private const long BOX_VALUE = 10;

        /// <summary>
        /// A display box with its own card grid. Any cards can go in; the Seal Set button only
        /// works when it holds exactly one of each card of a single set.
        /// </summary>
        private static GameItem CreateCollectorBox()
        {
            int largestSet = CardDatabase.Sets.Count > 0 ? CardDatabase.Sets.Max(s => s.cards.Length) : BOX_GRID_WIDTH;
            GameGridInventory grid = new GameGridInventory(BOX_GRID_WIDTH, (largestSet + BOX_GRID_WIDTH - 1) / BOX_GRID_WIDTH);
            MachineryText status = MachineryText.Status("Set", "empty");
            GameItem item = null;
            MachineryButton seal = new MachineryButton("Seal Set", () => TrySeal(item, grid, status))
                .SetDescription("Seals the box when it holds exactly one of each card of a set.");

            PixelWindow window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.Panel("Cards", grid, "cards"),
                MachineHelper.Panel("Seal", MachineryStack.Vertical(status, seal), "seal"));
            // a loaded save refills the grid without the add callbacks below, so refresh on open too
            window.openCallback += () => RefreshStatus(grid, status);

            item = ItemDirectory.CreateEmptyItem(COLLECTOR_BOX_ID)
                .SetContentWindow(window)
                .SetSpriteAndShapeFromMod(ModEntry.MOD_ID, "collector_box");
            item.name = "Collector's Box";
            item.shortDescription = "A display box for one card set. Fill it with one of each card, then seal it: sealed sets sell for a little more than the loose cards.";
            item.SetGameItemType(Collectibles.TYPE);
            item.SetValue(BOX_VALUE);

            ContainerHelper.InitContainerItem(grid, item, allowedTags: new List<string> { CARD_TAG });
            ContainerHelper.SetBasicContainerValueChangeRule(item, grid);
            // the value rule owns the add/remove callbacks, so chain the status refresh onto them
            grid.onSlotAddItemEndFunc += (_, _, _) => RefreshStatus(grid, status);
            grid.onSlotRemoveItemEndFunc += (_, _, _) => RefreshStatus(grid, status);
            return item;
        }

        /// <summary>The set the box holds exactly, or null with the reason it can't be sealed.</summary>
        private static CardSetDef CheckContents(GameInventory grid, out string problem)
        {
            List<GameItem> cards = grid.childItems.Where(IsCard).ToList();
            problem = null;
            if (cards.Count == 0) { problem = "empty"; return null; }

            List<CardDef> defs = cards.Select(GetCardDef).ToList();
            if (defs.Any(d => d == null)) { problem = "unknown card"; return null; }

            CardSetDef set = defs[0].set;
            if (defs.Any(d => d.set != set)) { problem = "mixed sets"; return null; }

            CardDef duplicate = defs.GroupBy(d => d).Where(g => g.Count() > 1).Select(g => g.Key).FirstOrDefault();
            if (duplicate != null) { problem = $"two of #{duplicate.number:000}"; return null; }

            int missing = set.cards.Length - defs.Count;
            if (missing > 0) { problem = $"{set.code} {defs.Count}/{set.cards.Length}"; return null; }
            return set;
        }

        private static void RefreshStatus(GameInventory grid, MachineryText status)
        {
            CardSetDef set = CheckContents(grid, out string problem);
            status.SetStatus("Set", set != null ? $"{set.code} complete" : problem);
        }

        private static void TrySeal(GameItem box, GameInventory grid, MachineryText status)
        {
            if (box == null || !GeneralHelper.IsItemOwned(box)) return;
            CardSetDef set = CheckContents(grid, out string problem);
            if (set == null)
            {
                status.SetStatus("Can't seal", problem);
                return;
            }

            List<GameItem> cards = grid.childItems.Where(IsCard).ToList();
            long loose = cards.Sum(c => c.unitValue);
            long sealedValue = (long)Math.Round(loose * (1f + set.sealedMarkup));
            Confirm($"Seal the {set.name} set?\nThe cards can't be taken out again.\nLoose value {loose}, sealed value {sealedValue}.", () =>
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
            item.name = $"Sealed Set: {set.name}";
            item.shortDescription = $"All {set.cards.Length} cards of {set.name}, sealed in a display box.";
            item.SetGameItemType(Collectibles.TYPE);
            item.EnableTag(BUNDLE_TAG);
            // Real value is set when a box is sealed; this is the fallback for spawned copies.
            item.SetValue((long)Math.Round(set.cards.Sum(CardDatabase.BaseValue) * (1f + set.sealedMarkup)));
            return item;
        }

        // ---------------------------------------------------------------- shared

        private static void Confirm(string message, Action onConfirm)
        {
            GenericConfirmationUIManager.Instance.OpenPanel(message, "Yes", "No", () =>
            {
                GenericConfirmationUIManager.Instance.ClosePanel();
                onConfirm();
            }, () => GenericConfirmationUIManager.Instance.ClosePanel());
        }
    }
}
