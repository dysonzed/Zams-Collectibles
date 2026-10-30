namespace ZamsCollectibles
{
    /// <summary>
    /// The Zamco Stockist Guide: a paper book (Books/zamco_guide.json) with Zamco's pitch, the way
    /// to the Downside Den, and the Aug Warriors rules and deck building. A copy lands on the store
    /// counter when the Den is unlocked; the Aug Warriors table's Rules button opens it at the rules.
    /// </summary>
    public static class ZamcoGuide
    {
        public const string BOOK_ID = "zamco_guide";
        public const int RULES_PAGE = 3;   // "How to Play: the Goal" (pages count from 0)

        private const string GIVEN_KEY = "guide_given";

        /// <summary>Saves from before the guide existed: a player who already has the Den gets their copy once.</summary>
        public static void GiveIfMissed()
        {
            if (Progression.DenUnlocked && ModHelper.GetModDataInt(ModEntry.MOD_ID, GIVEN_KEY, 0) == 0) GiveToCounter();
        }

        /// <summary>Puts a copy of the guide on the store's front counter (the back counter if it's full).</summary>
        public static void GiveToCounter()
        {
            ModHelper.SetModDataInt(ModEntry.MOD_ID, GIVEN_KEY, 1);
            Progression.PlaceOnCounter(ModHelper.SpawnPaperItem(ModEntry.MOD_ID, BOOK_ID));
        }

        public static void Open(int page = 0) => ModHelper.OpenPaper(ModEntry.MOD_ID, BOOK_ID)?.FlipTo(page);
    }
}
