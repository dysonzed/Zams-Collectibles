using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ZamsCollectibles
{
    // Shapes of Data/sets.json, read with the game's Newtonsoft.Json. (Unity's JsonUtility
    // silently returns empty objects for types in a runtime-loaded mod assembly.)

    [Serializable]
    public class CardDatabaseFile
    {
        public RarityDef[] rarities;
        public CardSetDef[] sets;
    }

    [Serializable]
    public class RarityDef
    {
        public string id;
        public string name;
        public int value;           // base value of a card of this rarity in average (Excellent) condition
        public string color;        // RenderHandler.ColorPalette name used in the tooltip
        public string gem;          // #rrggbb of the rarity gem on the card face
        public float shopWeight;    // how often the Downside Den stocks singles of this rarity
    }

    [Serializable]
    public class CardSetDef
    {
        public string id;
        public string name;
        public string code;         // short code printed with the card number, e.g. SCR 003/012
        public string cardSprite;   // Textures/Items key shared by every card of the set
        public string packSprite;   // Textures/Items key of the set's booster pack (default: booster_pack)
        public string frameColor;   // card face frame: red, teal, purple or slate
        public int tier = 1;                 // power tier: higher tiers are licensed through Wild's Network
        public float priceMultiplier = 1f;   // scales the rarity values of this set's cards: later sets and tiers cost more
        public int packValue;
        public float sealedMarkup;  // sealed-set value = sum of the card values * (1 + this)
        public int boxPacks;        // booster packs in a booster box
        public int boxValue;        // value of a sealed booster box
        public PackSlotDef[] packSlots;
        public CardDef[] cards;
    }

    [Serializable]
    public class PackSlotDef
    {
        public int count;
        public string[] rarities;
        public float[] weights;
    }

    [Serializable]
    public class CardDef
    {
        public int number;
        public string name;
        public string rarity;

        // Aug Warriors rules (see AugWarriors/AugRules.cs)
        public string kind;         // augment, mod or hack
        public string slot;         // augments: head, torso, arm or legs
        public string modFor;       // mods: the slot they attach to, or "any"
        public int power;
        public int glitch;          // may be negative on mods (they stabilise)
        public int stability;       // raises the glitch limit
        public string effect;       // special rule id, see AugRules
        public int amount;          // the effect's number

        public string text;
        public string flavor;
        public int value;           // optional override of the rarity's base value (0 = use rarity)
        public string art;          // optional Textures/Items key of the card art (default art_<set>_<number>, e.g. art_scrapyard_04)

        [JsonIgnore] public CardSetDef set;
    }

    /// <summary>Loads Data/sets.json from the mod folder once and indexes it.</summary>
    public static class CardDatabase
    {
        public const string DATA_FILE = "Data/sets.json";

        public static readonly List<CardSetDef> Sets = new List<CardSetDef>();
        public static readonly Dictionary<string, CardSetDef> SetsById = new Dictionary<string, CardSetDef>();
        public static readonly Dictionary<string, RarityDef> Rarities = new Dictionary<string, RarityDef>();

        private static bool loaded;

        public static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;

            string json = ModHelper.ReadModFileText(ModEntry.MOD_ID, DATA_FILE);
            if (string.IsNullOrEmpty(json))
            {
                ModEntry.Instance.Log.Error($"{DATA_FILE} is missing or empty; no cards registered.");
                return;
            }

            CardDatabaseFile file;
            try
            {
                file = JsonConvert.DeserializeObject<CardDatabaseFile>(json);
            }
            catch (JsonException e)
            {
                ModEntry.Instance.Log.Error($"{DATA_FILE} could not be read: {e.Message}");
                return;
            }
            if (file?.sets == null || file.sets.Length == 0)
            {
                ModEntry.Instance.Log.Error($"{DATA_FILE} contains no sets; no cards registered.");
                return;
            }
            foreach (RarityDef rarity in file.rarities ?? new RarityDef[0])
                Rarities[rarity.id] = rarity;

            foreach (CardSetDef set in file.sets ?? new CardSetDef[0])
            {
                if (SetsById.ContainsKey(set.id))
                {
                    ModEntry.Instance.Log.Warning($"Duplicate set id '{set.id}' skipped.");
                    continue;
                }
                foreach (CardDef card in set.cards)
                {
                    card.set = set;
                    if (!Rarities.ContainsKey(card.rarity))
                        ModEntry.Instance.Log.Warning($"Card {set.id} #{card.number} has unknown rarity '{card.rarity}'.");
                }
                Sets.Add(set);
                SetsById[set.id] = set;
            }

            int cardCount = 0;
            Sets.ForEach(s => cardCount += s.cards.Length);
            ModEntry.Instance.Log.Log($"Loaded {Sets.Count} set(s), {cardCount} card(s).");
        }

        public static RarityDef GetRarity(string id) =>
            id != null && Rarities.TryGetValue(id, out RarityDef rarity) ? rarity : null;

        public static int BaseValue(CardDef card)
        {
            if (card.value > 0) return card.value;
            float multiplier = card.set != null && card.set.priceMultiplier > 0f ? card.set.priceMultiplier : 1f;
            return Math.Max(1, (int)Math.Round((GetRarity(card.rarity)?.value ?? 1) * multiplier));
        }

        public static CardDef RollCard(CardSetDef set, PackSlotDef slot)
        {
            string rarity = slot.rarities[0];
            float total = 0f;
            for (int i = 0; i < slot.rarities.Length; i++) total += Weight(slot, i);
            float roll = RNG.GetRandomFloat(0f, total);
            for (int i = 0; i < slot.rarities.Length; i++)
            {
                roll -= Weight(slot, i);
                if (roll <= 0f) { rarity = slot.rarities[i]; break; }
            }

            List<CardDef> pool = new List<CardDef>();
            foreach (CardDef card in set.cards)
                if (card.rarity == rarity) pool.Add(card);
            if (pool.Count == 0) pool.AddRange(set.cards);
            return RNG.GetRandomElement(pool);
        }

        private static float Weight(PackSlotDef slot, int i) =>
            slot.weights != null && i < slot.weights.Length ? slot.weights[i] : 1f;
    }
}
