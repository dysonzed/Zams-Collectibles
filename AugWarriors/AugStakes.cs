using System;

namespace ZamsCollectibles.AugWarriors
{
    /// <summary>
    /// What's at stake. The ante is what the player risks: lose (or concede) and that's what goes.
    /// Most opponents play even money for small antes; Oracle, the best player at the Den, only
    /// plays for big ones and pays triple. A card ante won off Oracle brings two prize cards,
    /// each more likely to be a rarity up, and a booster pack of the highest licensed tier (AugTable).
    /// </summary>
    public static class AugStakes
    {
        private const float ORACLE_WIN = 3f;
        private static readonly int[] Antes = { 0, 10, 25, 50 };
        private static readonly int[] OracleAntes = { 0, 100, 250, 500 };

        public static bool HighStakes(AugPersonality style) => style == AugPersonality.Oracle;

        /// <summary>The credit antes on offer against this opponent, "no stakes" first (always the same count).</summary>
        public static int[] Options(AugPersonality style) => HighStakes(style) ? OracleAntes : Antes;

        /// <summary>Credits the player wins on this ante.</summary>
        public static int Won(AugPersonality style, int ante) =>
            HighStakes(style) ? (int)Math.Round(ante * ORACLE_WIN) : ante;
    }
}
