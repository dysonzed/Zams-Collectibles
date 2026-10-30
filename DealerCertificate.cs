using System;
using System.Collections.Generic;
using System.Linq;

namespace ZamsCollectibles
{
    /// <summary>
    /// The Zamco Authorized Dealer certificate, handed over with the Downside Den. Like the
    /// appraisal certificate, it works from the display case: on display it draws card customers
    /// (pack flippers and deck builders), and the odds climb each day one doesn't turn up. Off
    /// display, those customers almost never come.
    /// </summary>
    public static class DealerCertificate
    {
        public const string ID = ModEntry.MOD_ID + ".dealer_certificate";

        private const int BARE_CHANCE = 2;        // percent a day, without the certificate on display
        private const int BASE_CHANCE = 20;       // percent a day on display, before any missed days
        private const int MAX_CHANCE = 50;
        private const int DECK_BUILDER_SHARE = 55; // percent of card customers who want a deck built (once the Den is open)

        private const string MISSED_KEY = "card_customer_missed";
        private const string DAY_KEY = "card_customer_day";
        private const string GIVEN_KEY = "certificate_given";

        public static void Register(ModItemDirectory dir, Dictionary<string, Action<GameItem>> textByItemId)
        {
            dir.Add(ID, Create);
            textByItemId[ID] = SetText;
        }

        private static GameItem Create()
        {
            GameItem item = ItemDirectory.CreateEmptyItem(ID).SetSpriteAndShapeFromMod(ModEntry.MOD_ID, "dealer_certificate");
            SetText(item);
            item.unitValue = 0L;
            item.EnableTag("IMPORTANT_TAG");
            item.SetGameItemType("DOCUMENT");
            return item;
        }

        private static void SetText(GameItem item)
        {
            item.name = Loc.Get("certificate_name");
            item.shortDescription = Loc.Get("certificate_desc");
            item.flavorText = Loc.Get("certificate_flavor");
        }

        public static bool IsDisplayed() =>
            EmporiumEntry.Instance?.showcaseElement != null && EmporiumEntry.Instance.showcaseElement.childItems.Any(i => i.identifier == ID);

        private static int DaysMissed => ModHelper.GetModDataInt(ModEntry.MOD_ID, MISSED_KEY, 0);

        /// <summary>Today's odds of a card customer, in percent.</summary>
        public static int Chance() => IsDisplayed() ? Math.Min(MAX_CHANCE, BASE_CHANCE * (2 + DaysMissed) / 2) : BARE_CHANCE;

        /// <summary>Once a day: maybe a card customer, by the certificate's odds.</summary>
        public static void OnGenerateCustomer(StoreClientManager manager)
        {
            if (CardDatabase.Sets.Count == 0) return;
            int day = StoreStation.GetDayCounter();
            if (ModHelper.GetModDataInt(ModEntry.MOD_ID, DAY_KEY, -1) == day) return;
            ModHelper.SetModDataInt(ModEntry.MOD_ID, DAY_KEY, day);

            bool displayed = IsDisplayed();
            if (RNG.Roll(Chance()))
            {
                manager.AddClient(Progression.DenUnlocked && RNG.Roll(DECK_BUILDER_SHARE) ? DeckOrders.CreateCustomer() : Progression.CreatePackFlipper());
                ModHelper.SetModDataInt(ModEntry.MOD_ID, MISSED_KEY, 0);
            }
            else if (displayed)
            {
                // like the appraisal certificate, the odds only build up while it's on display
                ModHelper.SetModDataInt(ModEntry.MOD_ID, MISSED_KEY, DaysMissed + 1);
            }
        }

        public static void Tooltip(RichTextBuilder builder)
        {
            builder.AddLine(IsDisplayed() ? Loc.Get("certificate_odds", Chance()) : Loc.Get("certificate_not_displayed"));
        }

        /// <summary>Saves from before the certificate existed: a player who already has the Den gets theirs once.</summary>
        public static void GiveIfMissed()
        {
            if (Progression.DenUnlocked && ModHelper.GetModDataInt(ModEntry.MOD_ID, GIVEN_KEY, 0) == 0) GiveToCounter();
        }

        public static void GiveToCounter()
        {
            ModHelper.SetModDataInt(ModEntry.MOD_ID, GIVEN_KEY, 1);
            Progression.PlaceOnCounter(DirectoryMaster.Item(ID));
        }
    }
}
