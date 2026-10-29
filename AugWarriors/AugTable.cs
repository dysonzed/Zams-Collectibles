using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZamsCollectibles.AugWarriors
{
    /// <summary>
    /// The Aug Warriors table at the Downside Den: a machine-style window with real item slots.
    /// The player drops their deck box in the deck slot and, optionally, a card in the ante
    /// slot, picks a credit ante and an opponent, and plays.
    ///
    /// Card ante: win and you keep your card and win one of the same rarity (sometimes one
    /// rarity up, and a quarter of the time one you don't own yet); lose or concede and the
    /// opponent keeps it; a draw returns it.
    ///
    /// The window belongs to no item, so its slots are not saved: whatever is in them goes
    /// back to the player when the table closes or they leave the Den.
    /// </summary>
    public static class AugTable
    {
        private static readonly int[] CreditAntes = { 0, 10, 25, 50 };
        private const int RARITY_UP_CHANCE = 15;    // percent: the won card is one rarity up
        private const int NEW_CARD_CHANCE = 25;     // percent: the won card is one the player doesn't own

        // The regulars at the Den. Names are proper nouns; their blurbs are localized.
        private static readonly (string name, AugPersonality style)[] Regulars =
        {
            ("Rook", AugPersonality.Cautious),
            ("Vex", AugPersonality.Reckless),
            ("Patch", AugPersonality.Hacker),
        };

        private static PixelWindow window;
        private static GameSlotInventory deckSlot, anteSlot;
        private static MachineryText deckText, anteText, opponentText, statusText;
        private static MachineryButton playButton;
        private static int regular = 1, creditAnte;
        private static bool inMatch;

        public static void Open()
        {
            if (window == null || window.handler == null) Build();   // the UI goes with the scene: rebuild after a reload
            Refresh();
            window.Show(new Vector2(Screen.width * 0.5f, Screen.height * 0.72f), Vector2.zero);
        }

        /// <summary>Gives back whatever is in the slots and closes the table (leaving the Den).</summary>
        public static void Close()
        {
            ReturnItems();
            window?.Hide();
        }

        private static void Build()
        {
            deckSlot = new GameSlotInventory();
            deckSlot.mayInventoryAddItemFunc = (item, _) => GeneralHelper.IsItemOwned(item) && DeckBox.IsDeckBox(item) && !inMatch;
            deckSlot.mayInventoryRemoveItemFunc = (_, _) => !inMatch;
            deckSlot.onSlotAddItemEndFunc += (_, _, _) => Refresh();
            deckSlot.onSlotRemoveItemEndFunc += (_, _, _) => Refresh();

            anteSlot = new GameSlotInventory();
            anteSlot.mayInventoryAddItemFunc = (item, _) => GeneralHelper.IsItemOwned(item) && CardItems.IsCard(item) && !inMatch;
            anteSlot.mayInventoryRemoveItemFunc = (_, _) => !inMatch;
            anteSlot.onSlotAddItemEndFunc += (_, _, _) => Refresh();
            anteSlot.onSlotRemoveItemEndFunc += (_, _, _) => Refresh();

            deckText = new MachineryText();
            anteText = new MachineryText();
            opponentText = new MachineryText();
            statusText = new MachineryText();

            MachineryRadioList credits = new MachineryRadioList(
                CreditAntes.Select(a => a == 0 ? Loc.Get("aug_no_stakes") : a + GeneralHelper.CreditIcon()).ToList(), 0,
                i => { creditAnte = CreditAntes[i]; Refresh(); });
            MachineryRadioList opponents = new MachineryRadioList(Regulars.Select(r => r.name).ToList(), regular,
                i => { regular = i; Refresh(); });
            playButton = new MachineryButton(Loc.Get("aug_challenge_start"), Play);
            MachineryButton rules = new MachineryButton(Loc.Get("aug_table_rules"), ShowRules);

            window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.Panel(Loc.Get("aug_table_deck"), MachineryStack.Vertical(deckSlot, deckText), "deck"),
                MachineHelper.Panel(Loc.Get("aug_challenge_ante"), MachineryStack.Vertical(anteSlot, anteText, credits), "ante"),
                MachineHelper.Panel(Loc.Get("aug_challenge_opponent"), MachineryStack.Vertical(opponents, opponentText), "opponent"),
                MachineHelper.Panel(Loc.Get("aug_table_play"), MachineryStack.Vertical(playButton, rules, statusText), "play"));
            window.SetTitle(Loc.Get("aug_challenge_title"));
            window.closeCallback += ReturnItems;
        }

        private static void Refresh()
        {
            if (window == null) return;
            List<CardDef> deck = DeckBox.Cards(deckSlot.childItem);
            List<AugEvent> errors = AugRules.DeckErrors(deck);
            deckText.SetText(deckSlot.childItem == null ? Loc.Get("aug_table_no_deck")
                : errors.Count == 0 ? Loc.Get("deckbox_status_ok", deck.Count)
                : Loc.Get("deckbox_status_errors", deck.Count, errors.Count));

            CardDef ante = CardItems.GetCardDef(anteSlot.childItem);
            anteText.SetText(ante == null ? Loc.Get("aug_table_no_card") : Loc.Get("aug_table_card_ante", Loc.RarityName(ante.rarity)));
            opponentText.SetText(Loc.Get("aug_regular_" + Regulars[regular].style.id));

            string problem = deckSlot.childItem == null ? Loc.Get("aug_table_need_deck")
                : errors.Count > 0 ? Loc.Get("aug_table_fix_deck")
                : PlayerStore.Instance.GetCash() < creditAnte ? Loc.Get("den_not_enough")
                : null;
            statusText.SetText(problem ?? Loc.Get("aug_table_ready"));
            if (problem == null) playButton.Enable(); else playButton.Disable();
        }

        private static void Play()
        {
            List<CardDef> deck = DeckBox.Cards(deckSlot.childItem);
            if (inMatch || !AugRules.IsLegalDeck(deck) || PlayerStore.Instance.GetCash() < creditAnte) return;
            inMatch = true;
            window.Hide();
            var (name, style) = Regulars[regular];
            AugMatchWindow.Start(deck, name, style, creditAnte, Finished);
        }

        // winner: 0 the player, 1 the opponent, null a draw. Settles the card ante and brings the table back.
        private static void Finished(int? winner)
        {
            inMatch = false;
            GameItem anted = anteSlot.childItem;
            CardDef anteCard = CardItems.GetCardDef(anted);
            if (anteCard != null && winner == 1)
            {
                anted.Destroy();
                ModEntry.Instance.Log.Log($"Aug Warriors: lost {anteCard.name} on the ante.");
            }
            else if (anteCard != null && winner == 0)
            {
                GameItem prize = PrizeCard(anteCard);
                if (prize != null) GraphUtils.EmporiumTryAdd(EmporiumEntry.Instance.afterhourInventory, prize);
            }
            Open();
        }

        /// <summary>A card of the anted card's rarity, sometimes one up, a quarter of the time one the player doesn't own.</summary>
        private static GameItem PrizeCard(CardDef anted)
        {
            List<string> order = CardDatabase.Rarities.Keys.ToList();
            int rank = Math.Max(0, order.IndexOf(anted.rarity));
            if (RNG.Roll(RARITY_UP_CHANCE) && rank < order.Count - 1) rank++;
            string rarity = order[rank];

            List<CardDef> pool = CardDatabase.Sets.SelectMany(s => s.cards).Where(c => c.rarity == rarity).ToList();
            if (pool.Count == 0) return null;
            if (RNG.Roll(NEW_CARD_CHANCE))
            {
                HashSet<string> owned = new HashSet<string>(EmporiumEntry.Instance.GetAllOwnedItems().Where(CardItems.IsCard).Select(i => i.identifier));
                List<CardDef> missing = pool.Where(c => !owned.Contains(CardItems.CardId(c))).ToList();
                if (missing.Count > 0) pool = missing;
            }
            GameItem prize = DirectoryMaster.Item(CardItems.CardId(RNG.GetRandomElement(pool)));
            ModEntry.Instance.Log.Log($"Aug Warriors: won {prize.name} on the ante.");
            return prize;
        }

        // Anything left in the slots goes back to the player (their bag, or wherever the store has room).
        private static void ReturnItems()
        {
            if (inMatch) return;
            foreach (GameSlotInventory slot in new[] { deckSlot, anteSlot })
            {
                GameItem item = slot?.childItem;
                if (item == null) continue;
                slot.Expel(item);
                GraphUtils.EmporiumTryAdd(EmporiumEntry.Instance.afterhourInventory, item);
            }
        }

        private static void ShowRules()
        {
            if (CustomUIManager.Instance == null) return;
            string id = ModEntry.MOD_ID + ".aug_rules";
            CustomUIManager.Window(id, Loc.Get("aug_challenge_title"), CustomUIManager.LAYER_META)
                .SetSize(460f, 300f).SetDraggable(true).SetCloseOnEscape(true)
                .BeginColumn(6f)
                    .AddLabel(Loc.Get("aug_challenge_rules", AugRules.MAX_TURNS))
                    .AddLabel(Loc.Get("aug_table_ante_rules"))
                    .AddButton(Loc.Get("aug_close"), () => CustomUIManager.Instance.CloseWindow(id))
                .End().Center().Show();
        }
    }
}
