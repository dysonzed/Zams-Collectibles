using System;
using System.Collections.Generic;
using System.Linq;

namespace ZamsCollectibles.AugWarriors
{
    /// <summary>How an NPC plays: how close to their glitch limit they push, how much they like hacking, when they call.</summary>
    public class AugPersonality
    {
        public string id;
        public int glitchBuffer;        // keeps Glitch this far under Stability (negative: gambles past it)
        public float hackWeight;        // how much hurting the opponent counts against building yourself
        public int callLead;            // calls the showdown when ahead by this much
        public float deckHackShare;     // share of hacks when building their deck
        public float deckGlitchyBias;   // positive: prefers high-Power, high-Glitch augments
        public float callGain = 2f;     // when ahead, calls unless a play would add at least this much
        public float modBonus;          // extra value on playing mods
        public float deckModWeight = 1.2f;
        public float deckTorsoWeight = 2f;
        public int lookahead;           // > 0: plays each option out this many times and picks the best (slow, strong)
        public bool bestDeck;           // builds the strongest deck it can from every card, instead of a random one

        public static readonly AugPersonality Cautious = new AugPersonality { id = "cautious", glitchBuffer = 1, hackWeight = 0.8f, callLead = 4, deckHackShare = 0.1f, deckGlitchyBias = -1f };
        public static readonly AugPersonality Reckless = new AugPersonality { id = "reckless", glitchBuffer = -1, hackWeight = 0.7f, callLead = 2, deckHackShare = 0.1f, deckGlitchyBias = 1.5f };
        public static readonly AugPersonality Hacker = new AugPersonality { id = "hacker", glitchBuffer = 0, hackWeight = 1.5f, callLead = 3, deckHackShare = 0.35f, deckGlitchyBias = 0f };
        // Loves stacking mods on their augments, in deck and in play.
        public static readonly AugPersonality Tinkerer = new AugPersonality { id = "tinkerer", glitchBuffer = 0, hackWeight = 0.8f, callLead = 3, deckHackShare = 0.1f, deckGlitchyBias = 0f, modBonus = 0.4f, deckModWeight = 2.2f };
        // Calls the showdown as soon as they have a lead, to rob you of your turns.
        public static readonly AugPersonality Shark = new AugPersonality { id = "shark", glitchBuffer = 0, hackWeight = 1f, callLead = 3, callGain = 4f, deckHackShare = 0.15f, deckGlitchyBias = 0.5f };
        // Builds a wall of Stability and plays the long game; rarely calls early.
        public static readonly AugPersonality Turtle = new AugPersonality { id = "turtle", glitchBuffer = 1, hackWeight = 0.8f, callLead = 5, callGain = 1f, deckHackShare = 0.08f, deckGlitchyBias = -1f, deckTorsoWeight = 3.5f };

        // Oracle, the Den's shark of sharks: sees every game through before moving, reads your hand,
        // and brings the strongest deck the card pool allows. Not in the random pool; one regular
        // always plays this way.
        public static readonly AugPersonality Oracle = new AugPersonality
        {
            id = "oracle", glitchBuffer = 0, hackWeight = 1f, callLead = 3, deckHackShare = 0.2f, deckGlitchyBias = 0f,
            lookahead = 40, bestDeck = true,
        };

        // The styles regulars and customers are dealt at random.
        public static readonly AugPersonality[] All = { Cautious, Reckless, Hacker, Tinkerer, Shark, Turtle };

        public static AugPersonality ById(string id) => id == Oracle.id ? Oracle : Array.Find(All, p => p.id == id);
    }

