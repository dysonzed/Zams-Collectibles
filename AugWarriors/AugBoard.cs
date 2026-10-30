using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZamsCollectibles.AugWarriors
{
    /// <summary>
    /// The Aug Warriors board: a match as a machine-style window built from the game's own
    /// widgets, played by dragging cards like any other item.
    ///
    /// Panels: the opponent's Aug, your Aug, your hand. Each Aug is one shaped grid (like the
    /// game's module bays) with five card-sized 2 x 3 pockets and a one-cell gap between them:
    ///
    ///         [head]
    ///   [arm] [torso] [arm]
    ///         [legs]
    ///
    /// with a line of text per part and bars for Power and Glitch against Stability below.
    /// The gaps mean a card fits exactly one pocket, so where it lands says which part it is.
    ///
    /// Playing: drag an augment into an empty pocket, or onto an installed one to replace it;
    /// drag a mod onto one of your augments; drag a hack onto one of the opponent's. Illegal
    /// drops bounce back to the hand (the rules engine decides). The opponent's moves play out
    /// with a short delay.
    ///
    /// The cards on the board are temporary items: not owned (so they can't go into the
    /// player's bags), tagged, and destroyed when the match ends. The rules engine holds the
    /// real state; the board is redrawn from it after every move.
    /// </summary>
    public static class AugBoard
    {
        public const string MATCH_TAG = "ZAMS_AUG_MATCH_CARD";
        private const string USE_ON_UNOWNED_TAG = "CAN_USE_ON_NON_OWNED_TAG";   // the game's tag: may be dropped onto items the player doesn't own
        private const int HUMAN = 0, NPC = 1;
        private const int HAND_COLUMNS = 12, HAND_ROWS = 6;   // two rows of six cards: room for the rules' maximum hand
        private const float NPC_DELAY = 0.7f;    // seconds between the opponent's moves
        private const float POWER_BAR_MAX = 20f;

        // The Aug board: 8 x 11 cells, '0' a cell, '1' none. Pockets in Slot order: head, torso, arm, arm, legs.
        private const int BOARD_WIDTH = 8;
        private static readonly string BoardShape = string.Concat(
            "11100111", "11100111", "11100111",
            "11111111",
            "00100100", "00100100", "00100100",
            "11111111",
            "11100111", "11100111", "11100111");
        private static readonly (int x, int y)[] Pockets = { (3, 0), (3, 4), (0, 4), (6, 4), (3, 8) };

        private static readonly Color Good = new Color32(0x45, 0x91, 0x8B, 0xFF);
        private static readonly Color Risky = new Color32(0xCC, 0x77, 0x55, 0xFF);
        private static readonly Color Bad = new Color32(0xC0, 0x40, 0x4A, 0xFF);
        private static readonly Color PowerColor = new Color32(0x67, 0xAA, 0xA5, 0xFF);

        // match
        private static AugMatch match;
        private static AugPersonality npcStyle;
        private static string npcName;
        private static int ante;
        private static bool settled;
        private static Action<int?> onFinished;
        private static readonly Queue<AugAction> pending = new Queue<AugAction>();
        private static float nextNpcMove;

        // coin toss: the player calls it; the winner picks who goes first
        private enum Toss { Call, Flipping, Lost, Choose, Done }
        private static Toss toss = Toss.Done;
        private static System.Random tossRng;
        private static bool calledHeads, landedHeads;
        private static bool npcGoesFirst;
        private const int NPC_FIRST_CHANCE = 25;   // percent: an NPC that wins the toss usually goes second (it wins more often)
        private static float tossStart;
        private const float FLIP_TIME = 1.6f, LOST_PAUSE = 1.8f;
        private static SpritePixelElement coin;
        private static MachineryText coinSpace;   // a gap under the coin while it shows
        private static MachineryButton choiceA, choiceB;
        private static bool bounced;     // the last drop wasn't legal and went back to the hand
        private static bool resync;      // redraw on the next frame (never inside the game's drop callbacks)

        // window
        private static PixelWindow window;
        private static readonly GameGridInventory[] boards = new GameGridInventory[2];
        private static readonly MachineryText[,] partText = new MachineryText[2, AugRules.SLOTS];
        private static readonly MachineryText[] statText = new MachineryText[2];
        private static readonly MachineryProgressBar[] powerBar = new MachineryProgressBar[2];
        private static readonly SpritePixelElement[] stabilityBar = new SpritePixelElement[2];
        private static readonly MachineryText[] stabilityText = new MachineryText[2];
        private static readonly MachineryText[] glitchText = new MachineryText[2];   // the Glitch total, under the Stability bar
        private static readonly MachineryText[] logText = new MachineryText[3];
        private static GameGridInventory hand;
        private static MachineryText statusText;
        private static MachineryButton callButton, endButton;
        private static bool syncing;     // the board is being redrawn: grids accept anything, nothing is a move
        private static GameObject runner;

        public static bool IsMatchCard(GameItem item) => item != null && item.IsTag(MATCH_TAG);

        // ------------------------------------------------------------ match

        /// <summary>Starts a match on the board. A null deck gets a random one (debug); ante 0 plays for nothing.</summary>
        public static void Start(List<CardDef> deck, string opponentName, AugPersonality style, int stake, Action<int?> finished = null)
        {
            System.Random rng = new System.Random(RNG.random.Next());
            List<CardDef> pool = CardDatabase.Sets.SelectMany(s => s.cards).ToList();
            AugPlayer you = new AugPlayer(Loc.Get("aug_you"), deck ?? AugAI.BuildDeck(pool, AugPersonality.Cautious, rng));
            AugPlayer them = new AugPlayer(opponentName, AugAI.BuildDeck(pool, style, rng));
            match = new AugMatch(you, them, rng.Next());
            toss = Toss.Call;            // who starts is settled by the coin toss before play
            tossRng = rng;
            npcName = opponentName;
            npcStyle = style;
            ante = stake;
            settled = false;
            bounced = false;
            onFinished = finished;
            pending.Clear();
            nextNpcMove = Time.time + NPC_DELAY;

            if (window == null || window.handler == null) Build();
            window.SetTitle(Loc.Get("aug_title_vs", npcName));
            if (runner == null)
            {
                runner = new GameObject("ZamsAugBoard");
                runner.AddComponent<Runner>();
            }
            Sync();
            window.Show(new Vector2(Screen.width * 0.5f, Screen.height * 0.85f), Vector2.zero);
        }

        // Called every frame: plays queued moves and the opponent's turns, one at a time.
        private static void Update()
        {
            if (match == null) return;
            if (toss != Toss.Done) { UpdateToss(); return; }
            if (resync) { resync = false; Sync(); return; }
            if (pending.Count > 0)
            {
                AugAction action = pending.Dequeue();
                bool legal = !match.over && match.current == HUMAN && match.IsLegal(action);
                if (legal)
                {
                    match.Apply(action);
                    nextNpcMove = Time.time + NPC_DELAY;
                }
                bounced = !legal && action.kind == ActionKind.Play;
                Sync();
                return;
            }
            if (!match.over && match.current == NPC && Time.time >= nextNpcMove)
            {
                match.Apply(AugAI.Choose(match, npcStyle));
                nextNpcMove = Time.time + NPC_DELAY;
                Sync();
            }
        }

        private static void Settle()
        {
            if (settled) return;
            settled = true;
            if (ante <= 0) return;
            if (match.winner == HUMAN) ModHelper.AddCredit(ante);
            else if (match.winner == NPC) ModHelper.AddCredit(-ante);
        }

        // The window closes: report the result, or concede if the match isn't over.
        private static void Finish()
        {
            if (match == null) return;
            int? winner = match.winner;
            if (!match.over)
            {
                if (!settled && ante > 0) ModHelper.AddCredit(-ante);
                settled = true;
                winner = NPC;
            }
            match = null;
            pending.Clear();
            Sweep();
            Action<int?> finished = onFinished;
            onFinished = null;
            finished?.Invoke(winner);
        }

        /// <summary>Closes the board if a match is open (mid-match that concedes).</summary>
        public static void CloseIfOpen()
        {
            if (match == null) return;
            window?.Hide();
            Finish();
        }

        // Destroys every temporary match card, wherever it ended up.
        private static void Sweep()
        {
            syncing = true;
            try
            {
                foreach (GameGridInventory grid in AllGrids())
                    foreach (GameItem item in grid.childItems.ToList()) item.Destroy();
                foreach (GameItem stray in EmporiumEntry.Instance?.GetAllItems().Where(IsMatchCard).ToList() ?? new List<GameItem>())
                    stray.Destroy();
            }
            finally { syncing = false; }
        }

        // ------------------------------------------------------------ pockets

        // Which pocket an item on a board sits in, from its position.
        private static bool PocketOf(GameItem item, out Slot slot)
        {
            slot = Slot.Head;
            if (item?.modifiedShape == null) return false;
            int i = Array.IndexOf(Pockets, (item.modifiedShape.minX, item.modifiedShape.minY));
            if (i < 0) return false;
            slot = (Slot)i;
            return true;
        }

        private static GameItem InPocket(int who, Slot slot) =>
            boards[who].childItems.FirstOrDefault(i => PocketOf(i, out Slot s) && s == slot);

        private static int BoardOf(GameInventory inventory) => Array.IndexOf(boards, inventory);

        // ------------------------------------------------------------ playing by dragging

        // A drop onto your board from the hand. Where it lands decides the part; illegal drops bounce back.
        private static bool MayDropOnBoard(GameItem item, int who)
        {
            if (syncing) return true;
            if (who != HUMAN || !IsMatchCard(item) || item.parentInventory != hand) return false;
            CardDef card = CardItems.GetCardDef(item);
            return card != null && card.kind == AugRules.AUGMENT && match != null && toss == Toss.Done && !match.over && match.current == HUMAN;
        }

        private static void OnDropOnBoard(GameItem item, int who)
        {
            if (syncing || who != HUMAN) return;
            CardDef card = CardItems.GetCardDef(item);
            if (card == null) return;
            if (PocketOf(item, out Slot slot)) Queue(AugAction.Play(card, slot));
            else { bounced = true; resync = true; }   // not in a pocket: redrawn (back to the hand) next frame
        }

        // A card dropped onto an installed card: replace it, attach a mod, or hack it.
        private static AugAction? TargetAction(GameItem source, GameItem target)
        {
            if (match == null || toss != Toss.Done || match.over || match.current != HUMAN) return null;
            if (!IsMatchCard(source) || source.parentInventory != hand || !IsMatchCard(target)) return null;
            int who = BoardOf(target.parentInventory);
            if (who < 0 || !PocketOf(target, out Slot slot)) return null;
            CardDef card = CardItems.GetCardDef(source);
            if (card == null) return null;
            bool mine = who == HUMAN;
            bool fits = card.kind == AugRules.HACK ? !mine : mine;
            AugAction action = AugAction.Play(card, slot);
            return fits && match.IsLegal(action) ? action : (AugAction?)null;
        }

        private static void Queue(AugAction action) => pending.Enqueue(action);

        // ------------------------------------------------------------ window

        private static void Build()
        {
            window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.Panel(Loc.Get("aug_board_them"), AugPanel(NPC), "them"),
                MachineHelper.Panel(Loc.Get("aug_board_you"), AugPanel(HUMAN), "you"),
                MachineHelper.Panel(Loc.Get("aug_hand"), HandPanel(), "hand"));
            window.closeCallback += Finish;
        }

        // One player's Aug: the shaped board, a line per part, the totals and the bars.
        private static PixelElement AugPanel(int who)
        {
            GameGridInventory board = new GameGridInventory(BoardShape, BOARD_WIDTH).SetMachineryStyle();
            int owner = who;
            board.mayInventoryAddItemFunc = (item, _) => MayDropOnBoard(item, owner);
            board.mayInventoryRemoveItemFunc = (_, _) => syncing;
            board.onSlotAddItemEndFunc += (item, _, _) => OnDropOnBoard(item, owner);
            boards[who] = board;
            // the skeleton shows in the empty pockets: parts are bolted onto a body
            foreach (var cell in AugArt.Skeleton(BOARD_WIDTH, BoardShape.Length / BOARD_WIDTH))
                if (BoardShape[cell.Key.y * BOARD_WIDTH + cell.Key.x] == '0') board.SetCellBackdrop(cell.Key.x, cell.Key.y, cell.Value);

            List<PixelElement> rows = new List<PixelElement> { board };
            for (int s = 0; s < AugRules.SLOTS; s++)
            {
                partText[who, s] = new MachineryText(null, 5, TMPro.HorizontalAlignmentOptions.Left, false);
                rows.Add(partText[who, s]);
            }
            statText[who] = new MachineryText();
            powerBar[who] = new MachineryProgressBar(0f).SetColor(PowerColor);
            stabilityText[who] = new MachineryText(null, 6, TMPro.HorizontalAlignmentOptions.Center, true);
            stabilityBar[who] = new SpritePixelElement("Items/items_misc", "postit");
            // the element takes its size from the sprite it has when laid out, so give it the bar's
            // fixed-width canvas now (later bars are the same size and stay centred)
            stabilityBar[who].SetSprite(AugArt.StabilityBar(who, AugRules.BASE_STABILITY, 0));
            stabilityBar[who].Validate();
            rows.Add(statText[who]);
            rows.Add(powerBar[who]);
            rows.Add(stabilityText[who]);
            rows.Add(stabilityBar[who]);
            glitchText[who] = new MachineryText(null, 6, TMPro.HorizontalAlignmentOptions.Center, true);
            rows.Add(glitchText[who]);
            return MachineryStack.Vertical(rows.ToArray());
        }

        private static PixelElement HandPanel()
        {
            hand = new GameGridInventory(HAND_COLUMNS, HAND_ROWS).SetMachineryStyle();
            hand.mayInventoryAddItemFunc = (item, _) => syncing || item.parentInventory == hand;   // rearranging only
            statusText = new MachineryText(null, 6, TMPro.HorizontalAlignmentOptions.Left, false);
            for (int i = 0; i < logText.Length; i++) logText[i] = new MachineryText(null, 5, TMPro.HorizontalAlignmentOptions.Left, false);
            callButton = new MachineryButton(Loc.Get("aug_call"), () => Queue(AugAction.Call)).SetDescription(Loc.Get("aug_call_desc"));
            endButton = new MachineryButton(Loc.Get("aug_concede"), () => { window.Hide(); Finish(); });
            coin = new SpritePixelElement("Items/items_misc", "postit");
            coin.SetSprite(AugArt.Coin(true, 1f));   // sized from its sprite at layout: the coin's size
            coin.Validate();
            choiceA = new MachineryButton(Loc.Get("aug_toss_heads"), () => ChoiceA());
            choiceB = new MachineryButton(Loc.Get("aug_toss_tails"), () => ChoiceB());
            coinSpace = new MachineryText(" ");
            return MachineryStack.Vertical(hand, coin, coinSpace, statusText, MachineryStack.Horizontal(choiceA, choiceB), logText[0], logText[1], logText[2],
                MachineryStack.Horizontal(callButton, endButton));
        }

        // Redraws the board from the rules engine's state.
        private static void Sync()
        {
            if (match == null || window == null) return;
            syncing = true;
            try
            {
                for (int who = 0; who < 2; who++)
                {
                    AugPlayer p = match.players[who];
                    // anything that isn't sitting exactly in a pocket goes (a dropped card is redrawn properly)
                    foreach (GameItem stray in boards[who].childItems.Where(i => !PocketOf(i, out _)).ToList()) stray.Destroy();
                    for (int s = 0; s < AugRules.SLOTS; s++)
                    {
                        Installed it = p.body[s];
                        GameItem shown = InPocket(who, (Slot)s);
                        if (CardItems.GetCardDef(shown) != it?.augment || (shown == null) != (it == null))
                        {
                            shown?.Destroy();
                            if (it != null) PlaceInPocket(who, (Slot)s, it.augment);
                        }
                        partText[who, s].SetText(PartText((Slot)s, it));
                    }
                    Stats(who);
                }

                foreach (GameItem old in hand.childItems.ToList()) old.Destroy();
                foreach (CardDef card in match.players[HUMAN].hand) PlaceInHand(card);

                var recent = match.log.Skip(Math.Max(0, match.log.Count - logText.Length)).ToList();
                for (int i = 0; i < logText.Length; i++)
                    logText[i].SetText(i < recent.Count ? AugMatchWindow.LogLine(recent[i]) : "");

                ShowToss();
                if (toss != Toss.Done)
                {
                    callButton.Disable();
                    endButton.SetText(Loc.Get("aug_concede"));
                }
                else if (match.over)
                {
                    Settle();
                    statusText.SetText(Verdict());
                    callButton.Disable();
                    endButton.SetText(Loc.Get("aug_close"));
                }
                else
                {
                    bool mine = match.current == HUMAN;
                    bool stuck = mine && !match.HasPlay();
                    statusText.SetText(!mine ? Loc.Get("aug_board_their_turn", npcName)
                        : stuck ? Loc.Get(match.finalTurn ? "aug_board_stuck_final" : "aug_board_stuck")
                        : bounced ? Loc.Get("aug_board_bounced")
                        : match.finalTurn ? Loc.Get("aug_board_final_turn") : Loc.Get("aug_board_your_turn"));
                    // nothing playable on the last turn: it ends on its own
                    if (stuck && match.finalTurn) pending.Enqueue(AugAction.Hold);
                    if (mine && !match.finalTurn) callButton.Enable(); else callButton.Disable();
                    endButton.SetText(Loc.Get("aug_concede"));
                }
            }
            finally { syncing = false; }
        }

        // ------------------------------------------------------------ coin toss

        // The two choice buttons: heads / tails to call it, then go first / go second.
        private static void ChoiceA()
        {
            if (toss == Toss.Call) Flip(heads: true);
            else if (toss == Toss.Choose) StartPlay(HUMAN);
        }

        private static void ChoiceB()
        {
            if (toss == Toss.Call) Flip(heads: false);
            else if (toss == Toss.Choose) StartPlay(NPC);
        }

        private static void Flip(bool heads)
        {
            calledHeads = heads;
            landedHeads = tossRng.Next(2) == 0;
            npcGoesFirst = tossRng.Next(100) < NPC_FIRST_CHANCE;   // only used if the NPC wins the toss
            toss = Toss.Flipping;
            tossStart = Time.time;
            resync = true;
        }

        private static void StartPlay(int first)
        {
            match.current = first;
            toss = Toss.Done;
            nextNpcMove = Time.time + NPC_DELAY;
            match.log.Add(new AugEvent("aug_log_first", match.players[first].name));
            resync = true;
        }

        // Runs every frame during the toss: the spinning coin, then the loser's pause.
        private static void UpdateToss()
        {
            float t = Time.time - tossStart;
            if (resync) { resync = false; Sync(); }
            if (toss == Toss.Flipping)
            {
                if (t < FLIP_TIME)
                {
                    // spins fast, then slows: the angle eases out over the flip
                    float eased = 1f - (1f - t / FLIP_TIME) * (1f - t / FLIP_TIME);
                    float angle = eased * Mathf.PI * 9f;
                    bool face = Mathf.Cos(angle) >= 0f ? landedHeads : !landedHeads;
                    coin.SetSprite(AugArt.Coin(face, Mathf.Abs(Mathf.Cos(angle))));
                    return;
                }
                bool won = calledHeads == landedHeads;
                toss = won ? Toss.Choose : Toss.Lost;
                tossStart = Time.time;
                Sync();
            }
            else if (toss == Toss.Lost && t >= LOST_PAUSE)
            {
                StartPlay(npcGoesFirst ? NPC : HUMAN);
            }
        }

        private static void ShowToss()
        {
            bool tossing = toss != Toss.Done;
            if (tossing) { coin.Show(); coin.SetSprite(AugArt.Coin(toss == Toss.Call || landedHeads, 1f)); }
            else coin.Hide();   // gone once play starts
            coinSpace.SetText(tossing ? " " : "");
            string face = Loc.Get(landedHeads ? "aug_toss_heads" : "aug_toss_tails");
            switch (toss)
            {
                case Toss.Call:
                    statusText.SetText(Loc.Get("aug_toss_call"));
                    choiceA.SetText(Loc.Get("aug_toss_heads")); choiceB.SetText(Loc.Get("aug_toss_tails"));
                    choiceA.Show(); choiceB.Show();
                    break;
                case Toss.Flipping:
                    statusText.SetText(Loc.Get("aug_toss_flipping"));
                    choiceA.Hide(); choiceB.Hide();
                    break;
                case Toss.Choose:
                    statusText.SetText(Loc.Get("aug_toss_won", face));
                    choiceA.SetText(Loc.Get("aug_toss_first")); choiceB.SetText(Loc.Get("aug_toss_second"));
                    choiceA.Show(); choiceB.Show();
                    break;
                case Toss.Lost:
                    statusText.SetText(Loc.Get(npcGoesFirst ? "aug_toss_lost_first" : "aug_toss_lost_second", face, npcName));
                    choiceA.Hide(); choiceB.Hide();
                    break;
                default:
                    choiceA.Hide(); choiceB.Hide();
                    break;
            }
        }

        // A temporary, unowned card item showing the card's mini face.
        private static GameItem MakeCard(CardDef card)
        {
            GameItem item = DirectoryMaster.Item(CardItems.CardId(card), isOwned: false);
            item.EnableTag(MATCH_TAG);
            item.SetSpriteAndShapeFromMod(ModEntry.MOD_ID, MiniCards.Ensure(card));
            item.SetValue(0);
            return item;
        }

        private static void PlaceInPocket(int who, Slot slot, CardDef card)
        {
            GameItem item = MakeCard(card);
            (int x, int y) = Pockets[(int)slot];
            item.modifiedShape = new GridShapeBuilder(item.modifiedShape).SetPosition(x, y).Build();
            if (!boards[who].UncheckedAccept(item)) item.Destroy();
        }

        // Hand cards are owned, because the game only lets owned items be dropped onto other
        // items (mods, hacks, replacing). They may only ever sit in the hand or on a board, so
        // they can't be carried off; the sweep at the end of the match removes them anyway.
        private static void PlaceInHand(CardDef card)
        {
            GameItem item = MakeCard(card);
            GeneralHelper.SetItemOwned(item, true);
            item.EnableTag(USE_ON_UNOWNED_TAG);
            item.mayInventoryAddItemFunc = (_, inv) => syncing || inv == hand || BoardOf(inv) >= 0;
            item.mayThisTargetItemFunc = (s, t) => TargetAction(s, t).HasValue;
            item.canThisTargetItemFunc = (s, t) => TargetAction(s, t).HasValue;
            item.onThisTargetItemFunc = (s, t) => { AugAction? a = TargetAction(s, t); if (a.HasValue) Queue(a.Value); };
            if (GraphUtils.TryAcceptAll(hand, item) > 0) item.Destroy();
        }

        private static string PartText(Slot slot, Installed it)
        {
            string part = Loc.SlotName(AugRules.SlotKind(slot));
            if (it == null) return Loc.Get("aug_board_part_empty", part);
            string text = Loc.Get("aug_board_part", part, Loc.CardTitle(it.augment), it.augment.power + it.mods.Sum(m => m.power), it.Glitch);
            if (it.hackGlitch > 0) text += Loc.Get("aug_slot_hacked", it.hackGlitch);
            if (it.mods.Count > 0) text += " + " + string.Join(", ", it.mods.Select(Loc.CardTitle));
            return text;
        }

        private static void Stats(int who)
        {
            AugScore s = AugRules.Score(match.players[who], match.caller == who, match.players[1 - who]);
            statText[who].SetStatus(match.players[who].name, Loc.Get("aug_board_power", s.power));
            powerBar[who].SetValue(Mathf.Clamp01(s.power / POWER_BAR_MAX));
            stabilityText[who].SetText(s.compromised ? Loc.Get("aug_board_compromised", s.glitch, s.stability) : Loc.Get("aug_board_stability", s.glitch, s.stability));
            stabilityText[who].SetColor(s.compromised ? Bad : s.glitch >= s.stability ? Risky : Good);
            stabilityBar[who].SetSprite(AugArt.StabilityBar(who, s.stability, s.glitch));
            glitchText[who].SetText(Loc.Get("aug_board_glitch", s.glitch));
            glitchText[who].SetColor(s.glitch == 0 ? Good : s.compromised ? Bad : Risky);
        }

        private static string Verdict()
        {
            AugScore you = match.scores[HUMAN], them = match.scores[NPC];
            string verdict = Loc.Get(match.winner == HUMAN ? "aug_result_win" : match.winner == NPC ? "aug_result_lose" : "aug_result_draw", npcName);
            string scores = Loc.Get("aug_result_scores", Score(you), npcName, Score(them));
            string stakes = ante > 0 && match.winner.HasValue
                ? " " + Loc.Get(match.winner == HUMAN ? "aug_result_won_ante" : "aug_result_lost_ante", ante + GeneralHelper.CreditIcon())
                : "";
            return $"{verdict} {scores}{stakes}";
        }

        private static string Score(AugScore s) => s.compromised ? Loc.Get("aug_score_compromised", s.final, s.power) : s.final.ToString();

        private static IEnumerable<GameGridInventory> AllGrids()
        {
            foreach (GameGridInventory g in boards) if (g != null) yield return g;
            if (hand != null) yield return hand;
        }

        /// <summary>Drives the board: queued moves and the opponent's paced turns.</summary>
        private class Runner : MonoBehaviour
        {
            private void Update()
            {
                try { AugBoard.Update(); }
                catch (Exception e)
                {
                    ModEntry.Instance.Log.Error($"Aug Warriors board: {e}");
                    pending.Clear();
                }
            }
        }
    }
}
