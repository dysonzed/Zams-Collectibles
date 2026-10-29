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
        // The standard TCG condition scale; labels are localization keys.
        private static readonly (float min, string key, float multiplier)[] Labels =
        {
            (9.5f, "condition_mint",              1.5f),
            (8.5f, "condition_near_mint",         1.25f),
            (7.0f, "condition_lightly_played",    1.0f),
            (5.0f, "condition_moderately_played", 0.75f),
            (3.0f, "condition_heavily_played",    0.5f),
            (0.0f, "condition_damaged",           0.25f),
        };

        /// <summary>Condition of a card fresh out of a pack: mostly 8 to 10, occasionally worse.</summary>
        public static float RollFreshCondition()
        {
            // half-normal below 10, sigma 1.2
            double u1 = 1.0 - RNG.random.NextDouble();
            double u2 = RNG.random.NextDouble();
            double gaussian = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            float condition = 10f - (float)Math.Abs(gaussian) * 1.2f;
            return (float)Math.Round(Clamp(condition, 1f, 10f), 2);
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

        /// <summary>Graded cards sell on the exact number: a gem 10 is worth far more than a 9.</summary>
        public static float GradedMultiplier(float grade)
        {
            if (grade >= 10f) return 6f;
            if (grade >= 9.5f) return 3.5f;
            if (grade >= 9f) return 2.25f;
            if (grade >= 8f) return 1.5f;
            if (grade >= 7f) return 1.1f;
            return RawMultiplier(grade) * 0.9f;
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
