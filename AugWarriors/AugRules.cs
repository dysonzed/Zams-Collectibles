using System;
using System.Collections.Generic;
using System.Linq;

namespace ZamsCollectibles.AugWarriors
{
    /// <summary>
    /// Aug Warriors rules. Two mercenaries fill a five-slot body (head, torso, two arms, legs)
    /// with augments. Every augment adds Power and Glitch; the torso and some mods add
    /// Stability. At the showdown the higher Power wins, but anyone whose Glitch is over their
    /// Stability is Compromised and keeps only half their Power.
    ///
    /// A turn: draw a card (hand limit 4), then play one card, hold, or call the showdown.
    /// Calling gives the opponent one last turn, with two extra cards drawn first. The showdown
    /// also happens on its own once both players have had MAX_TURNS turns.
    ///
    /// Plain C#, no game types: the AI and offline simulations run the same code.
    /// </summary>
    public enum Slot { Head, Torso, ArmLeft, ArmRight, Legs }

    public class Installed
    {
        public CardDef augment;
        public readonly List<CardDef> mods = new List<CardDef>();
        public int hackGlitch;      // Glitch added to it by opponents' hacks

        public int Glitch => Math.Max(0, augment.glitch + mods.Sum(m => m.glitch) + hackGlitch);

        public Installed Clone()
        {
            Installed copy = new Installed { augment = augment, hackGlitch = hackGlitch };
            copy.mods.AddRange(mods);
            return copy;
        }
    }

    public class AugPlayer
    {
        public readonly string name;
        public readonly List<CardDef> deck;
        public readonly List<CardDef> hand = new List<CardDef>();
        public readonly Installed[] body = new Installed[AugRules.SLOTS];
        public int turnsTaken;

        public AugPlayer(string name, IEnumerable<CardDef> deck)
        {
            this.name = name;
            this.deck = deck.ToList();
        }

        public Installed this[Slot slot] => body[(int)slot];

        public bool HackImmune => body.Any(i => i != null && i.augment.effect == AugRules.HACK_IMMUNE);

        public AugPlayer CloneBody(IEnumerable<CardDef> withDeck = null)
        {
            AugPlayer copy = new AugPlayer(name, withDeck ?? Enumerable.Empty<CardDef>());
            for (int i = 0; i < body.Length; i++) copy.body[i] = body[i]?.Clone();
            return copy;
        }
    }

    public enum ActionKind { Play, Hold, CallShowdown }

    public struct AugAction
    {
        public ActionKind kind;
        public CardDef card;
        public Slot target;     // own slot for augments and mods, the opponent's for hacks

        public static AugAction Play(CardDef card, Slot target) => new AugAction { kind = ActionKind.Play, card = card, target = target };
        public static AugAction Hold => new AugAction { kind = ActionKind.Hold };
        public static AugAction Call => new AugAction { kind = ActionKind.CallShowdown };
    }

    /// <summary>A line for the match log: a localization key and its arguments (names, cards, numbers).</summary>
    public struct AugEvent
    {
        public string key;
        public object[] args;
        public AugEvent(string key, params object[] args) { this.key = key; this.args = args; }
    }

    public struct AugScore
    {
        public int power, glitch, stability, final;
        public bool compromised;
    }

    public class AugMatch
    {
        public readonly AugPlayer[] players;
        public int current;                 // whose turn it is
        public int? caller;                 // who called the showdown
        public bool finalTurn;              // the turn after a call
        public bool over;
        public int? winner;                 // null on a draw
        public readonly AugScore[] scores = new AugScore[2];
        public readonly List<AugEvent> log = new List<AugEvent>();
        private readonly Random rng;

        public AugMatch(AugPlayer first, AugPlayer second, int seed)
        {
            players = new[] { first, second };
            rng = new Random(seed);
            foreach (AugPlayer p in players)
            {
                Shuffle(p.deck);
                for (int i = 0; i < AugRules.START_HAND; i++) Draw(p);
            }
            StartTurn();
        }

        private AugMatch(AugPlayer[] players, int seed)
        {
            this.players = players;
            rng = new Random(seed);
        }

        /// <summary>
        /// A copy to play ahead in, for the AI's lookahead. Both draw piles are reshuffled: the
        /// copy knows what's left in them but not the order.
        /// </summary>
        public AugMatch Clone(int seed)
        {
            AugPlayer[] copies = players.Select(p =>
            {
                AugPlayer c = p.CloneBody(p.deck);
                c.hand.AddRange(p.hand);
                c.turnsTaken = p.turnsTaken;
                return c;
            }).ToArray();
            AugMatch copy = new AugMatch(copies, seed)
            {
                current = current, caller = caller, finalTurn = finalTurn, over = over, winner = winner,
            };
            foreach (AugPlayer p in copies) copy.Shuffle(p.deck);
            return copy;
        }

        public AugPlayer Current => players[current];
        public AugPlayer Opponent => players[1 - current];
        public int Round => Math.Min(players[0].turnsTaken, players[1].turnsTaken) + 1;

