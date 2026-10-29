using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZamsCollectibles.AugWarriors
{
    /// <summary>
    /// An Aug Warriors match against an NPC, in a custom window rebuilt after every move.
    /// Stakes: the winner takes the credit ante; closing the window mid-match concedes. The
    /// caller (the Den's table) is told the result when the player closes the window.
    /// </summary>
    public static class AugMatchWindow
    {
        private const string MATCH_WINDOW = ModEntry.MOD_ID + ".aug_match";
        private const int HUMAN = 0, NPC = 1;

        private static readonly Color Warning = new Color32(0xE0, 0x50, 0x5A, 0xFF);
        private static readonly Color Good = new Color32(0x67, 0xAA, 0xA5, 0xFF);

        private static AugMatch match;
        private static AugPersonality npcStyle;
        private static string npcName;
        private static int ante;
        private static bool settled;
        private static bool rebuilding;     // replacing the window closes the old one: not a forfeit
        private static CardDef selected;
        private static Action<int?> onFinished;   // told the winner (0 player, 1 opponent, null draw)

        // ------------------------------------------------------------ match

        /// <summary>Starts a match. A null deck gets a random one (debug test matches); ante 0 plays for nothing.</summary>
        public static void Start(List<CardDef> deck, string opponentName, AugPersonality style, int stake, Action<int?> finished = null)
        {
            onFinished = finished;
            System.Random rng = new System.Random(RNG.random.Next());
            List<CardDef> pool = CardDatabase.Sets.SelectMany(s => s.cards).ToList();
            AugPlayer you = new AugPlayer(Loc.Get("aug_you"), deck ?? AugAI.BuildDeck(pool, AugPersonality.Cautious, rng));
            AugPlayer them = new AugPlayer(opponentName, AugAI.BuildDeck(pool, style, rng));
            match = new AugMatch(you, them, rng.Next());
            npcName = opponentName;
            npcStyle = style;
            ante = stake;
            settled = false;
            selected = null;
            if (rng.Next(2) == 1) match.current = NPC;   // coin flip for who starts
            RunNpc();
            Rebuild();
        }

        private static void Act(AugAction action)
        {
            if (match == null || match.over || match.current != HUMAN || !match.IsLegal(action)) return;
            selected = null;
            match.Apply(action);
            RunNpc();
            Rebuild();
        }

        private static void RunNpc()
        {
            int guard = 0;
            while (!match.over && match.current == NPC && guard++ < 10) match.Apply(AugAI.Choose(match, npcStyle));
            if (match.over) Settle();
        }

        private static void Settle()
        {
            if (settled || ante <= 0) { settled = true; return; }
            settled = true;
            if (match.winner == HUMAN) ModHelper.AddCredit(ante);
            else if (match.winner == NPC) ModHelper.AddCredit(-ante);
        }

        // Closing the window: after the showdown it reports the result; mid-match it concedes.
        private static void OnClosed()
        {
            if (rebuilding || match == null) return;
            int? winner = match.winner;
            if (!match.over)
            {
                if (!settled && ante > 0) ModHelper.AddCredit(-ante);
                settled = true;
                winner = NPC;
            }
            match = null;
            Action<int?> finished = onFinished;
            onFinished = null;
            finished?.Invoke(winner);
        }

        /// <summary>Closes the window if a match is open; mid-match that concedes it.</summary>
        public static void CloseIfOpen()
        {
            if (match != null) CustomUIManager.Instance?.CloseWindow(MATCH_WINDOW);
        }

        private static void Rebuild()
        {
            if (CustomUIManager.Instance == null || match == null) return;
            rebuilding = true;
            try { Build(); }
            finally { rebuilding = false; }
        }

        private static void Build()
        {
            AugPlayer you = match.players[HUMAN], them = match.players[NPC];
            List<Slot> targets = selected != null && match.current == HUMAN && !match.over ? match.Targets(selected) : new List<Slot>();

            CustomUIBuilder b = CustomUIManager.Window(MATCH_WINDOW, Loc.Get("aug_title"), CustomUIManager.LAYER_META)
                .SetSize(1000f, 760f).SetDraggable(true).OnClose(OnClosed)
                .BeginColumn(4f);

            b.AddLabel(Loc.Get("aug_header", npcName, Mathf.Min(match.Round, AugRules.MAX_TURNS), AugRules.MAX_TURNS,
                ante > 0 ? ante + GeneralHelper.CreditIcon() : Loc.Get("aug_no_stakes")));

            BodyRow(b, them, NPC, selected != null && selected.kind == AugRules.HACK ? targets : null);
            BodyRow(b, you, HUMAN, selected != null && selected.kind != AugRules.HACK ? targets : null);

            if (match.over) Result(b);
            else Hand(b, you);

            b.AddHeader(Loc.Get("aug_log"));
            foreach (AugEvent e in match.log.Skip(Math.Max(0, match.log.Count - 4)))
                b.AddLabel(Loc.Get(e.key, Localize(e.args)));

            b.End().Center().Show();
        }

        // One player's five slots and totals. hackTargets/installTargets: slots that take the selected card.
        private static void BodyRow(CustomUIBuilder b, AugPlayer p, int who, List<Slot> targets)
        {
            AugScore s = AugRules.Score(p, match.caller == who);
            b.AddLabel(Loc.Get("aug_totals", p.name, s.power, s.glitch, s.stability, p.hand.Count, p.deck.Count));
            if (s.glitch > s.stability) b.Last.SetTextColor(Warning);

            b.BeginRow(6f, TextAnchor.UpperLeft);
            for (int i = 0; i < AugRules.SLOTS; i++)
            {
                Slot slot = (Slot)i;
                Installed it = p[slot];
                b.BeginPanel(184f, 118f, TextAnchor.UpperLeft, 6f, 2f);
                b.AddLabel(Loc.SlotName(AugRules.SlotKind(slot)));
                if (it == null)
                {
                    b.AddLabel(Loc.Get("aug_empty"));
                }
                else
                {
                    b.AddLabel(Loc.CardTitle(it.augment));
                    int bonus = it.hackGlitch;
                    b.AddLabel(Loc.Get("aug_slot_stats", it.augment.power + it.mods.Sum(m => m.power), it.Glitch) + (bonus > 0 ? Loc.Get("aug_slot_hacked", bonus) : ""));
                    if (bonus > 0) b.Last.SetTextColor(Warning);
                    if (it.mods.Count > 0) b.AddLabel(string.Join(", ", it.mods.Select(Loc.CardTitle)));
                }
                if (targets != null && targets.Contains(slot))
                {
                    CardDef card = selected;
                    string verb = card.kind == AugRules.HACK ? "aug_do_hack" : card.kind == AugRules.MOD ? "aug_do_mod" : it == null ? "aug_do_install" : "aug_do_replace";
                    b.AddButton(Loc.Get(verb), () => Act(AugAction.Play(card, slot)));
                }
                b.End();
            }
            b.End();
        }

        private static void Hand(CustomUIBuilder b, AugPlayer you)
        {
            b.AddHeader(match.finalTurn ? Loc.Get("aug_hand_final") : Loc.Get("aug_hand"));
            b.BeginRow(6f, TextAnchor.UpperLeft);
            foreach (CardDef card in you.hand)
            {
                CardDef c = card;
                bool playable = match.Targets(c).Count > 0;
                b.BeginPanel(236f, 168f, TextAnchor.UpperLeft, 6f, 2f);
                b.AddLabel(Loc.CardTitle(c));
                b.AddLabel(CardItems.CardText(c));
                b.AddButton(c == selected ? Loc.Get("aug_selected") : Loc.Get("aug_select"), () => { selected = c == selected ? null : c; Rebuild(); });
                if (!playable) b.Last.SetInteractable(false);
                b.End();
            }
            b.End();

            b.BeginRow(6f, TextAnchor.MiddleLeft);
            b.AddLabel(selected == null ? Loc.Get("aug_pick_card") : Loc.Get("aug_pick_target"));
            b.AddFlexibleSpace();
            b.AddButton(Loc.Get("aug_hold"), () => Act(AugAction.Hold));
            b.AddButton(Loc.Get("aug_call"), () => Act(AugAction.Call));
            if (match.finalTurn) b.Last.SetInteractable(false);
            b.AddButton(Loc.Get("aug_concede"), () => CustomUIManager.Instance.CloseWindow(MATCH_WINDOW));
            b.End();
        }

        private static void Result(CustomUIBuilder b)
        {
            AugScore you = match.scores[HUMAN], them = match.scores[NPC];
            string verdict = match.winner == HUMAN ? "aug_result_win" : match.winner == NPC ? "aug_result_lose" : "aug_result_draw";
            b.AddHeader(Loc.Get(verdict, npcName));
            b.Last.SetTextColor(match.winner == HUMAN ? Good : match.winner == NPC ? Warning : Color.white);
            b.AddLabel(Loc.Get("aug_result_scores", ScoreText(you), npcName, ScoreText(them)));
            if (ante > 0 && match.winner.HasValue)
                b.AddLabel(Loc.Get(match.winner == HUMAN ? "aug_result_won_ante" : "aug_result_lost_ante", ante + GeneralHelper.CreditIcon()));
            b.AddButton(Loc.Get("aug_close"), () => CustomUIManager.Instance.CloseWindow(MATCH_WINDOW));
        }

        private static string ScoreText(AugScore s) =>
            s.compromised ? Loc.Get("aug_score_compromised", s.final, s.power) : s.final.ToString();

        // Log and message arguments: cards in the player's language, everything else as is.
        public static object[] Localize(object[] args) =>
            args.Select(a => a is CardDef c ? (object)Loc.CardTitle(c) : a).ToArray();
    }
}
