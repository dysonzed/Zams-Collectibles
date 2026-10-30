using System;
using System.Collections.Generic;
using System.Linq;

namespace ZamsCollectibles
{
    /// <summary>
    /// How the collectibles open up over a run. Early on, booster packs only turn up by chance:
    /// a rare pack flipper customer, and sometimes a pack on Dr. Jackson's table. The Downside
    /// Den opens once the player buys its upgrade from Wild's Network, which also hands over the
    /// Zamco guide and the dealer certificate; on display, the certificate brings card customers
    /// (see DealerCertificate). The Card Dealer starting scenario begins with all of that.
    /// </summary>
    public static class Progression
    {
        public const string DEN_UPGRADE = ModEntry.MOD_ID + ".DOWNSIDE_DEN";
        private const int DEN_UPGRADE_COST = 500;
        public const string TIER2_UPGRADE = ModEntry.MOD_ID + ".TIER_2";
        private const int TIER2_UPGRADE_COST = 1500;
        private const string ZAMCO_CARD = "Zamco";   // their own card in Wild's Network: Den, then the tier licenses

        private const int JACKSON_PACK_CHANCE = 20;   // percent per visit
        private const int FLIPPER_GAME_CHANCE = 10;   // percent: asks for a game of Aug Warriors when sent off

        public static bool DenUnlocked => ModHelper.IsNetworkUpgradeUnlocked(DEN_UPGRADE);

        /// <summary>The highest power tier the player has licensed: 1 to start, 2 with the Tier 2 License.</summary>
        public static int MaxTier => ModHelper.IsNetworkUpgradeUnlocked(TIER2_UPGRADE) ? 2 : 1;

        /// <summary>The sets shops and customers stock: those of the licensed tiers.</summary>
        public static List<CardSetDef> AvailableSets()
        {
            int tier = MaxTier;
            List<CardSetDef> sets = CardDatabase.Sets.Where(s => s.tier <= tier).ToList();
            return sets.Count > 0 ? sets : CardDatabase.Sets;
        }

        public static List<CardDef> AvailableCards() => AvailableSets().SelectMany(s => s.cards).ToList();

        /// <summary>
        /// Registers the Wild's Network upgrade. At the main menu the game's registration stores
        /// the definition and then touches the player store, which can't be built yet and throws;
        /// the definition is kept, so that error is caught. Called again once a run exists (a save
        /// loads or a new game starts), which adds the upgrade to the run; repeating it is safe.
        /// </summary>
        public static void Register()
        {
            try
            {
                ModHelper.AddNetworkUpgrade(ModEntry.MOD_ID, DEN_UPGRADE, DEN_UPGRADE_COST, ZAMCO_CARD,
                    title: () => Loc.Get("upgrade_den_title"),
                    description: () => Loc.Get("upgrade_den_desc"),
                    onUnlock: OnDenUnlocked,
                    iconKey: $"{ModEntry.MOD_ID}:booster_box_rustwave",
                    subtitleLabel: () => Loc.Get("upgrade_card_zamco"));
                ModHelper.AddNetworkUpgrade(ModEntry.MOD_ID, TIER2_UPGRADE, TIER2_UPGRADE_COST, ZAMCO_CARD,
                    title: () => Loc.Get("upgrade_tier2_title"),
                    description: () => Loc.Get("upgrade_tier2_desc"),
                    onUnlock: OnTier2Unlocked,
                    iconKey: $"{ModEntry.MOD_ID}:booster_box_grace",
                    prerequisites: new[] { DEN_UPGRADE },
                    subtitleLabel: () => Loc.Get("upgrade_card_zamco"));
            }
            catch (NullReferenceException)
            {
                // main menu: no player store yet; registered again when a run starts
            }
        }

        // The referral comes with Zamco's welcome pack: the guide and the dealer certificate.
        private static void OnDenUnlocked()
        {
            ZamcoGuide.GiveToCounter();
            DealerCertificate.GiveToCounter();
        }

        // The license comes with a sample: a Tier 2 booster pack on the counter.
        private static void OnTier2Unlocked()
        {
            List<CardSetDef> tier2 = CardDatabase.Sets.Where(s => s.tier == 2).ToList();
            if (tier2.Count > 0) PlaceOnCounter(DirectoryMaster.Item(CardItems.BoosterId(RNG.GetRandomElement(tier2))));
        }

        /// <summary>Puts an item on the store's front counter (the back counter if it's full).</summary>
        public static void PlaceOnCounter(GameItem item)
        {
            if (item == null) return;
            if (GraphUtils.TryAcceptAllMid(EmporiumEntry.Instance.frontInvinvElement, item) > 0)
                GraphUtils.EmporiumTryAdd(EmporiumEntry.Instance.backInvinvElement, item);
        }

        public static void OnRunStarted() => Register();

        public static void OnRunStarted(string _)
        {
            Register();
            CardDealerScenario.UnlockDen();
        }

        /// <summary>Dr. Jackson sometimes has a booster pack on his table.</summary>
        public static void OnInventorStock(List<GameItem> ownedItems)
        {
            if (CardDatabase.Sets.Count == 0 || !RNG.Roll(JACKSON_PACK_CHANCE)) return;
            PlayerStore.Instance.AddDirectSellingItemToTable(CardItems.BoosterId(RNG.GetRandomElement(AvailableSets())));
        }

        // A scavenger with a few packs they found or flipped.
        public static StoreClient CreatePackFlipper() => CreatePackFlipper(FLIPPER_GAME_CHANCE);

        public static StoreClient CreatePackFlipper(int gameChance)
        {
            StoreClient client = new StoreClient();
            client.identifier = ModEntry.MOD_ID + ".pack_flipper";
            client.displayName = Loc.Get("client_pack_flipper");
            StoreClientFactionSetup.InitScav(client);
            client.spriteName = SpriteDict.GetRandomScavSpriteName();
            client.clientIntent = StoreClient.ClientIntent.SELL;
            int packs = RNG.GetRandomInt(1, 3);
            client.mainDialogue.SetText(client.displayName, Loc.Get("client_pack_flipper_line")).SetEndAction(() =>
            {
                for (int i = 0; i < packs; i++)
                    PlayerStore.Instance.AddDirectSellingItemToTable(CardItems.BoosterId(RNG.GetRandomElement(AvailableSets())));
            });
            AugWarriors.StoreChallenge.Offer(client, gameChance);
            client.CompleteClientCreation();
            return client;
        }
    }
}