        // ------------------------------------------------------------ actions

        /// <summary>Where a card can be played this turn: own slots for augments and mods, the opponent's for hacks.</summary>
        public List<Slot> Targets(CardDef card) => AugRules.Targets(Current, Opponent, card);

        /// <summary>Whether the current player has any card they can play.</summary>
        public bool HasPlay() => Current.hand.Any(c => Targets(c).Count > 0);

        // You must play a card or call the showdown. With nothing playable you must call; on the
        // last turn (no second call) the turn simply ends.
        public bool IsLegal(AugAction action)
        {
            if (over) return false;
            switch (action.kind)
            {
                case ActionKind.Hold: return finalTurn && !HasPlay();   // nothing playable on the last turn: it just ends
                case ActionKind.CallShowdown: return !finalTurn;
                default: return Current.hand.Contains(action.card) && Targets(action.card).Contains(action.target);
            }
        }

        public void Apply(AugAction action)
        {
            if (!IsLegal(action)) throw new InvalidOperationException("illegal Aug Warriors action");
            AugPlayer me = Current;
            switch (action.kind)
            {
                case ActionKind.Hold:
                    log.Add(new AugEvent("aug_log_hold", me.name));
                    break;
                case ActionKind.CallShowdown:
                    caller = current;
                    log.Add(new AugEvent("aug_log_call", me.name));
                    break;
                case ActionKind.Play:
                    me.hand.Remove(action.card);
                    Play(me, Opponent, action.card, action.target);
                    break;
            }
            EndTurn();
        }

        private void Play(AugPlayer me, AugPlayer opponent, CardDef card, Slot target)
        {
            switch (card.kind)
            {
                case AugRules.AUGMENT:
                    Installed old = me[target];
                    me.body[(int)target] = new Installed { augment = card };
                    log.Add(old == null
                        ? new AugEvent("aug_log_install", me.name, card)
                        : new AugEvent("aug_log_replace", me.name, card, old.augment));
                    if (card.effect == AugRules.DRAW)
                        for (int i = 0; i < card.amount; i++) Draw(me);
                    break;
                case AugRules.MOD:
                    me[target].mods.Add(card);
                    log.Add(new AugEvent("aug_log_mod", me.name, card, me[target].augment));
                    break;
                case AugRules.HACK:
                    Installed victim = opponent[target];
                    if (card.effect == AugRules.SCRAP)
                    {
                        opponent.body[(int)target] = null;
                        log.Add(new AugEvent("aug_log_scrap", me.name, card, victim.augment));
                    }
                    else
                    {
                        victim.hackGlitch += card.amount;
                        log.Add(new AugEvent("aug_log_hack", me.name, card, victim.augment, card.amount));
                    }
                    break;
            }
        }

        // ------------------------------------------------------------ turns

        private void StartTurn()
        {
            if (Current.hand.Count < AugRules.HAND_LIMIT) Draw(Current);
        }

        private void EndTurn()
        {
            Current.turnsTaken++;
            if (finalTurn) { Showdown(); return; }

            if (caller.HasValue)
            {
                // the caller's opponent gets one last turn, with extra cards
                finalTurn = true;
                current = 1 - current;
                for (int i = 0; i < AugRules.FINAL_DRAW; i++) Draw(Current);
                return;
            }

            if (players.All(p => p.turnsTaken >= AugRules.MAX_TURNS)) { Showdown(); return; }
            current = 1 - current;
            StartTurn();
        }

        private void Showdown()
        {
            over = true;
            for (int i = 0; i < 2; i++) scores[i] = AugRules.Score(players[i], caller == i);
            int a = scores[0].final, b = scores[1].final;
            if (a != b) winner = a > b ? 0 : 1;
            else if (caller.HasValue) winner = 1 - caller.Value;   // ties go to whoever didn't call
            else winner = null;
            log.Add(new AugEvent("aug_log_showdown"));
        }

        private void Draw(AugPlayer p)
        {
            if (p.deck.Count == 0) return;
            p.hand.Add(p.deck[0]);
            p.deck.RemoveAt(0);
        }

