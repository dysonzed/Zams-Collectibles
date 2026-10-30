using System;
using System.Collections.Generic;
using System.Linq;

namespace ZamsCollectibles.AugWarriors
{
    /// <summary>
    /// The Downside Den's Aug Warriors regulars. Each save gets its own pool, made on the first
    /// visit: random names, each with a random play style the player never sees. Some nights
    /// nobody is at the back table; otherwise one to three of them are, the same all night.
    ///
    /// Oracle is in every save's pool, always with the Oracle style, and has two tickets in
    /// the nightly draw of who turns up.
    /// </summary>
    public static class DenRegulars
    {
        private const int POOL_SIZE = 6;
        private const int AVAILABLE_CHANCE = 50;   // percent of nights someone is in
        private const int MAX_TONIGHT = 3;
        private const string ORACLE = "Oracle";

        private const string POOL_KEY = "den_regulars";            // "name:style;name:style"
        private const string TONIGHT_KEY = "den_regulars_tonight"; // pool indices, "0,4"; empty: nobody
        private const string DAY_KEY = "den_regulars_day";

        // Street handles: proper nouns, not localized.
        private static readonly string[] Names =
        {
            "Rook", "Vex", "Patch", "Mags", "Tiny", "Sprocket", "Lux", "Dregs", "Kit", "Bolt", "Nix", "Juno",
            "Static", "Pike", "Wren", "Gutter", "Moth", "Shiv", "Ozzy", "Fen", "Rivet", "Cass", "Dot", "Hex",
        };

        /// <summary>This save's regulars, made on first use, with Oracle last.</summary>
        public static List<(string name, AugPersonality style)> Pool()
        {
            List<(string, AugPersonality)> pool = Parse(ModHelper.GetModData(ModEntry.MOD_ID, POOL_KEY, ""));
            if (pool.Count == 0)
            {
                pool = Names.OrderBy(_ => RNG.GetRandomInt(0, 9999)).Take(POOL_SIZE)
                    .Select(n => (n, RNG.GetRandomElement(AugPersonality.All))).ToList();
                ModHelper.SetModData(ModEntry.MOD_ID, POOL_KEY, string.Join(";", pool.Select(r => r.Item1 + ":" + r.Item2.id)));
            }
            pool.Add((ORACLE, AugPersonality.Oracle));
            return pool;
        }

        /// <summary>Who's at the back table tonight: rolled once per night, possibly nobody.</summary>
        public static List<(string name, AugPersonality style)> Tonight()
        {
            List<(string name, AugPersonality style)> pool = Pool();
            int day = StoreStation.GetDayCounter();
            if (ModHelper.GetModDataInt(ModEntry.MOD_ID, DAY_KEY, -1) != day)
            {
                string picked = "";
                if (RNG.Roll(AVAILABLE_CHANCE))
                {
                    // one ticket each, two for Oracle (last in the pool)
                    IEnumerable<int> tickets = Enumerable.Range(0, pool.Count).Append(pool.Count - 1);
                    picked = string.Join(",", tickets.OrderBy(_ => RNG.GetRandomInt(0, 9999)).Distinct().Take(RNG.GetRandomInt(1, MAX_TONIGHT)));
                }
                ModHelper.SetModData(ModEntry.MOD_ID, TONIGHT_KEY, picked);
                ModHelper.SetModDataInt(ModEntry.MOD_ID, DAY_KEY, day);
            }
            return ModHelper.GetModData(ModEntry.MOD_ID, TONIGHT_KEY, "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(i => int.TryParse(i, out int n) ? n : -1)
                .Where(n => n >= 0 && n < pool.Count)
                .Select(n => pool[n]).ToList();
        }

        /// <summary>Debug: rerolls tonight, with someone guaranteed to be in.</summary>
        public static void ForceSomeoneTonight()
        {
            int count = RNG.GetRandomInt(1, MAX_TONIGHT);
            ModHelper.SetModData(ModEntry.MOD_ID, TONIGHT_KEY, string.Join(",", Enumerable.Range(0, Pool().Count).OrderBy(_ => RNG.GetRandomInt(0, 9999)).Take(count)));
            ModHelper.SetModDataInt(ModEntry.MOD_ID, DAY_KEY, StoreStation.GetDayCounter());
        }

        /// <summary>Debug: the pool with the hidden styles, and who's in tonight.</summary>
        public static string Describe() =>
            "pool: " + string.Join(", ", Pool().Select(r => $"{r.name} ({r.style.id})"))
            + " | tonight: " + (Tonight().Count == 0 ? "nobody" : string.Join(", ", Tonight().Select(r => r.name)));

        private static List<(string, AugPersonality)> Parse(string saved)
        {
            List<(string, AugPersonality)> pool = new List<(string, AugPersonality)>();
            foreach (string entry in (saved ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split(':');
                AugPersonality style = parts.Length == 2 ? AugPersonality.ById(parts[1]) : null;
                if (style != null) pool.Add((parts[0], style));
            }
            return pool;
        }
    }
}
