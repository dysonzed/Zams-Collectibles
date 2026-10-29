using System.Collections.Generic;

namespace ZamsCollectibles
{
    /// <summary>
    /// Player-facing text. UI strings live in Localization/&lt;locale&gt;.csv and are read with the
    /// game's GetLocalized (current language, then en). Card content (card, set and rarity names,
    /// rules, flavour) is written in Data/sets.json in English; a locale file can override any of
    /// it with the keys below, and anything without a translation falls back to sets.json silently.
    ///
    ///   set_&lt;set&gt;_name, rarity_&lt;rarity&gt;, card_type_&lt;type&gt;,
    ///   card_&lt;set&gt;_&lt;number&gt;_name / _text / _flavor   (number as in the art key, e.g. card_scrapyard_01_name)
    /// </summary>
    public static class Loc
    {
        public static string Get(string key, params object[] args) => ModHelper.GetLocalized(ModEntry.MOD_ID, key, args);

        /// <summary>The translation of key if any locale file has it, else the fallback (no warning).</summary>
        public static string Optional(string key, string fallback)
        {
            LoadedMod mod = ModLoader.Mods.Find(m => m.Manifest.ID == ModEntry.MOD_ID);
            if (mod == null) return fallback;
            string code = ModHelper.GetCurrentLocale()?.Identifier.Code ?? "en";
            if (Lookup(mod, code, key, out string text) || Lookup(mod, "en", key, out text)) return text;
            return fallback;
        }

        private static bool Lookup(LoadedMod mod, string locale, string key, out string text)
        {
            text = null;
            return mod.Localization.TryGetValue(locale, out Dictionary<string, string> table) && table.TryGetValue(key, out text);
        }

        // ------------------------------------------------------------ card content

        public static string SetName(CardSetDef set) => Optional($"set_{set.id}_name", set.name);

        public static string RarityName(string rarityId) =>
            Optional($"rarity_{rarityId}", CardDatabase.GetRarity(rarityId)?.name ?? rarityId);

        /// <summary>"Arm Augment", "Torso Mod", "Mod", "Hack".</summary>
        public static string CardType(CardDef card)
        {
            switch (card.kind)
            {
                case "augment": return Get("card_kind_augment", SlotName(card.slot));
                case "mod": return card.modFor == null || card.modFor == "any" ? Get("card_kind_mod_any") : Get("card_kind_mod", SlotName(card.modFor));
                case "hack": return Get("card_kind_hack");
                default: return "";
            }
        }

        public static string SlotName(string slot) => string.IsNullOrEmpty(slot) ? "" : Get($"slot_{slot}");

        public static string CardTitle(CardDef card) => Optional(CardKey(card, "name"), card.name);

        public static string CardRules(CardDef card) => Optional(CardKey(card, "text"), card.text ?? "");

        public static string CardFlavor(CardDef card) => Optional(CardKey(card, "flavor"), card.flavor ?? "");

        private static string CardKey(CardDef card, string field) => $"card_{card.set.id}_{card.number:00}_{field}";
    }
}
