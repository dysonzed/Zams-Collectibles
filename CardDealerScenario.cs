namespace ZamsCollectibles
{
    /// <summary>
    /// The Card Dealer starting scenario: the run begins with the Downside Den already open (and
    /// so with the Zamco guide and the dealer certificate on the counter), a few booster packs, a
    /// deck box and a collection binder.
    /// </summary>
    public static class CardDealerScenario
    {
        public const string ID = ModEntry.MOD_ID + ".card_dealer";
        private const int STARTING_PACKS = 3;

        public static void Register()
        {
            ModHelper.AddStartingScenario(ModEntry.MOD_ID, ID,
                title: () => Loc.Get("scenario_title"),
                description: () => Loc.Get("scenario_desc"),
                startWith: new System.Func<string>[]
                {
                    () => Loc.Get("scenario_start_den"),
                    () => Loc.Get("scenario_start_packs", STARTING_PACKS),
                    () => Loc.Get("scenario_start_gear"),
                    () => Loc.Get("scenario_start_certificate"),
                },
                onStart: OnStart,
                iconKey: $"{ModEntry.MOD_ID}:scenario_card_dealer",
                tag: () => Loc.Get("scenario_tag"),
                tagTone: StartingScenario.TagTone.Positive);
        }

        private static void OnStart()
        {
            for (int i = 0; i < STARTING_PACKS; i++)
                ModHelper.MoveItemToInventory(ModHelper.SpawnItem(CardItems.BoosterId(RNG.GetRandomElement(Progression.AvailableSets()))));
            ModHelper.MoveItemToInventory(ModHelper.SpawnItem(AugWarriors.DeckBox.DECKBOX_ID));
            ModHelper.MoveItemToInventory(ModHelper.SpawnItem(CardBinder.BINDER_ID));
            UnlockDen();
        }

        /// <summary>
        /// Opens the Den for this run (which hands over the guide and the certificate). Tried as the
        /// run starts and again once it has started, in case the upgrade wasn't in the run yet.
        /// </summary>
        public static void UnlockDen()
        {
            if (!ModHelper.IsStartingScenario(ID) || Progression.DenUnlocked) return;
            Progression.Register();
            ModHelper.GetNetworkUpgrade(Progression.DEN_UPGRADE)?.Unlock();
        }
    }
}
