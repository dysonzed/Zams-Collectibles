using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZamsCollectibles.AugWarriors
{
    /// <summary>
    /// A deck box: a small container for one Aug Warriors deck (up to 20 cards). It nests in
    /// backpacks and other containers, so it can be carried to the Downside Den, where it goes
    /// in the Aug Warriors table's deck slot. Its window shows whether the deck is legal and
    /// has a Validate button for a full report.
    /// </summary>
    public static class DeckBox
    {
        public const string DECKBOX_ID = ModEntry.MOD_ID + ".deckbox";
        private const string REPORT_WINDOW = ModEntry.MOD_ID + ".deck_report";
        private const int COLUMNS = 5, ROWS = 4;   // 20 cards, the largest legal deck
        private const long DECKBOX_VALUE = 5;

        private static readonly Color Warning = new Color32(0xE0, 0x50, 0x5A, 0xFF);
        private static readonly Color Advice = new Color32(0xCC, 0x77, 0x55, 0xFF);

        public static void Register(ModItemDirectory dir, Dictionary<string, Action<GameItem>> textByItemId)
        {
            dir.Add(DECKBOX_ID, CreateDeckBox);
            textByItemId[DECKBOX_ID] = SetText;
        }

        private static GameItem CreateDeckBox()
        {
            GameGridInventory grid = new GameGridInventory(COLUMNS, ROWS);
            MachineryText summary = MachineryText.Status(Loc.Get("deckbox_status"), "");
            MachineryButton validate = new MachineryButton(Loc.Get("deckbox_validate"), () => OpenReport(Cards(grid)))
                .SetDescription(Loc.Get("deckbox_validate_desc"));
            MachineryButton copyCode = new MachineryButton(Loc.Get("deckcode_copy"), () => summary.SetStatus(Loc.Get("deckbox_status"), DeckCode.CopyToClipboard(Cards(grid))))
                .SetDescription(Loc.Get("deckcode_copy_desc"));

            PixelWindow window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.Panel(Loc.Get("deckbox_panel_cards"), grid, "cards"),
                MachineHelper.Panel(Loc.Get("deckbox_panel_deck"), MachineryStack.Vertical(summary, validate, copyCode), "deck"));
            window.openCallback += () => Refresh(grid, summary);

            GameItem item = ItemDirectory.CreateEmptyItem(DECKBOX_ID)
                .SetContentWindow(window)
                .SetSpriteAndShapeFromMod(ModEntry.MOD_ID, "deck_box");
            SetText(item);
            item.SetGameItemType(Collectibles.TYPE);
            item.SetValue(DECKBOX_VALUE);
            ContainerHelper.InitContainerItem(grid, item, allowedTags: new List<string> { CardItems.CARD_TAG });
            CardItems.MakeNestable(item);
            CardItems.TrackContainerValue(item, DECKBOX_VALUE, grid);
            grid.onSlotAddItemEndFunc += (_, _, _) => Refresh(grid, summary);
            grid.onSlotRemoveItemEndFunc += (_, _, _) => Refresh(grid, summary);
            return item;
        }

        private static void SetText(GameItem item)
        {
            item.name = Loc.Get("deckbox_name");
            item.shortDescription = Loc.Get("deckbox_desc", AugRules.MIN_DECK, AugRules.MAX_DECK, AugRules.MAX_COPIES);
        }

        public static bool IsDeckBox(GameItem item) => item != null && item.identifier == DECKBOX_ID;

        /// <summary>
        /// Locks a deck box while its deck is being played: its window closes and won't open, and
        /// its cards can't be taken out or added (in case a window was left open some other way).
        /// </summary>
        public static void SetInPlay(GameItem box, bool inPlay)
        {
            GameGridInventory grid = box == null ? null : GraphUtils.FindChildType<GameGridInventory>(box);
            if (grid == null) return;
            if (inPlay)
            {
                box.contentWindow?.Hide();
                box.contentWindow?.Lock();
                grid.LockInv();
            }
            else
            {
                box.contentWindow?.Unlock();
                grid.UnlockInv();
            }
        }

        /// <summary>The cards in a deck box (or its grid).</summary>
        public static List<CardDef> Cards(GameItem box) => Cards(box == null ? null : GraphUtils.FindChildType<GameGridInventory>(box));

        private static List<CardDef> Cards(GameGridInventory grid) =>
            grid == null ? new List<CardDef>() : grid.childItems.Select(CardItems.GetCardDef).Where(c => c != null).ToList();

        private static void Refresh(GameGridInventory grid, MachineryText summary)
        {
            List<CardDef> deck = Cards(grid);
            int errors = AugRules.DeckErrors(deck).Count;
            summary.SetStatus(Loc.Get("deckbox_status"), errors == 0
                ? Loc.Get("deckbox_status_ok", deck.Count)
                : Loc.Get("deckbox_status_errors", deck.Count, errors));
        }

        /// <summary>The deck report: legality, every error, advice, and what the deck is made of.</summary>
        public static void OpenReport(List<CardDef> deck)
        {
            if (CustomUIManager.Instance == null) return;
            List<AugEvent> errors = AugRules.DeckErrors(deck);
            List<AugEvent> warnings = AugRules.DeckWarnings(deck);

            CustomUIBuilder b = CustomUIManager.Window(REPORT_WINDOW, Loc.Get("deckbox_report_title"), CustomUIManager.LAYER_META)
                .SetSize(460f, 520f).SetDraggable(true).SetCloseOnEscape(true)
                .BeginColumn(4f);
            b.AddHeader(errors.Count == 0 ? Loc.Get("deckbox_report_legal", deck.Count) : Loc.Get("deckbox_report_illegal", deck.Count));
            b.Last.SetTextColor(errors.Count == 0 ? (Color)new Color32(0x67, 0xAA, 0xA5, 0xFF) : Warning);
            foreach (AugEvent e in errors) { b.AddLabel("• " + Loc.Get(e.key, AugMatchWindow.Localize(e.args))); b.Last.SetTextColor(Warning); }
            foreach (AugEvent e in warnings) { b.AddLabel("• " + Loc.Get(e.key, AugMatchWindow.Localize(e.args))); b.Last.SetTextColor(Advice); }

            b.AddHeader(Loc.Get("deckbox_report_overview"));
            foreach (string slot in new[] { "head", "torso", "arm", "legs" })
                b.AddLabel(Loc.Get("deckbox_report_slot", Loc.SlotName(slot), deck.Count(c => c.kind == AugRules.AUGMENT && c.slot == slot)));
            b.AddLabel(Loc.Get("deckbox_report_mods", deck.Count(c => c.kind == AugRules.MOD)));
            b.AddLabel(Loc.Get("deckbox_report_hacks", deck.Count(c => c.kind == AugRules.HACK)));
            List<CardDef> augments = deck.Where(c => c.kind == AugRules.AUGMENT).ToList();
            if (augments.Count > 0)
                b.AddLabel(Loc.Get("deckbox_report_average", augments.Average(c => c.power).ToString("0.0"), augments.Average(c => c.glitch).ToString("0.0")));

            if (deck.Count > 0)
            {
                // the code to select and copy by hand, in case the clipboard doesn't work
                b.AddHeader(Loc.Get("deckcode_header"));
                b.AddInput("", DeckCode.Encode(deck));
                b.Last.SetPreferredSize(420f, -1f);
            }
            b.AddButton(Loc.Get("aug_close"), () => CustomUIManager.Instance.CloseWindow(REPORT_WINDOW));
            b.End().Center().Show();
        }
    }
}