    /// <summary>A greedy player: tries every legal play, scores the result, and calls the showdown when comfortably ahead.</summary>
    public static class AugAI
    {
        public static AugAction Choose(AugMatch match, AugPersonality style)
        {
            if (style.lookahead > 0) return Search(match, style);
            AugPlayer me = match.Current, opponent = match.Opponent;
            float now = Value(me, style, called: false);
            float theirs = AugRules.Score(opponent, false).final;

            // The best play, even if it hurts: holding is only allowed with nothing playable.
            AugAction best = AugAction.Hold;
            float bestGain = float.NegativeInfinity;
            foreach (CardDef card in me.hand.Distinct())
            {
                foreach (Slot target in match.Targets(card))
                {
                    float gain = Gain(me, opponent, card, target, style, now, theirs);
                    if (gain > bestGain) { bestGain = gain; best = AugAction.Play(card, target); }
                }
            }

            // Call when comfortably ahead and the hand has little left to add, or on the last turn when ahead.
            if (!match.finalTurn)
            {
                float lead = AugRules.Score(me, true).final - theirs;
                bool lastTurn = me.turnsTaken >= AugRules.MAX_TURNS - 1;
                if (lead >= style.callLead && bestGain < style.callGain) return AugAction.Call;
                if (lastTurn && lead > 0 && bestGain < 1f) return AugAction.Call;
                if (lead > 0 && bestGain < 0f) return AugAction.Call;   // every play would hurt: stop while ahead
            }
            if (float.IsNegativeInfinity(bestGain)) return match.finalTurn ? AugAction.Hold : AugAction.Call;   // nothing playable
            return best;
        }

        // ------------------------------------------------------------ lookahead

        // How the searcher imagines the rest of the game going, for both sides.
        private static readonly AugPersonality Imagined = AugPersonality.Hacker;
        private static readonly Random SearchRng = new Random();

        /// <summary>
        /// Plays every legal option out to the end of the game, many times over, with both sides
        /// playing greedily, and picks the option that won most often. Every option is tried
        /// against the same shuffles so they're compared fairly. It knows the cards in its
        /// opponent's hand, but not the order of either draw pile.
        /// </summary>
        private static AugAction Search(AugMatch match, AugPersonality style)
        {
            List<AugAction> options = Options(match);
            if (options.Count == 1) return options[0];
            int me = match.current;
            int[] seeds = Enumerable.Range(0, style.lookahead).Select(_ => SearchRng.Next()).ToArray();
            AugAction best = options[0];
            float bestScore = float.NegativeInfinity;
            foreach (AugAction option in options)
            {
                float score = 0f;
                foreach (int seed in seeds)
                {
                    AugMatch sim = match.Clone(seed);
                    sim.Apply(option);
                    for (int guard = 0; !sim.over && guard < 100; guard++) sim.Apply(Choose(sim, Imagined));
                    score += sim.winner == me ? 1f : sim.winner == null ? 0.5f : 0f;
                }
                if (score > bestScore) { bestScore = score; best = option; }
            }
            return best;
        }

        // Every legal move: the greedy pick first, so it wins ties.
        private static List<AugAction> Options(AugMatch match)
        {
            List<AugAction> options = new List<AugAction> { Choose(match, Imagined) };
            foreach (CardDef card in match.Current.hand.Distinct())
                foreach (Slot target in match.Targets(card))
                    options.Add(AugAction.Play(card, target));
            if (match.IsLegal(AugAction.Call)) options.Add(AugAction.Call);
            if (match.IsLegal(AugAction.Hold)) options.Add(AugAction.Hold);
            return options.GroupBy(o => (o.kind, o.card, o.target)).Select(g => g.First()).ToList();
        }

        // How much a play improves my position: my value up, plus (weighted) their score down.
        private static float Gain(AugPlayer me, AugPlayer opponent, CardDef card, Slot target, AugPersonality style, float now, float theirs)
        {
            if (card.kind == AugRules.HACK)
            {
                AugPlayer them = opponent.CloneBody();
                Installed victim = them[target];
                if (card.effect == AugRules.SCRAP) them.body[(int)target] = null;
                else victim.hackGlitch += card.amount;
                return (theirs - AugRules.Score(them, false).final) * style.hackWeight;
            }

            AugPlayer after = me.CloneBody();
            if (card.kind == AugRules.AUGMENT) after.body[(int)target] = new Installed { augment = card };
            else after[target].mods.Add(card);
            float gain = Value(after, style, called: false) - now;
            if (card.effect == AugRules.DRAW) gain += 1f;
            if (card.kind == AugRules.MOD) gain += style.modBonus;
            return gain;
        }

        // My score, but treating getting close to the glitch limit as risky, by personality.
        private static float Value(AugPlayer p, AugPersonality style, bool called)
        {
            AugScore s = AugRules.Score(p, called);
            int headroom = s.stability - s.glitch;
            if (headroom < style.glitchBuffer) return s.final - (style.glitchBuffer - headroom) * 1.5f;
            return s.final;
        }

