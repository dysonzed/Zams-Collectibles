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
        public bool silenced;       // its augment's effect is gone
        public bool zeroBase;       // its printed Power counts 0 (mods still count)
        public int bonusPower;      // Power it has built up in play (Golden Rule, Echo Chamber)

        /// <summary>The augment's effect, unless it's been silenced.</summary>
        public string Effect => silenced ? null : augment.effect;

        public int Glitch => Math.Max(0, augment.glitch + mods.Sum(m => m.glitch) + hackGlitch);

        public Installed Clone()
        {
            Installed copy = new Installed
            {
                augment = augment, hackGlitch = hackGlitch, turnInstalled = turnInstalled,
                suffocated = suffocated, voided = voided, silenced = silenced, zeroBase = zeroBase, bonusPower = bonusPower,
            };
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
        public int droneSwarm;      // Glitch per empty slot at the showdown
        public int scorePenalty;    // taken off the final score
        public int modLock;         // turns left in which no mods can be played
        public int extraActions;    // more plays this turn
        public int bonusDraws;      // cards drawn beyond the turn's draw
        public int hacksTaken;      // hacks played on this player

        public AugPlayer(string name, IEnumerable<CardDef> deck)
        {
            this.name = name;
            this.deck = deck.ToList();
        }

        public Installed this[Slot slot] => body[(int)slot];

        public bool HackImmune => HasEffect(AugRules.HACK_IMMUNE);

        public bool HasEffect(string effect) => body.Any(i => i != null && i.Effect == effect);

        public int EffectTotal(string effect) => body.Where(i => i != null && i.Effect == effect).Sum(i => i.augment.amount);

        /// <summary>Mods in play; God-Tier Processor counts as two.</summary>
        public int ModCount => body.Where(i => i != null).Sum(i => AugRules.ModWeight(i.mods));

        /// <summary>A copy of the board state (body, counters), with the given draw pile.</summary>
        public AugPlayer CloneBody(IEnumerable<CardDef> withDeck = null)
        {
            AugPlayer copy = new AugPlayer(name, withDeck ?? Enumerable.Empty<CardDef>())
            {
                turnsTaken = turnsTaken, looseGlitch = looseGlitch, stabilityLoss = stabilityLoss, hackShields = hackShields,
                droneSwarm = droneSwarm, scorePenalty = scorePenalty, modLock = modLock,
                extraActions = extraActions, bonusDraws = bonusDraws, hacksTaken = hacksTaken,
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
                    if (me.extraActions > 0 && !finalTurn)
                    {
                        me.extraActions--;   // another play this turn, without a draw
                        return;
                    }
                    break;
            }
            EndTurn();
        }

        private void Play(AugPlayer me, AugPlayer opponent, CardDef card, Slot target)
        {
            bool wasCompromised = AugRules.Score(opponent, false, me).compromised;
            AugRules.Resolve(me, opponent, card, target, log);
            switch (card.effect)
            {
                case AugRules.DRAW:
                    for (int i = 0; i < card.amount; i++) Draw(me, true);
                    break;
                case AugRules.KARMIC:
                    if (wasCompromised || AugRules.Score(opponent, false, me).compromised)
                        for (int i = 0; i < card.amount; i++) Draw(me, true);
                    break;
                case AugRules.DRAW_BOTH:
                    for (int i = 0; i < card.amount; i++) Draw(me, true);
                    for (int i = 0; i < card.amount - 1; i++) Draw(opponent, true);
                    break;
                case AugRules.REBOOT:
                    opponent.deck.AddRange(opponent.hand);
                    opponent.hand.Clear();
                    for (int i = 0; i < card.amount; i++) Draw(opponent);
                    break;
                case AugRules.EXTRA_ACTIONS:
                    me.extraActions += card.amount;
                    break;
                case AugRules.EXTRA_ACTIONS_COSTLY:
                    me.extraActions += card.amount;
                    me.turnsTaken++;   // a turn lost later
                    break;
            }
        }

        // ------------------------------------------------------------ turns

        private void StartTurn()
        {
            if (Current.hand.Count < AugRules.HAND_LIMIT) Draw(Current);
            if (Current.HasEffect(AugRules.TIME_DILATION) && Current.turnsTaken >= 4) Draw(Current, true);   // turns 5 and 6
        }

        private void EndTurn()
        {
            Current.turnsTaken++;
            Current.extraActions = 0;
            if (Current.modLock > 0) Current.modLock--;
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
            for (int i = 0; i < 2; i++) scores[i] = AugRules.Score(players[i], caller == i, players[1 - i], caller.HasValue);
            int a = scores[0].final, b = scores[1].final;
            if (a != b) winner = a > b ? 0 : 1;
            else if (caller.HasValue) winner = 1 - caller.Value;   // ties go to whoever didn't call
            else winner = null;
            log.Add(new AugEvent("aug_log_showdown"));
        }

        // bonus: a draw from a card's effect, not the turn's draw (Over-Ray Visor counts those).
        // A draw from an empty deck overloads the system instead: it adds Glitch, so drawing a
        // thin deck dry has a cost. A draw stopped by the hand limit costs nothing.
        private void Draw(AugPlayer p, bool bonus = false)
        {
            if (p.hand.Count >= AugRules.MAX_HAND) return;
            if (p.deck.Count == 0)
            {
                p.looseGlitch += AugRules.EMPTY_DRAW_GLITCH;
                log.Add(new AugEvent("aug_log_empty_draw", p.name, AugRules.EMPTY_DRAW_GLITCH));
                return;
            }
            p.hand.Add(p.deck[0]);
            p.deck.RemoveAt(0);
            if (bonus) p.bonusDraws++;
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
        public const int EMPTY_DRAW_GLITCH = 1; // Glitch for each draw that finds the deck empty
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
        public const string AEGIS = "aegis";                        // augment: never Compromised; lose the overflow instead
        public const string EXTRA_MODS_SELF = "extra_mods_self";    // augment: holds N more mods itself
        public const string SWARM = "swarm";                        // augment: +N per other card of its set you have in play
        public const string FULL_TERM = "full_term";                // augment: +N if nobody called the showdown
        public const string DISCARD = "discard";                    // augment: the opponent discards a card
        public const string RELAY = "relay";                        // augment: your mods' Glitch counts against the opponent
        public const string POWER_PER_ANY_MOD = "power_per_any_mod";      // augment: +N per mod you have in play
        public const string LAST_WORD = "last_word";                // augment: +N if installed on your 6th turn or later
        public const string PURIFY = "purify";                      // mod: clears its augment's hack Glitch
        public const string WARD = "ward";                          // mod: its augment can't be hacked
        public const string TORSO_BONUS = "torso_bonus";            // mod: +N Power on a torso
        public const string VOID_ARM = "void_arm";                  // hack: an arm counts 0 Power
        public const string BROOD_DRAIN = "brood_drain";            // hack: drain 1 Stability, plus 1 per card of its set you have in play
        public const string STRIP_BEST = "strip_best";              // hack: remove the opponent's N best mods
        public const string DRONE_SWARM = "drone_swarm";            // hack: +N Glitch per empty slot of theirs at the showdown
        public const string GLITCH_PER_MOD = "glitch_per_mod";      // hack: +1 Glitch, plus N per mod they have
        public const string SILENCE = "silence";                    // hack: the target loses its effect
        public const string SWAP_HEADS = "swap_heads";              // hack: swap heads with the opponent
        public const string MOD_LOCK = "mod_lock";                  // hack: no mods for their next N turns
        public const string LOGIC_BOMB = "logic_bomb";              // hack: +N Glitch if they have none, else +1
        public const string WIPE_MODS = "wipe_mods";                // hack: every mod in play is destroyed
        public const string SCORE_PENALTY = "score_penalty";        // hack: -N off their final score
        public const string ZERO_BASE = "zero_base";                // hack: the target's printed Power counts 0
        public const string MOD_GUARD = "mod_guard";                // augment: your mods can't be destroyed, moved or stolen
        public const string HACK_REWARD = "hack_reward";            // augment: +N Power on it each time you play a hack
        public const string DRAW_POWER = "draw_power";              // augment: +N per card you've drawn from effects
        public const string HACKED_POWER = "hacked_power";          // augment: +N per hack played on you
        public const string ECHO = "echo";                          // augment: +N Power on it each time you attach a mod
        public const string EXTRA_ACTIONS = "extra_actions";        // augment: N more plays this turn
        public const string EXTRA_ACTIONS_COSTLY = "extra_actions_costly";  // augment: N more plays now, a turn lost later
        public const string TIME_DILATION = "time_dilation";        // augment: an extra card on your turns 5 and 6
        public const string UNIFIED = "unified";                    // augment: your torso's mods apply to every augment
        public const string STABILITY_DOUBLE = "stability_double";  // augment: your Stability counts double
        public const string SINGULARITY = "singularity";            // augment: + the opponent's strongest printed Power
        public const string OUROBOROS = "ouroboros";                // augment: your strongest augment counts twice
        public const string DOUBLE_MOD = "double_mod";              // mod: counts as two mods
        public const string ACCRETION = "accretion";                // mod: +N per card in your hand
        public const string KARMIC = "karmic";                      // hack: +1 Glitch; draw N if they're Compromised
        public const string DRAW_BOTH = "draw_both";                // hack: you draw N, the opponent N - 1
        public const string REBOOT = "reboot";                      // hack: they shuffle their hand away and draw N
        public const string SPAGHETTI = "spaghetti";                // hack: all their mods onto the target, which counts 0 Power
        public const string MOD_SWAP = "mod_swap";                  // hack: your worst mod for their best

        public static int ModWeight(IEnumerable<CardDef> mods) => mods.Sum(m => m.effect == DOUBLE_MOD ? 2 : 1);

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
                    if (card.effect == LATE_ONLY && me.turnsTaken + 1 < card.amount) return new List<Slot>();
                    return SlotsFor(card.slot).ToList();
                case MOD:
                    if (me.modLock > 0) return new List<Slot>();
                    if (card.effect == LATE_ONLY && me.turnsTaken + 1 < card.amount) return new List<Slot>();
                    return Enumerable.Range(0, SLOTS).Select(i => (Slot)i).Where(s =>
                    {
                        Installed host = me[s];
                        return host != null && host.mods.Count < MaxMods(me, s) && (card.modFor == null || card.modFor == "any" || card.modFor == SlotKind(s));
                    }).ToList();
                case HACK:
                    if (opponent.HackImmune) return new List<Slot>();
                    if (card.effect == SACRIFICE_GLITCH && !me.body.Any(i => i != null && i.mods.Count > 0)) return new List<Slot>();
                    if (card.effect == MOD_SWAP && (!me.body.Any(i => i != null && i.mods.Count > 0) || opponent.ModCount == 0 || opponent.HasEffect(MOD_GUARD)))
                        return new List<Slot>();
                    return Enumerable.Range(0, SLOTS).Select(i => (Slot)i).Where(s =>
                    {
                        Installed victim = opponent[s];
                        if (victim == null || victim.mods.Any(m => m.effect == WARD)) return false;
                        switch (card.effect)
                        {
                            case STRIP_MODS: return victim.mods.Count > 0 && !opponent.HasEffect(MOD_GUARD);
                            case VOID: return victim.mods.Count == 0 && !victim.voided;
                            case VOID_ARM: return SlotKind(s) == "arm" && !victim.voided;
                            case SWAP_HEADS: return s == Slot.Head;
                            case ZERO_BASE: return !victim.zeroBase;
                            case SILENCE: return victim.Effect != null;
                            default: return true;
                        }
                    }).ToList();
                default:
                    return new List<Slot>();
            }
        }

        public static int MaxMods(AugPlayer me, Slot slot) =>
            MAX_MODS + (slot == Slot.Torso ? me.EffectTotal(EXTRA_TORSO_MOD) : 0)
            + (me[slot]?.Effect == EXTRA_MODS_SELF ? me[slot].augment.amount : 0);

        // Cards of a set a player has in play: augments and mods.
        private static int InPlayFrom(AugPlayer p, CardSetDef set) =>
            p.body.Where(i => i != null).Sum(i => (i.augment.set == set ? 1 : 0) + i.mods.Count(m => m.set == set));

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
                    if (card.effect == DISCARD && opponent.hand.Count > 0)
                        opponent.hand.RemoveAt((opponent.hand.Count * 7 + me.turnsTaken * 3) % opponent.hand.Count);
                    break;
                }
                case MOD:
                    me[target].mods.Add(card);
                    log?.Add(new AugEvent("aug_log_mod", me.name, card, me[target].augment));
                    if (card.effect == SIPHON) opponent.looseGlitch += card.amount;
                    foreach (Installed echo in me.body.Where(i => i != null && i.Effect == ECHO)) echo.bonusPower += echo.augment.amount;
                    if (card.effect == PURIFY) me[target].hackGlitch = 0;
                    break;
                case HACK:
                {
                    Installed victim = opponent[target];
                    opponent.hacksTaken++;
                    foreach (Installed rule in me.body.Where(i => i != null && i.Effect == HACK_REWARD)) rule.bonusPower += rule.augment.amount;
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
                        case GLITCH_PER_MOD:
                        case LOGIC_BOMB:
                        {
                            int amount = card.effect == GLITCH_PER_MOD ? 1 + card.amount * opponent.ModCount
                                : Score(opponent, false, me).glitch == 0 ? card.amount : 1;
                            victim.hackGlitch += amount + amp;
                            log?.Add(new AugEvent("aug_log_hack", me.name, card, victim.augment, amount + amp));
                            return;
                        }
                        case STABILITY_DRAIN:
                            opponent.stabilityLoss += card.amount;
                            break;
                        case BROOD_DRAIN:
                            opponent.stabilityLoss += 1 + InPlayFrom(me, card.set);
                            break;
                        case STRIP_BEST:
                            for (int n = 0; n < card.amount && !opponent.HasEffect(MOD_GUARD); n++)
                            {
                                Installed from = opponent.body.Where(i => i != null && i.mods.Count > 0)
                                    .OrderByDescending(i => i.mods.Max(m => m.power - m.glitch)).FirstOrDefault();
                                if (from == null) break;
                                from.mods.Remove(from.mods.OrderByDescending(m => m.power - m.glitch).First());
                            }
                            break;
                        case DRONE_SWARM:
                            opponent.droneSwarm += card.amount;
                            break;
                        case SILENCE:
                            victim.silenced = true;
                            break;
                        case SWAP_HEADS:
                        {
                            Installed mine = me.body[(int)Slot.Head];
                            me.body[(int)Slot.Head] = victim;
                            opponent.body[(int)Slot.Head] = mine;
                            break;
                        }
                        case MOD_LOCK:
                            opponent.modLock = Math.Max(opponent.modLock, Math.Max(1, card.amount));
                            break;
                        case WIPE_MODS:
                            foreach (AugPlayer side in new[] { me, opponent })
                                if (!side.HasEffect(MOD_GUARD))
                                    foreach (Installed i in side.body.Where(i => i != null)) i.mods.Clear();
                            break;
                        case KARMIC:
                            victim.hackGlitch += 1 + amp;
                            break;
                        case DISCARD:
                            if (opponent.hand.Count > 0)
                                opponent.hand.RemoveAt((opponent.hand.Count * 7 + me.turnsTaken * 3) % opponent.hand.Count);
                            break;
                        case SPAGHETTI:
                            if (!opponent.HasEffect(MOD_GUARD))
                                foreach (Installed other in opponent.body.Where(i => i != null && i != victim))
                                {
                                    victim.mods.AddRange(other.mods);
                                    other.mods.Clear();
                                }
                            victim.voided = true;
                            break;
                        case MOD_SWAP:
                        {
                            Installed myHost = me.body.Where(i => i != null && i.mods.Count > 0)
                                .OrderByDescending(i => i.mods.Max(m => m.glitch - m.power)).First();
                            CardDef mine = myHost.mods.OrderByDescending(m => m.glitch - m.power).First();
                            Installed theirHost = opponent.body.Where(i => i != null && i.mods.Count > 0)
                                .OrderByDescending(i => i.mods.Max(m => m.power - m.glitch)).First();
                            CardDef theirs = theirHost.mods.OrderByDescending(m => m.power - m.glitch).First();
                            myHost.mods.Remove(mine); theirHost.mods.Remove(theirs);
                            myHost.mods.Add(theirs); theirHost.mods.Add(mine);
                            break;
                        }
                        case SCORE_PENALTY:
                            opponent.scorePenalty += card.amount;
                            break;
                        case ZERO_BASE:
                            victim.zeroBase = true;
                            break;
                        case STRIP_MODS:
                            victim.mods.Clear();
                            break;
                        case SUFFOCATE:
                            victim.suffocated = true;
                            break;
                        case VOID:
                        case VOID_ARM:
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
            if (opponent.HasEffect(MOD_GUARD)) return;
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

        // An augment's Glitch for its owner: with Bypass Relay, its mods' Glitch goes to the opponent.
        private static int SlotGlitch(AugPlayer owner, Installed slot) =>
            Math.Max(0, slot.augment.glitch + slot.hackGlitch
                + slot.mods.Sum(m => owner.HasEffect(RELAY) ? Math.Min(0, m.glitch) : m.glitch));

        /// <summary>
        /// A player's showdown score. The opponent is needed for effects that look at them;
        /// anyCalled says whether someone called the showdown (the match knows; elsewhere assume not).
        /// </summary>
        public static AugScore Score(AugPlayer p, bool called, AugPlayer opponent = null, bool anyCalled = false)
        {
            AugScore s = new AugScore { stability = BASE_STABILITY - (p.HasEffect(IRONCLAD) ? 0 : p.stabilityLoss) };
            s.glitch = p.looseGlitch + p.droneSwarm * p.body.Count(i => i == null);
            if (opponent != null && opponent.HasEffect(RELAY))
                s.glitch += opponent.body.Where(i => i != null).Sum(i => i.mods.Sum(m => Math.Max(0, m.glitch)));
            int softPenalty = -1, protectedPower = 0, strongest = 0;
            // Unified Field: the torso's mods count on every other augment too
            Installed torso = p[Slot.Torso];
            bool unified = torso != null && p.HasEffect(UNIFIED);
            int sharedPower = unified && !torso.suffocated ? torso.mods.Sum(m => m.power) : 0;
            int sharedGlitch = unified ? torso.mods.Sum(m => p.HasEffect(RELAY) ? Math.Min(0, m.glitch) : m.glitch) : 0;
            for (int i = 0; i < SLOTS; i++)
            {
                Installed slot = p.body[i];
                if (slot == null) continue;
                CardDef aug = slot.augment;
                int glitch = SlotGlitch(p, slot);
                if (unified && i != (int)Slot.Torso) glitch = Math.Max(0, glitch + sharedGlitch);
                s.glitch += glitch;
                s.stability += aug.stability + slot.mods.Sum(m => m.stability);
                if (slot.voided) continue;

                int printed = slot.zeroBase ? 0 : aug.power;
                int part = printed + slot.mods.Sum(m => slot.suffocated ? Math.Min(0, m.power) : m.power) + slot.bonusPower;
                if (unified && i != (int)Slot.Torso) part += sharedPower;
                if (!slot.suffocated) part += slot.mods.Where(m => m.effect == ACCRETION).Sum(m => m.amount * p.hand.Count);
                if (glitch == 0) part += slot.mods.Where(m => m.effect == CLEAN_BONUS).Sum(m => m.amount);
                if (!slot.suffocated)
                {
                    part += printed * slot.mods.Count(m => m.effect == DOUBLE_BASE);
                    if (i == (int)Slot.Torso) part += slot.mods.Where(m => m.effect == TORSO_BONUS).Sum(m => m.amount);
                }
                switch (slot.Effect)
                {
                    case POWER_PER_MOD: part += aug.amount * ModWeight(slot.mods); break;
                    case POWER_PER_OTHER_MOD: part += aug.amount * p.body.Where(o => o != null && o != slot).Sum(o => ModWeight(o.mods)); break;
                    case DRAW_POWER: part += aug.amount * p.bonusDraws; break;
                    case HACKED_POWER: part += aug.amount * p.hacksTaken; break;
                    case SINGULARITY:
                        if (opponent != null)
                            part += opponent.body.Where(o => o != null && !o.voided && !o.zeroBase).Select(o => o.augment.power).DefaultIfEmpty(0).Max();
                        break;
                    case POWER_PER_ANY_MOD: part += aug.amount * p.ModCount; break;
                    case POWER_IF_CALLED: if (called) part += aug.amount; break;
                    case POWER_PER_STABLE: part += aug.amount * p.body.Count(o => o != null && o != slot && SlotGlitch(p, o) == 0); break;
                    case LATE_BLOOMER: part += aug.amount * slot.turnInstalled; break;
                    case LAST_WORD: if (slot.turnInstalled >= 5) part += aug.amount; break;
                    case FULL_TERM: if (!anyCalled) part += aug.amount; break;
                    case SWARM: part += aug.amount * (InPlayFrom(p, aug.set) - 1); break;
                    case PUNISH_COMPROMISED: if (opponent != null && Score(opponent, false).compromised) part += aug.amount; break;
                    case SOFT_COMPROMISE: softPenalty = aug.amount; break;
                }
                if (slot.Effect == COMPROMISE_PROOF) protectedPower += Math.Max(0, part);
                strongest = Math.Max(strongest, part);
                s.power += part;
            }
            if (p.HasEffect(OUROBOROS)) s.power += strongest;
            s.glitch = Math.Max(0, s.glitch);
            s.stability = Math.Max(0, s.stability);
            if (p.HasEffect(STABILITY_DOUBLE)) s.stability *= 2;
            bool over = s.glitch > s.stability;
            if (over && p.HasEffect(AEGIS))
            {
                // Aegis Halo: never Compromised; the Glitch over the limit comes off the Power instead
                s.compromised = false;
                s.final = Math.Max(0, s.power - (s.glitch - s.stability));
            }
            else
            {
                s.compromised = over;
                int exposed = s.power - protectedPower;
                s.final = !over ? s.power
                    : (softPenalty >= 0 ? Math.Max(0, exposed - softPenalty) : exposed / 2) + protectedPower;
            }
            s.final = Math.Max(0, s.final - p.scorePenalty);
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
