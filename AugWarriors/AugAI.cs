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

        public static readonly AugPersonality Cautious = new AugPersonality { id = "cautious", glitchBuffer = 1, hackWeight = 0.8f, callLead = 4, deckHackShare = 0.1f, deckGlitchyBias = -1f };
        public static readonly AugPersonality Reckless = new AugPersonality { id = "reckless", glitchBuffer = -1, hackWeight = 0.7f, callLead = 2, deckHackShare = 0.1f, deckGlitchyBias = 1.5f };
        public static readonly AugPersonality Hacker = new AugPersonality { id = "hacker", glitchBuffer = 0, hackWeight = 1.5f, callLead = 3, deckHackShare = 0.35f, deckGlitchyBias = 0f };
    }

    /// <summary>A greedy player: tries every legal play, scores the result, and calls the showdown when comfortably ahead.</summary>
    public static class AugAI
    {
        public static AugAction Choose(AugMatch match, AugPersonality style)
        {
            AugPlayer me = match.Current, opponent = match.Opponent;
            float now = Value(me, style, called: false);
            float theirs = AugRules.Score(opponent, false).final;

            AugAction best = AugAction.Hold;
            float bestGain = 0f;
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
                if (lead >= style.callLead && bestGain < 2f) return AugAction.Call;
                if (lastTurn && lead > 0 && bestGain < 1f) return AugAction.Call;
            }
            return best;
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

        private static float DeckWeight(CardDef c, AugPersonality style)
        {
            switch (c.kind)
            {
                case AugRules.HACK: return 10f * style.deckHackShare;
                case AugRules.MOD: return 1.2f;
                default:
                    float w = c.slot == "torso" ? 2f : c.slot == "arm" ? 2.5f : 1.5f;   // bodies need torsos and two arms
                    return Math.Max(0.2f, w + style.deckGlitchyBias * (c.glitch - 1) * 0.4f);
            }
        }
    }
}