        /// <summary>A deck for an NPC, drawn from every card in the game, flavoured by personality.</summary>
        public static List<CardDef> BuildDeck(IList<CardDef> pool, AugPersonality style, Random rng, int size = 16)
        {
            if (style.bestDeck)
            {
                List<CardDef> best = StrongDeck(pool, rng);
                if (AugRules.IsLegalDeck(best)) return best;   // a thin card pool: fall back to a random deck
            }

            List<CardDef> deck = new List<CardDef>();
            Dictionary<CardDef, float> weights = pool.ToDictionary(c => c, c => DeckWeight(c, style));
            int guard = 0;
            while (deck.Count < size && guard++ < 500)
            {
                float total = weights.Values.Sum();
                float roll = (float)rng.NextDouble() * total;
                foreach (var w in weights)
                {
                    roll -= w.Value;
                    if (roll > 0f) continue;
                    if (deck.Count(c => c == w.Key) < AugRules.MAX_COPIES) deck.Add(w.Key);
                    break;
                }
            }
            return deck;
        }

        /// <summary>
        /// A strong deck from whatever cards exist, different every time: for each body part (two
        /// for the arms) it picks a card, strongly favouring the best ones, then a mod and a hack,
        /// mostly two copies each, and tops up with a few more picks. Small on purpose, so the good
        /// cards come up every game.
        /// </summary>
        public static List<CardDef> StrongDeck(IList<CardDef> pool, Random rng)
        {
            List<CardDef> deck = new List<CardDef>();
            void Pick(IEnumerable<CardDef> cards)
            {
                // weight e^(2 x strength): a card 1 point stronger is about 7 times as likely
                List<CardDef> open = cards.Where(c => deck.Count(d => d == c) < AugRules.MAX_COPIES).ToList();
                if (open.Count == 0) return;
                List<double> weights = open.Select(c => Math.Exp(2.0 * CardStrength(c))).ToList();
                double roll = rng.NextDouble() * weights.Sum();
                int i = 0;
                while (i < open.Count - 1 && (roll -= weights[i]) > 0) i++;
                deck.Add(open[i]);
                if (rng.Next(100) < 80 && deck.Count(d => d == open[i]) < AugRules.MAX_COPIES) deck.Add(open[i]);
            }
            IEnumerable<CardDef> Augments(string slot) => pool.Where(c => c.kind == AugRules.AUGMENT && c.slot == slot);
            Pick(Augments("torso"));
            Pick(Augments("arm"));
            Pick(Augments("arm"));
            Pick(Augments("head"));
            Pick(Augments("legs"));
            Pick(pool.Where(c => c.kind == AugRules.MOD));
            Pick(pool.Where(c => c.kind == AugRules.HACK));
            for (int extra = 0; extra < 3 || deck.Count < AugRules.MIN_DECK; extra++)
            {
                if (extra > 20) break;
                Pick(pool);
            }
            return deck;
        }

        // A rough worth for deck building: Power and Stability are good, Glitch is bad, effects add.
        private static float CardStrength(CardDef c)
        {
            if (c.kind == AugRules.HACK) return c.effect == AugRules.SCRAP ? 4f : 1.2f * c.amount;
            float worth = c.power + 0.6f * c.stability - 0.8f * c.glitch;
            switch (c.effect)
            {
                case AugRules.DRAW: worth += c.amount; break;
                case AugRules.HACK_IMMUNE: worth += 2f; break;
                case AugRules.POWER_PER_MOD: worth += c.amount; break;
                case AugRules.POWER_PER_STABLE: worth += 2f * c.amount; break;
                case AugRules.POWER_IF_CALLED: worth += 0.5f * c.amount; break;
                case AugRules.SOFT_COMPROMISE: worth += 1f; break;
            }
            return worth;
        }

        private static float DeckWeight(CardDef c, AugPersonality style)
        {
            switch (c.kind)
            {
                case AugRules.HACK: return 10f * style.deckHackShare;
                case AugRules.MOD: return style.deckModWeight;
                default:
                    float w = c.slot == "torso" ? style.deckTorsoWeight : c.slot == "arm" ? 2.5f : 1.5f;   // bodies need torsos and two arms
                    return Math.Max(0.2f, w + style.deckGlitchyBias * (c.glitch - 1) * 0.4f);
            }
        }
    }
}
