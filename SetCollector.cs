using System.Linq;

namespace ZamsCollectibles
{
    /// <summary>
    /// The Collector: a rare customer who comes for sealed sets and nothing else. Sealed sets
    /// already carry their bonus (more for a Master Set), so they pay the sealed value; what they
    /// add is a buyer who always wants them, where the other card customers only might. They only
    /// turn up once the Den is open and the player has a sealed set in the shop, and grow a little
    /// more likely each day one waits for them.
    /// </summary>
    public static class SetCollector
    {
        private const int BASE_CHANCE = 4;     // percent a day, while a sealed set is waiting
        private const int DAILY_RISE = 2;      // added for each day it waits
        private const int MAX_CHANCE = 20;

        private const string MISSED_KEY = "collector_missed";
        private const string DAY_KEY = "collector_day";

        private static bool HasSealedSet() =>
            EmporiumEntry.Instance?.GetAllItems().Any(i => i.IsTag(CardItems.BUNDLE_TAG) && GeneralHelper.IsItemOwned(i)) ?? false;

        /// <summary>Once a day: maybe the Collector, if there's a sealed set for them.</summary>
        public static void OnGenerateCustomer(StoreClientManager manager)
        {
            if (CardDatabase.Sets.Count == 0 || !Progression.DenUnlocked) return;
            int day = StoreStation.GetDayCounter();
            if (ModHelper.GetModDataInt(ModEntry.MOD_ID, DAY_KEY, -1) == day) return;
            ModHelper.SetModDataInt(ModEntry.MOD_ID, DAY_KEY, day);
            if (!HasSealedSet()) return;

            int missed = ModHelper.GetModDataInt(ModEntry.MOD_ID, MISSED_KEY, 0);
            if (RNG.Roll(System.Math.Min(MAX_CHANCE, BASE_CHANCE + DAILY_RISE * missed)))
            {
                manager.AddClient(CreateCustomer());
                ModHelper.SetModDataInt(ModEntry.MOD_ID, MISSED_KEY, 0);
            }
            else
            {
                ModHelper.SetModDataInt(ModEntry.MOD_ID, MISSED_KEY, missed + 1);
            }
        }

        public static StoreClient CreateCustomer()
        {
            StoreClient client = new StoreClient();
            client.identifier = ModEntry.MOD_ID + ".set_collector";
            client.displayName = Loc.Get("client_collector");
            StoreClientFactionSetup.InitUpperLevelCitizen(client);
            client.spriteName = SpriteDict.GetRandomUpperSpriteName();
            client.clientIntent = StoreClient.ClientIntent.BUY;
            client.isBargainAvailable = false;   // they know what a sealed set is worth, and pay it
            client.willAcceptItem = item => item.IsTag(CardItems.BUNDLE_TAG);
            client.mainDialogue.SetText(client.displayName, Loc.Get("client_collector_line"))
                .NextDialogue().SetText(client.displayName, Loc.Get("client_collector_wants"));
            client.CompleteClientCreation();
            return client;
        }
    }
}
