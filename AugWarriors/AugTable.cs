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

        private static PixelWindow window;
        private static GameSlotInventory deckSlot, anteSlot;
        private static MachineryText deckText, anteText, opponentText, statusText;
        private static MachineryButton playButton;
        private static int chosen, creditAnte;
        private static bool inMatch;

        // Who can be played: the regulars at the Den tonight, or one store customer. Their play
        // styles are hidden from the player.
        private static List<(string name, AugPersonality style)> opponents = new List<(string, AugPersonality)>();
        private static string builtFor;   // the opponent list the window was built for

        // A store customer who asked for a game leaves once it's over. played is false when the
        // player closed the table without playing.
        private static Action<int?, bool> challengeOver;
        private static bool AtStore => challengeOver != null;

        private static (string name, AugPersonality style) Opponent => opponents[Math.Min(chosen, opponents.Count - 1)];

        // Where the player's things go back to: the Den's inventory, or the store counter.
        private static GraphNodeStorage Home => AtStore ? EmporiumEntry.Instance.backInvinvElement : EmporiumEntry.Instance.afterhourInventory;

        /// <summary>The Den's table, against whichever regulars are in tonight (none: it doesn't open).</summary>
        public static void Open()
        {
            challengeOver = null;
            opponents = DenRegulars.Tonight();
            if (opponents.Count == 0) return;
            Show();
        }

        /// <summary>The table at the store, against a customer. over(winner, played) runs once they're done.</summary>
        public static void OpenChallenge(string name, AugPersonality style, Action<int?, bool> over)
        {
            challengeOver = over;
            opponents = new List<(string, AugPersonality)> { (name, style) };
            Show();
        }

        private static void Show()
        {
            string layout = string.Join("|", opponents.Select(o => o.name));
            if (window != null && window.handler != null && builtFor != layout)
            {
                // a different set of opponents: rebuild the opponent panel
                window.Hide();
                UnityEngine.Object.Destroy(window.handler.gameObject);
                window = null;
            }
            if (builtFor != layout) chosen = 0;
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
            if (AtStore && !inMatch) EndChallenge(null, false);
        }

        private static void EndChallenge(int? winner, bool played)
        {
            Action<int?, bool> over = challengeOver;
            challengeOver = null;
            over?.Invoke(winner, played);
        }

        private static void Build()
        {
            builtFor = string.Join("|", opponents.Select(o => o.name));
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
            MachineryStack opponentPanel = opponents.Count > 1
                ? MachineryStack.Vertical(new MachineryRadioList(opponents.Select(o => o.name).ToList(), chosen, i => { chosen = i; Refresh(); }), opponentText)
                : MachineryStack.Vertical(opponentText);
            playButton = new MachineryButton(Loc.Get("aug_challenge_start"), Play);
            MachineryButton rules = new MachineryButton(Loc.Get("aug_table_rules"), () => ZamcoGuide.Open(ZamcoGuide.RULES_PAGE));

            window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.Panel(Loc.Get("aug_table_deck"), MachineryStack.Vertical(deckSlot, deckText), "deck"),
                MachineHelper.Panel(Loc.Get("aug_challenge_ante"), MachineryStack.Vertical(anteSlot, anteText, credits), "ante"),
                MachineHelper.Panel(Loc.Get("aug_challenge_opponent"), opponentPanel, "opponent"),
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
            anteText.SetText(ante == null ? Loc.Get("aug_table_no_card") : Loc.Get("aug_table_card_ante", Loc.CardTitle(ante)));
            // how they play is for the player to find out
            opponentText.SetText(opponents.Count > 1 ? Loc.Get("aug_table_opponent_hint") : Opponent.name);

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
            if (AtStore)
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
