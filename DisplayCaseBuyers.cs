using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace ZamsCollectibles
{
    /// <summary>
    /// Collectibles in the display case draw card buyers, the way the game's own goods do: the case's
    /// most valuable categories (as many as the store draws display-case visitors for) each bring a
    /// buyer at the front of the day's line, and the clientele forecast lists them. Like the game's
    /// buyers there are three, picked by the value of collectibles on display and the store's
    /// attractiveness, with the same budgets as the game's medical buyers.
    ///
    /// The buyers are registered in the game's customer list, so the display case (and the debug
    /// customer spawner) create them like any other customer.
    /// </summary>
    public static class DisplayCaseBuyers
    {
        public const string FAN_ID = ModEntry.MOD_ID + ".card_fan";
        public const string PLAYER_ID = ModEntry.MOD_ID + ".card_player";
        public const string INVESTOR_ID = ModEntry.MOD_ID + ".card_investor";
        private const int GAME_CHANCE = 10;   // percent: the fan and the player ask for a game of Aug Warriors when sent off

        // The game's thresholds for its display-case buyers.
        private const int MID_VALUE = 145, HIGH_VALUE = 350, HIGH_DAY = 14, MID_ATTRACTIVENESS = 400;

        private static readonly Dictionary<string, Func<StoreClient>> Buyers = new Dictionary<string, Func<StoreClient>>
        {
            [FAN_ID] = CreateFan,
            [PLAYER_ID] = CreatePlayer,
            [INVESTOR_ID] = CreateInvestor,
        };

        public static bool Enabled { get; private set; }

        /// <summary>
        /// Adds the buyers to the game's customer list. That list isn't part of the modding API, so if
        /// an update changes it the display case simply draws no card buyers; it mustn't stop the mod
        /// enabling (see PatchGuard).
        /// </summary>
        public static void Enable()
        {
            try
            {
                Register();
                Enabled = true;
            }
            catch (Exception e)
            {
                ModEntry.Instance.Log.Warning($"Display case card buyers are off: {e.GetType().Name}: {e.Message}");
            }
        }

        public static void Disable()
        {
            if (!Enabled) return;
            Enabled = false;
            try { Unregister(); }
            catch (Exception) { }
        }

        // Separate from Enable (and never inlined into it) so that a missing field fails when this is
        // compiled, inside Enable's try.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Register()
        {
            foreach (var buyer in Buyers) StoreClientListDict.storeClientDict[buyer.Key] = buyer.Value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Unregister()
        {
            foreach (string id in Buyers.Keys) StoreClientListDict.storeClientDict.Remove(id);
        }

        /// <summary>Which buyer the collectibles on display draw.</summary>
        public static string PickBuyer()
        {
            int value = ShowcaseHelper.GetTypeTotalValue(Collectibles.TYPE);
            if (value >= HIGH_VALUE && StoreStation.GetDayCounter() >= HIGH_DAY) return INVESTOR_ID;
            if (value >= MID_VALUE || PlayerStore.Instance.GetCurrentStoreAttractiveness() >= MID_ATTRACTIVENESS) return PLAYER_ID;
            return FAN_ID;
        }

        private static StoreClient CreateFan()
        {
            StoreClient client = NewBuyer(FAN_ID, "client_card_fan", "FACTION_LOWER_LEVEL", SpriteDict.GetRandomLowerLevelSpriteName());
            client.SetBudget(80 + RNG.GetRandomInt(0, 25));
            AugWarriors.StoreChallenge.Offer(client, GAME_CHANCE);
            client.CompleteClientCreation();
            return client;
        }

        private static StoreClient CreatePlayer()
        {
            StoreClient client = NewBuyer(PLAYER_ID, "client_card_player", "FACTION_MIDDLE", SpriteDict.GetRandomLowerLevelSpriteName());
            client.SetBudget(145 + RNG.GetRandomInt(0, 50) + StoreStation.GetDayCounter() / 4);
            AugWarriors.StoreChallenge.Offer(client, GAME_CHANCE);
            client.CompleteClientCreation();
            return client;
        }

        private static StoreClient CreateInvestor()
        {
            StoreClient client = NewBuyer(INVESTOR_ID, "client_card_investor", "FACTION_UPPER_LEVEL", SpriteDict.GetRandomUpperSpriteName());
            client.SetBudget(750 + RNG.GetRandomInt(0, 250) + StoreStation.GetDayCounter());
            client.CompleteClientCreation();
            return client;
        }

        private static StoreClient NewBuyer(string id, string nameKey, string faction, string sprite)
        {
            StoreClient client = new StoreClient();
            client.identifier = id;
            client.displayName = Loc.Get(nameKey);
            client.clientFaction = faction;
            client.spriteName = sprite;
            client.clientIntent = StoreClient.ClientIntent.BUY;
            client.IsPersonalConsumption = false;
            client.mainDialogue.SetText(client.displayName, Loc.Get(nameKey + "_line"));
            client.clientBuyingTagList.Add(Collectibles.TYPE);
            client.AddBasicDialog();
            return client;
        }
    }

    // The game ranks the display case's categories from a fixed list without ours; rank them again
    // with collectibles included. Cases without collectibles keep the game's answer.
    [HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetDisplayCaseType), new Type[0])]
    public static class DisplayCaseTypePatch
    {
        private const string FEATURE = "Collectibles in the display case drawing buyers";

        static bool Prepare() =>
            PatchGuard.Target(FEATURE, "ShowcaseHelper.GetDisplayCaseType", AccessTools.Method(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetDisplayCaseType), System.Type.EmptyTypes), typeof(List<string>));

        static void Postfix(ref List<string> __result)
        {
            if (!DisplayCaseBuyers.Enabled) return;
            List<string> types = __result;
            if (PatchGuard.Run(FEATURE, () => types = Rank(types))) __result = types;
        }

        private static List<string> Rank(List<string> vanilla)
        {
            List<GameItem> shown = EmporiumEntry.Instance?.showcaseElement?.childItems;
            if (shown == null || !shown.Any(i => i.IsGameItemType(Collectibles.TYPE))) return vanilla;
            List<string> types = new List<string>(TypeHelper.MAJOR_TYPES_LIST) { Collectibles.TYPE };
            List<KeyValuePair<string, int>> byValue = PlayerStore.Instance.GetTypesByValue(shown, types);
            int count = Math.Min(PlayerStore.Instance.GetShowcaseTypeVisitorCount(), byValue.Count);
            return byValue.Take(count).Where(t => t.Value > 0).Select(t => t.Key).ToList();
        }
    }

    // The game has a buyer for each of its own categories; ours gets one of the card buyers.
    [HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.CreateClientByStockType), typeof(string))]
    public static class DisplayCaseBuyerPatch
    {
        private const string FEATURE = "Card buyers from the display case";

        static bool Prepare() =>
            PatchGuard.Target(FEATURE, "ShowcaseHelper.CreateClientByStockType", AccessTools.Method(typeof(ShowcaseHelper), nameof(ShowcaseHelper.CreateClientByStockType), new[] { typeof(string) }), typeof(string));

        static void Postfix(string __0, ref string __result)
        {
            if (!DisplayCaseBuyers.Enabled || __0 != Collectibles.TYPE) return;
            string buyer = null;
            if (PatchGuard.Run(FEATURE, () => buyer = DisplayCaseBuyers.PickBuyer())) __result = buyer;
        }
    }
}
