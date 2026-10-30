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

        // A store customer who asked for a game: they are the only opponent, and they leave once it's over.
        // played is false when the player closed the table without playing.
        private static (string name, AugPersonality style)? challenger;
        private static Action<int?, bool> challengeOver;
        private static bool builtForChallenge;

        private static (string name, AugPersonality style) Opponent => challenger ?? Regulars[regular];

        // Where the player's things go back to: the Den's inventory, or the store counter.
        private static GraphNodeStorage Home => challenger != null ? EmporiumEntry.Instance.backInvinvElement : EmporiumEntry.Instance.afterhourInventory;

        /// <summary>The Den's table, against the regulars.</summary>
        public static void Open()
        {
            challenger = null;
            challengeOver = null;
            Show();
        }

        /// <summary>The table at the store, against a customer. over(winner, played) runs once they're done.</summary>
        public static void OpenChallenge(string name, AugPersonality style, Action<int?, bool> over)
        {
            challenger = (name, style);
            challengeOver = over;
            Show();
        }

        private static void Show()
        {
            bool challenge = challenger != null;
            if (window != null && window.handler != null && builtForChallenge != challenge)
            {
                // the opponent panel differs: build the other layout
                window.Hide();
                UnityEngine.Object.Destroy(window.handler.gameObject);
                window = null;
            }
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

        // Closed with its X: a store challenger takes that as a no.
        private static void OnClosed()
        {
            ReturnItems();
            if (challenger != null && !inMatch) EndChallenge(null, false);
        }

        private static void EndChallenge(int? winner, bool played)
        {
            Action<int?, bool> over = challengeOver;
            challenger = null;
            challengeOver = null;
            over?.Invoke(winner, played);
        }

        private static void Build()
        {
            builtForChallenge = challenger != null;
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
            MachineryButton rules = new MachineryButton(Loc.Get("aug_table_rules"), () => ZamcoGuide.Open(ZamcoGuide.RULES_PAGE));

            window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.Panel(Loc.Get("aug_table_deck"), MachineryStack.Vertical(deckSlot, deckText), "deck"),
                MachineHelper.Panel(Loc.Get("aug_challenge_ante"), MachineryStack.Vertical(anteSlot, anteText, credits), "ante"),
                MachineHelper.Panel(Loc.Get("aug_challenge_opponent"), builtForChallenge ? MachineryStack.Vertical(opponentText) : MachineryStack.Vertical(opponents, opponentText), "opponent"),
                MachineHelper.Panel(Loc.Get("aug_table_play"), MachineryStack.Vertical(playButton, rules, statusText), "play"));
            window.SetTitle(Loc.Get("aug_challenge_title"));
            window.closeCallback += OnClosed;
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
            opponentText.SetText(challenger != null
                ? Opponent.name + "\n" + Loc.Get("aug_regular_" + Opponent.style.id)
                : Loc.Get("aug_regular_" + Opponent.style.id));

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
            var (name, style) = Opponent;
            try
            {
                AugBoard.Start(deck, name, style, creditAnte, Finished);
            }
            catch (Exception e)
            {
                // The board drives the game's own widgets; if a game update breaks it, play in the plain window.
                ModEntry.Instance.Log.Warning($"Aug Warriors: the board could not open ({e.GetType().Name}: {e.Message}). Using the plain match window.");
                AugBoard.CloseIfOpen();
                AugMatchWindow.Start(deck, name, style, creditAnte, Finished);
            }
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
                if (prize != null) GraphUtils.EmporiumTryAdd(Home, prize);
            }
            if (challenger != null)
            {
                ReturnItems();
                EndChallenge(winner, true);
                return;
            }
            Show();
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
                GraphUtils.EmporiumTryAdd(Home, item);
            }
        }
    }
}
