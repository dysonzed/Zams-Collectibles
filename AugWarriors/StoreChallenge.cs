using System.Linq;

namespace ZamsCollectibles.AugWarriors
{
    /// <summary>
    /// Card customers who sometimes ask for a game of Aug Warriors as they're sent off, the way the
    /// gunsmith's customers ask about melee weapons. Only asks a player who owns a legal deck. Yes
    /// opens the table at the counter with the customer as the opponent; they leave once it's over.
    /// </summary>
    public static class StoreChallenge
    {
        /// <summary>Gives the customer a chance (percent) to ask for a game when dismissed.</summary>
        public static void Offer(StoreClient client, int chance)
        {
            client.tryInterceptDismiss = () => RNG.Roll(chance) && Ask(client);
        }

        private static bool PlayerHasDeck() =>
            EmporiumEntry.Instance.GetAllOwnedItems().Any(i => DeckBox.IsDeckBox(i) && AugRules.IsLegalDeck(DeckBox.Cards(i)));

        private static bool Ask(StoreClient client)
        {
            if (!PlayerHasDeck() || DialogUIManager.Instance == null) return false;
            AugPersonality style = RNG.GetRandomElement(AugPersonality.All);

            // A choice's dialogue is the player's answer: its text labels the choice and the game moves
            // straight on to the line after it, so the customer's reply (and its action) goes there.
            Dialogue yes = new Dialogue().SetText("PLAYER", Loc.Get("challenge_choice_yes"));
            yes.NextDialogue().SetText(client.displayName, Loc.Get("challenge_accepted"))
                .SetEndAction(() => AugTable.OpenChallenge(client.displayName, style, (winner, played) => After(client, winner, played)));
            Dialogue no = new Dialogue().SetText("PLAYER", Loc.Get("challenge_choice_no"));
            no.NextDialogue().SetText(client.displayName, Loc.Get("challenge_declined"))
                .SetEndAction(() => Leave(client));
            Dialogue ask = new Dialogue().SetText(client.displayName, Loc.Get("challenge_ask"))
                .AddChoice(DirectoryMaster.Item("yes"), Loc.Get("challenge_choice_yes"), "", yes, () => { })
                .AddChoice(DirectoryMaster.Item("cancel"), Loc.Get("challenge_choice_no"), "", no, () => { });
            ask.priority = 4;
            DialogUIManager.Instance.StartDialogue(ask, forcedStart: true);
            return true;
        }

        // A parting line for how it went, then they're off.
        private static void After(StoreClient client, int? winner, bool played)
        {
            string key = !played ? "challenge_declined" : winner == 0 ? "challenge_they_lost" : winner == 1 ? "challenge_they_won" : "challenge_draw";
            DialogUIManager.Instance.StartDialogue(new Dialogue().SetText(client.displayName, Loc.Get(key)).SetEndAction(() => Leave(client)), forcedStart: true);
        }

        private static void Leave(StoreClient client)
        {
            if (PlayerStore.Instance.currentClientInstance?.GetClientBlueprint() == client)
                PlayerStore.Instance.DismissCurrentClient();
        }
    }
}