        private void Shuffle(List<CardDef> cards)
        {
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (cards[i], cards[j]) = (cards[j], cards[i]);
            }
        }
    }

    public static class AugRules
    {
        public const int SLOTS = 5;
        public const int START_HAND = 4;
        public const int HAND_LIMIT = 4;
        public const int MAX_TURNS = 6;         // each; then the showdown happens on its own
        public const int FINAL_DRAW = 2;        // extra cards for the last turn after a call
        public const int BASE_STABILITY = 3;
        public const int MAX_MODS = 2;          // per augment

        public const int MIN_DECK = 10;
        public const int MAX_DECK = 20;
        public const int MAX_COPIES = 2;

        public const string AUGMENT = "augment", MOD = "mod", HACK = "hack";

        // effects
        public const string DRAW = "draw";                          // augment: draw N when installed
        public const string HACK_IMMUNE = "hack_immune";            // augment: hacks can't target you
        public const string POWER_PER_MOD = "power_per_mod";        // augment: +N per mod on it
        public const string POWER_PER_STABLE = "power_per_stable";  // augment: +N per other augment with 0 Glitch
        public const string POWER_IF_CALLED = "power_if_called";    // augment: +N if you called the showdown
        public const string SOFT_COMPROMISE = "soft_compromise";    // augment: Compromised costs N Power instead of half
        public const string GLITCH = "glitch";                      // hack: +N Glitch on the target
        public const string SCRAP = "scrap";                        // hack: remove the target and its mods

        public static Slot[] SlotsFor(string slot)
        {
            switch (slot)
            {
                case "head": return new[] { Slot.Head };
                case "torso": return new[] { Slot.Torso };
                case "arm": return new[] { Slot.ArmLeft, Slot.ArmRight };
                case "legs": return new[] { Slot.Legs };
                default: return new Slot[0];
            }
        }

        public static string SlotKind(Slot slot) =>
            slot == Slot.Head ? "head" : slot == Slot.Torso ? "torso" : slot == Slot.Legs ? "legs" : "arm";

        public static List<Slot> Targets(AugPlayer me, AugPlayer opponent, CardDef card)
        {
            switch (card.kind)
            {
                case AUGMENT:
                    return SlotsFor(card.slot).ToList();
                case MOD:
                    return Enumerable.Range(0, SLOTS).Select(i => (Slot)i).Where(s =>
                    {
                        Installed host = me[s];
                        return host != null && host.mods.Count < MAX_MODS && (card.modFor == null || card.modFor == "any" || card.modFor == SlotKind(s));
                    }).ToList();
                case HACK:
                    if (opponent.HackImmune) return new List<Slot>();
                    return Enumerable.Range(0, SLOTS).Select(i => (Slot)i).Where(s => opponent[s] != null).ToList();
                default:
                    return new List<Slot>();
            }
        }

        public static AugScore Score(AugPlayer p, bool called)
        {
            AugScore s = new AugScore { stability = BASE_STABILITY };
            int softPenalty = -1;
            for (int i = 0; i < SLOTS; i++)
            {
                Installed slot = p.body[i];
                if (slot == null) continue;
                CardDef aug = slot.augment;
                s.power += aug.power + slot.mods.Sum(m => m.power);
                s.glitch += slot.Glitch;
                s.stability += aug.stability + slot.mods.Sum(m => m.stability);
                switch (aug.effect)
                {
                    case POWER_PER_MOD: s.power += aug.amount * slot.mods.Count; break;
                    case POWER_IF_CALLED: if (called) s.power += aug.amount; break;
                    case POWER_PER_STABLE:
                        s.power += aug.amount * p.body.Count(o => o != null && o != slot && o.Glitch == 0);
                        break;
                    case SOFT_COMPROMISE: softPenalty = aug.amount; break;
                }
            }
            s.compromised = s.glitch > s.stability;
            s.final = !s.compromised ? s.power : softPenalty >= 0 ? Math.Max(0, s.power - softPenalty) : s.power / 2;
            return s;
        }

        /// <summary>Everything that makes a list of cards an illegal deck; empty when it is legal.</summary>
        public static List<AugEvent> DeckErrors(IList<CardDef> deck)
        {
            List<AugEvent> errors = new List<AugEvent>();
            if (deck.Count < MIN_DECK) errors.Add(new AugEvent("aug_deck_too_small", MIN_DECK, deck.Count));
            if (deck.Count > MAX_DECK) errors.Add(new AugEvent("aug_deck_too_big", MAX_DECK, deck.Count));
            foreach (var group in deck.GroupBy(c => c).Where(g => g.Count() > MAX_COPIES))
                errors.Add(new AugEvent("aug_deck_copies", MAX_COPIES, group.Key, group.Count()));
            return errors;
        }

        /// <summary>Legal but likely weak: a body that can't be filled.</summary>
        public static List<AugEvent> DeckWarnings(IList<CardDef> deck)
        {
            List<AugEvent> warnings = new List<AugEvent>();
            if (!deck.Any(c => c.kind == AUGMENT && c.slot == "torso")) warnings.Add(new AugEvent("aug_deck_no_torso"));
            if (deck.Count(c => c.kind == AUGMENT && c.slot == "arm") < 2) warnings.Add(new AugEvent("aug_deck_few_arms"));
            if (!deck.Any(c => c.kind == AUGMENT && c.slot == "head")) warnings.Add(new AugEvent("aug_deck_no_head"));
            if (!deck.Any(c => c.kind == AUGMENT && c.slot == "legs")) warnings.Add(new AugEvent("aug_deck_no_legs"));
            if (deck.Count(c => c.kind == MOD) > deck.Count(c => c.kind == AUGMENT)) warnings.Add(new AugEvent("aug_deck_mod_heavy"));
            return warnings;
        }

        public static bool IsLegalDeck(IList<CardDef> deck) => DeckErrors(deck).Count == 0;
    }
}
