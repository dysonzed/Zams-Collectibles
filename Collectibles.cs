using HarmonyLib;
using UnityEngine;

namespace ZamsCollectibles
{
    /// <summary>
    /// The COLLECTIBLE item category. An item's category is a tag that customers match against
    /// their buying list, so only customers with COLLECTIBLE on that list (plus the rare
    /// buy-anything customers) will buy cards, packs and sealed sets.
    /// </summary>
    public static class Collectibles
    {
        public const string TYPE = "COLLECTIBLE";

        // Marks a customer we already rolled for, so each one is only considered once.
        private const string ROLLED_MARKER = "ZAMS_COLLECTIBLES_ROLLED";

        // Chance (percent) that a buying customer of this faction also collects.
        private static int InterestChance(StoreClient client)
        {
            if (client.IsFaction("FACTION_TOURIST")) return 35;
            if (client.IsFaction("FACTION_UPPER_LEVEL")) return 20;
            if (client.IsFaction("FACTION_MIDDLE")) return 15;
            if (client.IsFaction("FACTION_LOWER_LEVEL")) return 5;
            return 0;
        }

        /// <summary>OnGenerateCustomerLate: runs after the vanilla customers of this round are queued.</summary>
        public static void OnGenerateCustomer(StoreClientManager manager)
        {
            foreach (StoreClient client in manager.clientStack)
            {
                if (client == null || client.clientBuyingTagList.Contains(ROLLED_MARKER)) continue;
                client.clientBuyingTagList.Add(ROLLED_MARKER);

                if (!client.IsClientBuying() || client.isAug) continue;
                if (RNG.Roll(InterestChance(client)) && !client.clientBuyingTagList.Contains(TYPE))
                    client.clientBuyingTagList.Add(TYPE);
            }
        }
    }

    // The game looks category names up in its own string table, which has no entry for ours.
    [HarmonyPatch(typeof(TypeHelper), nameof(TypeHelper.GetTypeDisplayName))]
    public static class CollectibleTypeNamePatch
    {
        static void Postfix(string typeConst, ref string __result)
        {
            if (typeConst == Collectibles.TYPE) __result = "Collectible";
        }
    }

    [HarmonyPatch(typeof(TypeHelper), nameof(TypeHelper.GetTypeColor))]
    public static class CollectibleTypeColorPatch
    {
        private static readonly Color CollectibleColor = new Color32(0xB0, 0x7A, 0x2E, 0xFF);

        static void Postfix(string typeConst, ref Color __result)
        {
            if (typeConst == Collectibles.TYPE) __result = CollectibleColor;
        }
    }
}
