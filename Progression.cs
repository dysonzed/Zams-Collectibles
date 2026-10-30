using System.Collections.Generic;

namespace ZamsCollectibles
{
    /// <summary>
    /// How the collectibles open up over a run. Early on, booster packs only turn up by chance:
    /// a pack flipper customer now and then, and sometimes a pack on Dr. Jackson's table. The
    /// Downside Den (and the deck order customers) open once the player buys the Downside Den
    /// upgrade from Wild's Network.
    /// </summary>
    public static class Progression
    {
        public const string DEN_UPGRADE = ModEntry.MOD_ID + ".DOWNSIDE_DEN";
        private const int DEN_UPGRADE_COST = 500;

        private const int JACKSON_PACK_CHANCE = 20;   // percent per visit
        private const int PACK_FLIPPER_CHANCE = 6;    // percent per customer generated, at most once a day
        private const int DECK_ORDER_CHANCE = 8;      // percent per customer generated, at most once a day, once unlocked

        public static bool DenUnlocked => ModHelper.IsNetworkUpgradeUnlocked(DEN_UPGRADE);

        public static void Register()
        {
            ModHelper.AddNetworkUpgrade(ModEntry.MOD_ID, DEN_UPGRADE, DEN_UPGRADE_COST, "Referral",
                title: () => Loc.Get("upgrade_den_title"),
                description: () => Loc.Get("upgrade_den_desc"),
                onUnlock: () => { },
                iconKey: $"{ModEntry.MOD_ID}:booster_box_red");
        }

        /// <summary>Dr. Jackson sometimes has a booster pack on his table.</summary>
        public static void OnInventorStock(List<GameItem> ownedItems)
        {
            if (CardDatabase.Sets.Count == 0 || !RNG.Roll(JACKSON_PACK_CHANCE)) return;
            PlayerStore.Instance.AddDirectSellingItemToTable(CardItems.BoosterId(RNG.GetRandomElement(CardDatabase.Sets)));
        }

        /// <summary>Now and then a customer comes in to sell packs; once the Den is open, deck orders too.</summary>
        public static void OnGenerateCustomer(StoreClientManager manager)
        {
            if (CardDatabase.Sets.Count == 0) return;
            if (OncePerDay("pack_flipper_day") && RNG.Roll(PACK_FLIPPER_CHANCE)) { MarkToday("pack_flipper_day"); manager.AddClient(CreatePackFlipper()); }
            else if (DenUnlocked && OncePerDay("deck_order_day") && RNG.Roll(DECK_ORDER_CHANCE)) { MarkToday("deck_order_day"); manager.AddClient(DeckOrders.CreateCustomer()); }
        }

        private static bool OncePerDay(string key) => ModHelper.GetModDataInt(ModEntry.MOD_ID, key, -1) != StoreStation.GetDayCounter();

        private static void MarkToday(string key) => ModHelper.SetModDataInt(ModEntry.MOD_ID, key, StoreStation.GetDayCounter());

        // A scavenger with a few packs they found or flipped.
        public static StoreClient CreatePackFlipper()
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
            client.CompleteClientCreation();
            return client;
        }
    }
}
