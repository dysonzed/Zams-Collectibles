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
        private const int RARITY_UP_CHANCE = 15;    // percent: the won card is one rarity up
        private const int NEW_CARD_CHANCE = 25;     // percent: the won card is one the player doesn't own
        private const int HIGH_STAKES_PRIZES = 2, HIGH_STAKES_RARITY_UP = 40;   // a card ante won off Oracle

        private static PixelWindow window;
        private static GameSlotInventory deckSlot, anteSlot;
        private static MachineryText deckText, anteText, opponentText, statusText;
        private static MachineryButton playButton;
        private static int chosen, anteChoice;
        private static MachineryRadioList creditList;
        private static int CreditAnte => AugStakes.Options(Opponent.style)[anteChoice];
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
                // a different set of opponents: rebuild the opponent panel. This isn't the player
                // closing the table, so OnClosed must not run: it would take a new store challenge as a no.
                ReturnItems();
                window.closeCallback -= OnClosed;
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
            deckSlot.onSlotAddItemEndFunc += (_, _, _) => SlotChanged();
            deckSlot.onSlotRemoveItemEndFunc += (_, _, _) => SlotChanged();

            anteSlot = new GameSlotInventory();
            anteSlot.mayInventoryAddItemFunc = (item, _) => GeneralHelper.IsItemOwned(item) && CardItems.IsCard(item) && !inMatch;
            anteSlot.mayInventoryRemoveItemFunc = (_, _) => !inMatch;
            anteSlot.onSlotAddItemEndFunc += (_, _, _) => SlotChanged();
            anteSlot.onSlotRemoveItemEndFunc += (_, _, _) => SlotChanged();

            deckText = new MachineryText();
            anteText = new MachineryText();
            opponentText = new MachineryText();
            statusText = new MachineryText();

            // the same buttons for every opponent; Refresh labels them with this opponent's antes
            anteChoice = 0;
            creditList = new MachineryRadioList(AnteLabels(), 0, i => { anteChoice = i; Refresh(); });
            MachineryStack opponentPanel = opponents.Count > 1
                ? MachineryStack.Vertical(new MachineryRadioList(opponents.Select(o => o.name).ToList(), chosen, ChooseOpponent), opponentText)
                : MachineryStack.Vertical(opponentText);
            playButton = new MachineryButton(Loc.Get("aug_challenge_start"), Play);
            MachineryButton rules = new MachineryButton(Loc.Get("aug_table_rules"), () => ZamcoGuide.Open(ZamcoGuide.RULES_PAGE));

            window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.Panel(Loc.Get("aug_table_deck"), MachineryStack.Vertical(deckSlot, deckText), "deck"),
                MachineHelper.Panel(Loc.Get("aug_challenge_ante"), MachineryStack.Vertical(anteSlot, anteText, creditList), "ante"),
                MachineHelper.Panel(Loc.Get("aug_challenge_opponent"), opponentPanel, "opponent"),
                MachineHelper.Panel(Loc.Get("aug_table_play"), MachineryStack.Vertical(playButton, rules, statusText), "play"));
            window.SetTitle(Loc.Get("aug_challenge_title"));
            window.closeCallback += OnClosed;
        }

        // Runs inside the game's drop of an item into a slot. An exception there skips the end of
        // the drop, leaving the item stuck to the cursor and the mouse dead, so log it instead.
        private static void SlotChanged()
        {
            try { Refresh(); }
            catch (Exception e) { ModEntry.Instance.Log.Warning($"Aug Warriors table: {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); }
        }

        private static void Refresh()
        {
            if (window == null) return;
            List<CardDef> deck = DeckBox.Cards(deckSlot.childItem);
            List<AugEvent> errors = AugRules.DeckErrors(deck);
            deckText.SetText(deckSlot.childItem == null ? Loc.Get("aug_table_no_deck")
                : errors.Count == 0 ? Loc.Get("deckbox_status_ok", deck.Count)
                : Loc.Get("deckbox_status_errors", deck.Count, errors.Count));

            List<string> labels = AnteLabels();
            for (int i = 0; i < labels.Count; i++) creditList.Get(i)?.SetText(labels[i]);

            CardDef ante = CardItems.GetCardDef(anteSlot.childItem);
            anteText.SetText(ante == null ? Loc.Get("aug_table_no_card") : Loc.Get("aug_table_card_ante", Loc.CardTitle(ante)));
            // how they play is for the player to find out
            opponentText.SetText(AugStakes.HighStakes(Opponent.style) ? Loc.Get("aug_table_high_stakes")
                : opponents.Count > 1 ? Loc.Get("aug_table_opponent_hint") : Opponent.name);

            string problem = !AtStore && DenRegulars.PlayedTonight() ? Loc.Get("aug_table_played_tonight")
                : deckSlot.childItem == null ? Loc.Get("aug_table_need_deck")
                : errors.Count > 0 ? Loc.Get("aug_table_fix_deck")
                : PlayerStore.Instance.GetCash() < CreditAnte ? Loc.Get("den_not_enough")
                : null;
            statusText.SetText(problem ?? Loc.Get("aug_table_ready"));
            if (problem == null) playButton.Enable(); else playButton.Disable();
        }

        private static void Play()
        {
            List<CardDef> deck = DeckBox.Cards(deckSlot.childItem);
            if (inMatch || !AugRules.IsLegalDeck(deck) || PlayerStore.Instance.GetCash() < CreditAnte) return;
            var (name, style) = Opponent;
            if (!AtStore && DenRegulars.PlayedTonight()) return;
            if (!AtStore) DenRegulars.MarkPlayed();   // the night's one game, conceded or not
            inMatch = true;
            DeckBox.SetInPlay(deckSlot.childItem, true);
            window.Hide();
            try
            {
                AugBoard.Start(deck, name, style, CreditAnte, Finished);
            }
            catch (Exception e)
            {
                // The board drives the game's own widgets; if a game update breaks it, play in the plain window.
                ModEntry.Instance.Log.Warning($"Aug Warriors: the board could not open ({e.GetType().Name}: {e.Message}). Using the plain match window.");
                AugBoard.CloseIfOpen();
                AugMatchWindow.Start(deck, name, style, CreditAnte, Finished);
            }
        }

        // winner: 0 the player, 1 the opponent, null a draw. Settles the card ante and brings the table back.
        private static void Finished(int? winner)
        {
            inMatch = false;
            DeckBox.SetInPlay(deckSlot.childItem, false);
            int worn = CardSleeves.WearDeck(deckSlot.childItem);   // every game handles the cards
            if (worn > 0) ModEntry.Instance.Log.Log($"Aug Warriors: {worn} unsleeved card(s) took some wear.");
            GameItem anted = anteSlot.childItem;
            CardDef anteCard = CardItems.GetCardDef(anted);
            if (anteCard != null && winner == 1)
            {
                anted.Destroy();
                ModEntry.Instance.Log.Log($"Aug Warriors: lost {anteCard.name} on the ante.");
            }
            else if (anteCard != null && winner == 0)
            {
                bool high = AugStakes.HighStakes(Opponent.style);
                for (int i = 0; i < (high ? HIGH_STAKES_PRIZES : 1); i++)
                {
                    GameItem prize = PrizeCard(anteCard, high ? HIGH_STAKES_RARITY_UP : RARITY_UP_CHANCE);
                    if (prize != null) GraphUtils.EmporiumTryAdd(Home, prize);
                }
                if (high) GraphUtils.EmporiumTryAdd(Home, PrizePack());
            }
            if (AtStore)
            {
                ReturnItems();
                EndChallenge(winner, true);
                return;
            }
            Show();
        }

        // Switching between Oracle's antes and everyone else's goes back to no stakes, so 50 never quietly becomes 500.
        private static void ChooseOpponent(int i)
        {
            bool wasHigh = AugStakes.HighStakes(Opponent.style);
            chosen = i;
            if (AugStakes.HighStakes(Opponent.style) != wasHigh)
            {
                anteChoice = 0;
                creditList.Select(0);
            }
            Refresh();
        }

        private static List<string> AnteLabels() =>
            AugStakes.Options(opponents.Count == 0 ? null : Opponent.style)
                .Select(a => a == 0 ? Loc.Get("aug_no_stakes") : a + GeneralHelper.CreditIcon()).ToList();

        /// <summary>A booster pack from one of the highest licensed tier's sets.</summary>
        private static GameItem PrizePack()
        {
            List<CardSetDef> sets = Progression.AvailableSets();
            int top = sets.Max(s => s.tier);
            GameItem pack = DirectoryMaster.Item(CardItems.BoosterId(RNG.GetRandomElement(sets.Where(s => s.tier == top).ToList())));
            ModEntry.Instance.Log.Log($"Aug Warriors: won {pack.name} off Oracle.");
            return pack;
        }

        /// <summary>A card of the anted card's rarity, sometimes one up, a quarter of the time one the player doesn't own.</summary>
        private static GameItem PrizeCard(CardDef anted, int rarityUpChance)
        {
            List<string> order = CardDatabase.Rarities.Keys.ToList();
            int rank = Math.Max(0, order.IndexOf(anted.rarity));
            if (RNG.Roll(rarityUpChance) && rank < order.Count - 1) rank++;
            string rarity = order[rank];

            List<CardDef> pool = Progression.AvailableCards().Where(c => c.rarity == rarity).ToList();
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
