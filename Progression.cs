using System;
using System.Collections.Generic;

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

        private const int JACKSON_PACK_CHANCE = 20;   // percent per visit
        private const int FLIPPER_GAME_CHANCE = 10;   // percent: asks for a game of Aug Warriors when sent off

        public static bool DenUnlocked => ModHelper.IsNetworkUpgradeUnlocked(DEN_UPGRADE);

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
                ModHelper.AddNetworkUpgrade(ModEntry.MOD_ID, DEN_UPGRADE, DEN_UPGRADE_COST, "Referral",
                    title: () => Loc.Get("upgrade_den_title"),
                    description: () => Loc.Get("upgrade_den_desc"),
                    onUnlock: OnDenUnlocked,
                    iconKey: $"{ModEntry.MOD_ID}:booster_box_red");
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
            PlayerStore.Instance.AddDirectSellingItemToTable(CardItems.BoosterId(RNG.GetRandomElement(CardDatabase.Sets)));
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
                    PlayerStore.Instance.AddDirectSellingItemToTable(CardItems.BoosterId(RNG.GetRandomElement(CardDatabase.Sets)));
            });
            AugWarriors.StoreChallenge.Offer(client, gameChance);
            client.CompleteClientCreation();
            return client;
        }
    }
}
