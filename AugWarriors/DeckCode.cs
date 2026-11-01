using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZamsCollectibles.AugWarriors
{
    /// <summary>
    /// Deck codes: a deck as a short line of text to send to friends, who can play against it at
    /// the Den table. The code lists each set's card numbers and ends with a check, e.g.
    /// AW1:RWR=1,1,4,7;DDY=3,12/K7 (set codes and card numbers as printed on the cards), so it stays
    /// readable and survives new sets being added. Case and spaces don't matter.
    ///
    /// The check is two characters worked out from the rest of the code. Until it's there the code
    /// is unfinished; when it doesn't match, a character is wrong or missing. So a code cut short
    /// or mistyped can't pass for a different deck, and the check looks like part of the code, so
    /// nobody leaves it off as stray punctuation. Anything after it is ignored.
    /// </summary>
    public static class DeckCode
    {
        private const string PREFIX = "AW1:";
        private const char CHECK_MARK = '/';
        private const string CHECK_ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";   // no look-alikes (0/O, 1/I/L)
        private const int CHECK_LENGTH = 2;
        private const string IMPORT_WINDOW = ModEntry.MOD_ID + ".deck_code";
        private const float WIDTH = 500f, CONTENT_WIDTH = 460f, LINE = 27f;   // the import window, and a line of its text

        private static readonly Color Good = new Color32(0x67, 0xAA, 0xA5, 0xFF);
        private static readonly Color Bad = new Color32(0xE0, 0x50, 0x5A, 0xFF);
        private static readonly Color Waiting = new Color32(0xB0, 0xB4, 0xC0, 0xFF);   // the hint in an empty field: not an error

        public static string Encode(IEnumerable<CardDef> deck)
        {
            string body = PREFIX + string.Join(";", deck
                .GroupBy(c => c.set)
                .OrderBy(g => CardDatabase.Sets.IndexOf(g.Key))
                .Select(g => g.Key.code + "=" + string.Join(",", g.Select(c => c.number).OrderBy(n => n))));
            return body + CHECK_MARK + Check(body);
        }

        // Two characters from an FNV-1a hash of the code (upper case, no spaces), stable on every machine.
        private static string Check(string body)
        {
            uint hash = 2166136261;
            foreach (char c in body.ToUpperInvariant()) { hash ^= c; hash *= 16777619; }
            int n = CHECK_ALPHABET.Length;
            return new string(new[] { CHECK_ALPHABET[(int)(hash % n)], CHECK_ALPHABET[(int)(hash / n % n)] });
        }

        /// <summary>
        /// The deck in a code, or null with the reason (a localization key) when it can't be read.
        /// unfinished: the code has no check yet, so it isn't wrong, just not done.
        /// </summary>
        public static List<CardDef> Decode(string code, out string error, out bool unfinished)
        {
            error = "deckcode_unreadable";
            unfinished = false;
            string text = new string((code ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
            int mark = text.IndexOf(CHECK_MARK);
            if (mark < 0 || text.Length < mark + 1 + CHECK_LENGTH)
            {
                // still being typed, or cut short; but text that can't become a code is wrong already
                unfinished = PREFIX.StartsWith(text) || text.StartsWith(PREFIX);
                if (unfinished) error = null;
                return null;
            }
            string check = text.Substring(mark + 1, CHECK_LENGTH);
            text = text.Substring(0, mark);
            if (!text.StartsWith(PREFIX)) return null;
            if (check != Check(text)) { error = "deckcode_check_failed"; return null; }

            List<CardDef> deck = new List<CardDef>();
            foreach (string group in text.Substring(PREFIX.Length).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = group.Split('=');
                if (parts.Length != 2) return null;
                CardSetDef set = CardDatabase.Sets.FirstOrDefault(s => s.code.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                if (set == null) { error = "deckcode_unknown_set"; return null; }
                foreach (string number in parts[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    CardDef card = int.TryParse(number, out int n) ? set.cards.FirstOrDefault(c => c.number == n) : null;
                    if (card == null) return null;
                    deck.Add(card);
                }
            }
            if (deck.Count == 0) return null;
            error = null;
            return deck;
        }

        /// <summary>
        /// How the AI plays a friend's deck: the way the deck was built to be played. Hacks count for
        /// more the more of the deck they are, mods likewise, and a deck of high-Glitch augments is
        /// pushed closer to its limit. Calls the showdown halfway between the Shark and the cautious.
        /// No lookahead: Oracle's search also reads your hand, which a friend couldn't.
        /// </summary>
        public static AugPersonality FriendStyle(List<CardDef> deck)
        {
            float hacks = deck.Count(c => c.kind == AugRules.HACK) / (float)Math.Max(1, deck.Count);
            float mods = deck.Count(c => c.kind == AugRules.MOD) / (float)Math.Max(1, deck.Count);
            List<CardDef> augments = deck.Where(c => c.kind == AugRules.AUGMENT).ToList();
            float glitch = augments.Count == 0 ? 0f : (float)augments.Average(c => c.glitch);
            return new AugPersonality
            {
                id = "friend",
                hackWeight = 0.7f + 2.5f * hacks,           // about 0.95 at a tenth hacks, 1.6 at a third
                modBonus = Mathf.Clamp(mods * 1.5f, 0f, 0.6f),
                glitchBuffer = glitch >= 1.5f ? -1 : glitch >= 0.75f ? 0 : 1,
                callLead = 3,
                callGain = 3f,
            };
        }

        // ------------------------------------------------------------ export

        /// <summary>Copies a deck's code to the clipboard; the status line to show.</summary>
        public static string CopyToClipboard(List<CardDef> deck)
        {
            if (deck.Count == 0) return Loc.Get("deckcode_empty");
            GUIUtility.systemCopyBuffer = Encode(deck);
            return Loc.Get("deckcode_copied");
        }

        // ------------------------------------------------------------ import

        /// <summary>The Den's "play a friend's deck" window: paste a code, check it, go to the table.</summary>
        public static void OpenImport()
        {
            if (CustomUIManager.Instance == null) return;
            List<CardDef> deck = null;
            CustomUIElement input = null, status = null, play = null;

            void Check(string code)
            {
                deck = null;
                if (status == null) return;
                if (string.IsNullOrWhiteSpace(code))
                {
                    status.SetText(Loc.Get("deckcode_paste_hint")).SetTextColor(Waiting);
                }
                else
                {
                    List<CardDef> read = Decode(code, out string error, out bool unfinished);
                    List<AugEvent> problems = read == null ? null : AugRules.DeckErrors(read);
                    // an unfinished code is probably being typed: say nothing until there's a whole code
                    if (read == null)
                        status.SetText(unfinished ? "" : Loc.Get(error)).SetTextColor(Bad);
                    else if (problems.Count > 0)
                        status.SetText(Loc.Get("deckcode_illegal", Loc.Get(problems[0].key, AugMatchWindow.Localize(problems[0].args)))).SetTextColor(Bad);
                    else
                    {
                        deck = read;
                        status.SetText(Loc.Get("deckcode_valid", read.Count,
                            read.Count(c => c.kind == AugRules.AUGMENT), read.Count(c => c.kind == AugRules.MOD), read.Count(c => c.kind == AugRules.HACK))).SetTextColor(Good);
                    }
                }
                play?.SetInteractable(deck != null);
            }

            // Labels keep a one-line height even when their text wraps, so the wrapping ones get room here.
            CustomUIBuilder b = CustomUIManager.Window(IMPORT_WINDOW, Loc.Get("deckcode_title"), CustomUIManager.LAYER_META)
                .SetSize(WIDTH, 400f).SetDraggable(true).SetCloseOnEscape(true)
                .BeginColumn(8f);
            b.AddLabel(Loc.Get("deckcode_intro"));
            b.Last.SetPreferredSize(CONTENT_WIDTH, 4 * LINE);
            // checked on every change (onTyping), so pasting is picked up at once, not when the field loses focus
            b.AddInput(Loc.Get("deckcode_placeholder"), "", onTyping: Check);
            input = b.Last;
            input.SetPreferredSize(CONTENT_WIDTH, -1f);
            b.AddButton(Loc.Get("deckcode_paste"), () => input.SetString(GUIUtility.systemCopyBuffer ?? "", notify: true));
            b.AddLabel("");
            status = b.Last;
            status.SetPreferredSize(CONTENT_WIDTH, 2 * LINE);
            b.BeginRow(8f);
            b.AddButton(Loc.Get("deckcode_play"), () =>
            {
                if (deck == null) return;
                CustomUIManager.Instance.CloseWindow(IMPORT_WINDOW);
                AugTable.OpenFriend(deck);
            });
            play = b.Last;
            b.AddButton(Loc.Get("aug_close"), () => CustomUIManager.Instance.CloseWindow(IMPORT_WINDOW));
            b.End();
            b.End().Center().Show();
            Check("");
        }
    }
}
