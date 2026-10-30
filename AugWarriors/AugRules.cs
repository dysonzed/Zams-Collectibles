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
        public int turnInstalled;   // the owner's turns taken before it went in
        public bool suffocated;     // gains no Power from mods
        public bool voided;         // counts 0 Power

        public int Glitch => Math.Max(0, augment.glitch + mods.Sum(m => m.glitch) + hackGlitch);

        public Installed Clone()
        {
            Installed copy = new Installed { augment = augment, hackGlitch = hackGlitch, turnInstalled = turnInstalled, suffocated = suffocated, voided = voided };
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
        public int looseGlitch;     // Glitch not on any augment (backlash, siphons)
        public int stabilityLoss;   // Stability drained by hacks
        public int hackShields;     // hacks still to be blocked

        public AugPlayer(string name, IEnumerable<CardDef> deck)
        {
            this.name = name;
            this.deck = deck.ToList();
        }

        public Installed this[Slot slot] => body[(int)slot];

        public bool HackImmune => HasEffect(AugRules.HACK_IMMUNE);

        public bool HasEffect(string effect) => body.Any(i => i != null && i.augment.effect == effect);

        public int EffectTotal(string effect) => body.Where(i => i != null && i.augment.effect == effect).Sum(i => i.augment.amount);

        /// <summary>A copy of the board state (body, counters), with the given draw pile.</summary>
        public AugPlayer CloneBody(IEnumerable<CardDef> withDeck = null)
        {
            AugPlayer copy = new AugPlayer(name, withDeck ?? Enumerable.Empty<CardDef>())
            {
                turnsTaken = turnsTaken, looseGlitch = looseGlitch, stabilityLoss = stabilityLoss, hackShields = hackShields,
            };
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
            AugRules.Resolve(me, opponent, card, target, log);
            if (card.kind == AugRules.AUGMENT && card.effect == AugRules.DRAW)
                for (int i = 0; i < card.amount; i++) Draw(me);
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
            for (int i = 0; i < 2; i++) scores[i] = AugRules.Score(players[i], caller == i, players[1 - i]);
            int a = scores[0].final, b = scores[1].final;
            if (a != b) winner = a > b ? 0 : 1;
            else if (caller.HasValue) winner = 1 - caller.Value;   // ties go to whoever didn't call
            else winner = null;
            log.Add(new AugEvent("aug_log_showdown"));
        }

        private void Draw(AugPlayer p)
        {
            if (p.deck.Count == 0 || p.hand.Count >= AugRules.MAX_HAND) return;
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
        public const int HAND_LIMIT = 4;        // the turn's draw only tops the hand up to this
        public const int MAX_HAND = 12;         // no draw goes past this (extra draws from effects and the final turn)
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
        public const string POWER_PER_OTHER_MOD = "power_per_other_mod";  // augment: +N per mod on your other augments
        public const string IRONCLAD = "ironclad";                  // augment: hacks can't lower your Stability
        public const string HACK_SHIELD = "hack_shield";            // augment: blocks the next N hacks on you
        public const string HACK_BACKLASH = "hack_backlash";        // augment: whoever hacks you gains N Glitch
        public const string PUNISH_COMPROMISED = "punish_compromised";    // augment: +N if the opponent is Compromised
        public const string COMPROMISE_PROOF = "compromise_proof";  // augment: its Power is never halved
        public const string LATE_BLOOMER = "late_bloomer";          // augment: +N per turn you took before installing it
        public const string EXTRA_TORSO_MOD = "extra_torso_mod";    // augment: the torso holds N more mods
        public const string STEAL_MOD = "steal_mod";                // augment: takes the opponent's best mod
        public const string CLEAN_BONUS = "clean_bonus";            // mod: +N Power if its augment has no Glitch
        public const string SIPHON = "siphon";                      // mod: the opponent gains N Glitch
        public const string LATE_ONLY = "late_only";                // mod: only from your Nth turn
        public const string GLITCH_MODDED = "glitch_modded";        // hack: +N Glitch, one more if the target has a mod
        public const string STABILITY_DRAIN = "stability_drain";    // hack: the opponent loses N Stability
        public const string STRIP_MODS = "strip_mods";              // hack: remove the target's mods
        public const string SACRIFICE_GLITCH = "sacrifice_glitch";  // hack: scrap your weakest mod for +N Glitch on the target
        public const string MOVE_HACK_GLITCH = "move_hack_glitch";  // hack: +N, and your hack Glitch moves onto the target
        public const string SUFFOCATE = "suffocate";                // hack: the target gains no Power from mods
        public const string VOID = "void";                          // hack: a target with no mods counts 0 Power
        public const string DOUBLE_BASE = "double_base";            // mod: its augment's printed Power counts twice
        public const string HACK_AMP = "hack_amp";                  // augment: your Glitch hacks add N more

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
                    if (card.effect == LATE_ONLY && me.turnsTaken + 1 < card.amount) return new List<Slot>();
                    return Enumerable.Range(0, SLOTS).Select(i => (Slot)i).Where(s =>
                    {
                        Installed host = me[s];
                        return host != null && host.mods.Count < MaxMods(me, s) && (card.modFor == null || card.modFor == "any" || card.modFor == SlotKind(s));
                    }).ToList();
                case HACK:
                    if (opponent.HackImmune) return new List<Slot>();
                    if (card.effect == SACRIFICE_GLITCH && !me.body.Any(i => i != null && i.mods.Count > 0)) return new List<Slot>();
                    return Enumerable.Range(0, SLOTS).Select(i => (Slot)i).Where(s =>
                    {
                        Installed victim = opponent[s];
                        if (victim == null) return false;
                        if (card.effect == STRIP_MODS) return victim.mods.Count > 0;
                        if (card.effect == VOID) return victim.mods.Count == 0 && !victim.voided;
                        return true;
                    }).ToList();
                default:
                    return new List<Slot>();
            }
        }

        public static int MaxMods(AugPlayer me, Slot slot) =>
            MAX_MODS + (slot == Slot.Torso ? me.EffectTotal(EXTRA_TORSO_MOD) : 0);

        /// <summary>
        /// Plays a card onto the board: installs an augment, attaches a mod or runs a hack, with
        /// their effects. The match, the AI's lookahead and the simulator all go through this.
        /// Drawing cards is left to the match (it owns the draw piles). log may be null.
        /// </summary>
        public static void Resolve(AugPlayer me, AugPlayer opponent, CardDef card, Slot target, List<AugEvent> log)
        {
            switch (card.kind)
            {
                case AUGMENT:
                {
                    Installed old = me[target];
                    Installed installed = new Installed { augment = card, turnInstalled = me.turnsTaken };
                    me.body[(int)target] = installed;
                    log?.Add(old == null ? new AugEvent("aug_log_install", me.name, card) : new AugEvent("aug_log_replace", me.name, card, old.augment));
                    if (card.effect == HACK_SHIELD) me.hackShields += Math.Max(1, card.amount);
                    if (card.effect == STEAL_MOD) StealMod(me, opponent, installed, log);
                    break;
                }
                case MOD:
                    me[target].mods.Add(card);
                    log?.Add(new AugEvent("aug_log_mod", me.name, card, me[target].augment));
                    if (card.effect == SIPHON) opponent.looseGlitch += card.amount;
                    break;
                case HACK:
                {
                    Installed victim = opponent[target];
                    int backlash = opponent.EffectTotal(HACK_BACKLASH);
                    me.looseGlitch += backlash;
                    if (opponent.hackShields > 0)
                    {
                        opponent.hackShields--;
                        log?.Add(new AugEvent("aug_log_blocked", me.name, card));
                        break;
                    }
                    int amp = me.EffectTotal(HACK_AMP);   // Plague Walker
                    switch (card.effect)
                    {
                        case SCRAP:
                            opponent.body[(int)target] = null;
                            log?.Add(new AugEvent("aug_log_scrap", me.name, card, victim.augment));
                            return;
                        case GLITCH:
                            victim.hackGlitch += card.amount + amp;
                            log?.Add(new AugEvent("aug_log_hack", me.name, card, victim.augment, card.amount + amp));
                            return;
                        case GLITCH_MODDED:
                        {
                            int amount = card.amount + (victim.mods.Count > 0 ? 1 : 0) + amp;
                            victim.hackGlitch += amount;
                            log?.Add(new AugEvent("aug_log_hack", me.name, card, victim.augment, amount));
                            return;
                        }
                        case SACRIFICE_GLITCH:
                        {
                            Installed host = me.body.Where(i => i != null && i.mods.Count > 0)
                                .OrderBy(i => i.mods.Min(m => m.power - m.glitch)).FirstOrDefault();
                            if (host != null) host.mods.Remove(host.mods.OrderBy(m => m.power - m.glitch).First());
                            victim.hackGlitch += card.amount + amp;
                            log?.Add(new AugEvent("aug_log_hack", me.name, card, victim.augment, card.amount + amp));
                            return;
                        }
                        case MOVE_HACK_GLITCH:
                        {
                            int moved = 0;
                            foreach (Installed mine in me.body.Where(i => i != null)) { moved += mine.hackGlitch; mine.hackGlitch = 0; }
                            victim.hackGlitch += card.amount + moved + amp;
                            log?.Add(new AugEvent("aug_log_hack", me.name, card, victim.augment, card.amount + moved + amp));
                            return;
                        }
                        case STABILITY_DRAIN:
                            opponent.stabilityLoss += card.amount;
                            break;
                        case STRIP_MODS:
                            victim.mods.Clear();
                            break;
                        case SUFFOCATE:
                            victim.suffocated = true;
                            break;
                        case VOID:
                            victim.voided = true;
                            break;
                    }
                    log?.Add(new AugEvent("aug_log_hack_on", me.name, card, victim.augment));
                    break;
                }
            }
        }

        // Kinetic Harpoon: the opponent's best mod (most Power for its Glitch) moves onto the new augment.
        private static void StealMod(AugPlayer me, AugPlayer opponent, Installed onto, List<AugEvent> log)
        {
            Installed from = null;
            CardDef best = null;
            foreach (Installed i in opponent.body.Where(i => i != null))
                foreach (CardDef m in i.mods)
                    if (best == null || m.power - m.glitch > best.power - best.glitch) { best = m; from = i; }
            if (best == null) return;
            from.mods.Remove(best);
            onto.mods.Add(best);
            log?.Add(new AugEvent("aug_log_steal", me.name, onto.augment, best));
        }

        /// <summary>A player's showdown score. The opponent is needed for effects that look at them.</summary>
        public static AugScore Score(AugPlayer p, bool called, AugPlayer opponent = null)
        {
            AugScore s = new AugScore { stability = BASE_STABILITY - (p.HasEffect(IRONCLAD) ? 0 : p.stabilityLoss) };
            s.glitch = p.looseGlitch;
            int softPenalty = -1, protectedPower = 0;
            for (int i = 0; i < SLOTS; i++)
            {
                Installed slot = p.body[i];
                if (slot == null) continue;
                CardDef aug = slot.augment;
                s.glitch += slot.Glitch;
                s.stability += aug.stability + slot.mods.Sum(m => m.stability);
                if (slot.voided) continue;

                int part = aug.power + slot.mods.Sum(m => slot.suffocated ? Math.Min(0, m.power) : m.power);
                if (slot.Glitch == 0) part += slot.mods.Where(m => m.effect == CLEAN_BONUS).Sum(m => m.amount);
                if (!slot.suffocated) part += aug.power * slot.mods.Count(m => m.effect == DOUBLE_BASE);
                switch (aug.effect)
                {
                    case POWER_PER_MOD: part += aug.amount * slot.mods.Count; break;
                    case POWER_PER_OTHER_MOD: part += aug.amount * p.body.Where(o => o != null && o != slot).Sum(o => o.mods.Count); break;
                    case POWER_IF_CALLED: if (called) part += aug.amount; break;
                    case POWER_PER_STABLE: part += aug.amount * p.body.Count(o => o != null && o != slot && o.Glitch == 0); break;
                    case LATE_BLOOMER: part += aug.amount * slot.turnInstalled; break;
                    case PUNISH_COMPROMISED: if (opponent != null && Score(opponent, false).compromised) part += aug.amount; break;
                    case SOFT_COMPROMISE: softPenalty = aug.amount; break;
                }
                if (aug.effect == COMPROMISE_PROOF) protectedPower += Math.Max(0, part);
                s.power += part;
            }
            s.stability = Math.Max(0, s.stability);
            s.compromised = s.glitch > s.stability;
            int exposed = s.power - protectedPower;
            s.final = !s.compromised ? s.power
                : (softPenalty >= 0 ? Math.Max(0, exposed - softPenalty) : exposed / 2) + protectedPower;
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
