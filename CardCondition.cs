using System;

namespace ZamsCollectibles
{
    /// <summary>
    /// Every card carries a hidden exact condition (1.00 to 10.00). Players see the condition
    /// label until the card is graded, which reveals the number. The card viewer's wear scales
    /// with the exact number, so a sharp eye can read it without grading.
    /// Raw value follows the label; graded value follows the exact grade.
    /// </summary>
    public static class CardCondition
    {
        // The standard TCG condition scale; labels are localization keys. Fresh cards are mostly
        // played-looking (this is the station), so the multipliers center on Moderately Played
        // and climb steeply: a clean card is a real find. Tuned with tools/economy.py.
        private static readonly (float min, string key, float multiplier)[] Labels =
        {
            (9.5f, "condition_mint",              3.8f),
            (8.5f, "condition_near_mint",         2.5f),
            (7.0f, "condition_lightly_played",    1.6f),
            (5.0f, "condition_moderately_played", 1.15f),
            (3.0f, "condition_heavily_played",    0.8f),
            (0.0f, "condition_damaged",           0.5f),
        };

        // A perfect 10.0 is its own jackpot roll; every other card tops out just under it.
        public const float GEM_CHANCE = 0.005f;
        private const float BEST_ORDINARY = 9.94f;   // grades 9.9 at best

        // Packs sit on shelves, in cargo holds and in pockets before they're opened: fresh cards
        // spread across the whole scale, most of them 5s and 6s (about 2% Mint, 5% Near Mint,
        // 20% Lightly Played, 43% Moderately Played, 25% Heavily Played, 5% Damaged).
        private const float FRESH_MEAN = 5.9f, FRESH_SD = 1.75f;
        // Second-hand singles have been played with: a little worse.
        private const float USED_MEAN = 5.2f, USED_SD = 2.0f;

        /// <summary>Condition of a card fresh out of a pack.</summary>
        public static float RollFreshCondition()
        {
            if (RNG.random.NextDouble() < GEM_CHANCE) return 10f;
            return RollNormal(FRESH_MEAN, FRESH_SD);
        }

        /// <summary>Condition of a second-hand card: never a gem 10.</summary>
        public static float RollUsedCondition() => RollNormal(USED_MEAN, USED_SD);

        private static float RollNormal(float mean, float sd)
        {
            double u1 = 1.0 - RNG.random.NextDouble();
            double u2 = RNG.random.NextDouble();
            double gaussian = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return (float)Math.Round(Clamp(mean + (float)gaussian * sd, 1f, BEST_ORDINARY), 2);
        }

        public static string Label(float condition)
        {
            foreach (var entry in Labels)
                if (condition >= entry.min) return Loc.Get(entry.key);
            return Loc.Get(Labels[Labels.Length - 1].key);
        }

        public static float RawMultiplier(float condition)
        {
            foreach (var entry in Labels)
                if (condition >= entry.min) return entry.multiplier;
            return Labels[Labels.Length - 1].multiplier;
        }

        /// <summary>
        /// Graded cards sell on the exact number: a gem 10 is worth far more than a 9. From 7 up a
        /// slab beats the raw price, so grading a card that looks clean pays (a rare that looks
        /// Near Mint gains about 70 at Tier 1); grading blind, or a common that only looks played,
        /// loses the fee. Below 7 the slab only proves the wear.
        /// </summary>
        public static float GradedMultiplier(float grade)
        {
            if (grade >= 10f) return 16f;
            if (grade >= 9.5f) return 7f;
            if (grade >= 9f) return 4.5f;
            if (grade >= 8.5f) return 3.2f;
            if (grade >= 8f) return 2.4f;
            if (grade >= 7f) return 1.85f;
            return RawMultiplier(grade) * 0.95f;
        }

        /// <summary>
        /// What a high grade adds to any card, on top of the multiplier: collectors pay for the slab
        /// itself, so a gem 10 common is a jackpot (over 100), and a low grade isn't worth the fee.
        /// </summary>
        public static int GradePremium(float grade)
        {
            if (grade >= 10f) return 100;
            if (grade >= 9.5f) return 20;
            if (grade >= 9f) return 6;
            return 0;
        }

        /// <summary>The grade a grader prints on the slab: the exact condition to one decimal.</summary>
        public static float GradeOf(float condition) => (float)Math.Round(condition, 1);

        /// <summary>
        /// Suffix of the inventory icon variant. The 16 px icon only has room for four looks:
        /// pristine (Mint, Near Mint), light (Lightly Played), worn (Moderately and Heavily
        /// Played) and ruined (Damaged).
        /// </summary>
        public static string IconSuffix(float condition)
        {
            if (condition >= 8.5f) return "";
            if (condition >= 7f) return "_light";
            if (condition >= 3f) return "_worn";
            return "_ruined";
        }

        /// <summary>How damaged the card looks, 0 (pristine 10.00) to 1 (1.00), from the exact value.</summary>
        public static float WearSeverity(float condition) => Clamp((10f - condition) / 9f, 0f, 1f);

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
