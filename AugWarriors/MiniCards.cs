using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZamsCollectibles.AugWarriors
{
    /// <summary>
    /// Mini card faces for the Aug Warriors board: 32 x 48 pixel art (2 x 3 inventory cells),
    /// drawn at runtime and added to this mod's item sprites, so match cards are real items
    /// that look like small cards.
    ///
    /// Face: the set's frame, the art, a Power number top right, a slot letter, and pips:
    /// red for Glitch (teal when a mod removes Glitch), teal for Stability.
    /// </summary>
    public static class MiniCards
    {
        private const int W = 32, H = 48;

        private static readonly Color32 Ink = CardFace.Hex("#292234");
        private static readonly Color32 Cream = CardFace.Hex("#d5d2b8");
        private static readonly Color32 CreamLight = CardFace.Hex("#e8e4d6");
        private static readonly Color32 ArtGround = CardFace.Hex("#242227");
        private static readonly Color32 GlitchRed = CardFace.Hex("#c0404a");
        private static readonly Color32 StableTeal = CardFace.Hex("#45918b");

        // 3 x 5 glyphs, rows top to bottom, '#' = ink.
        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['0'] = new[] { "###", "#.#", "#.#", "#.#", "###" },
            ['1'] = new[] { ".#.", "##.", ".#.", ".#.", "###" },
            ['2'] = new[] { "###", "..#", "###", "#..", "###" },
            ['3'] = new[] { "###", "..#", ".##", "..#", "###" },
            ['4'] = new[] { "#.#", "#.#", "###", "..#", "..#" },
            ['5'] = new[] { "###", "#..", "###", "..#", "###" },
            ['6'] = new[] { "###", "#..", "###", "#.#", "###" },
            ['7'] = new[] { "###", "..#", ".#.", ".#.", ".#." },
            ['8'] = new[] { "###", "#.#", "###", "#.#", "###" },
            ['9'] = new[] { "###", "#.#", "###", "..#", "###" },
            ['H'] = new[] { "#.#", "#.#", "###", "#.#", "#.#" },
            ['T'] = new[] { "###", ".#.", ".#.", ".#.", ".#." },
            ['A'] = new[] { ".#.", "#.#", "###", "#.#", "#.#" },
            ['L'] = new[] { "#..", "#..", "#..", "#..", "###" },
            ['M'] = new[] { "#.#", "###", "###", "#.#", "#.#" },
            ['X'] = new[] { "#.#", "#.#", ".#.", "#.#", "#.#" },
        };

        public static string Key(CardDef card) => $"mini_{card.set.id}_{card.number:00}";

        /// <summary>Makes sure the card's mini face is among this mod's item sprites; returns its key.</summary>
        public static string Ensure(CardDef card)
        {
            string key = Key(card);
            LoadedMod mod = ModLoader.Mods.Find(m => m.Manifest.ID == ModEntry.MOD_ID);
            if (mod == null || mod.Sprites.ContainsKey(key)) return key;

            Color32[] art = ReadArt(card, out int aw, out int ah);
            Color32[] px = Draw(card, art, aw, ah);
            Color32[] flipped = new Color32[px.Length];
            for (int y = 0; y < H; y++) Array.Copy(px, y * W, flipped, (H - 1 - y) * W, W);
            Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(flipped);
            tex.Apply();   // stays readable: the game reads the pixels to work out the item's shape
            mod.Sprites[key] = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
            return key;
        }

        // The card's art (or the placeholder), top-down; null when it can't be read.
        private static Color32[] ReadArt(CardDef card, out int w, out int h)
        {
            w = h = 0;
            Sprite art = CustomUIManager.Instance?.GetSprite($"{ModEntry.MOD_ID}:{CardViewer.ArtKey(card)}", true)
                ?? CustomUIManager.Instance?.GetSprite($"{ModEntry.MOD_ID}:{CardViewer.PLACEHOLDER_ART}", true);
            if (art == null) return null;
            try
            {
                Rect r = art.textureRect;
                w = (int)r.width; h = (int)r.height;
                Color[] src = art.texture.GetPixels((int)r.x, (int)r.y, w, h);
                Color32[] px = new Color32[w * h];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = src[(h - 1 - y) * w + x];
                return px;
            }
            catch (UnityException) { return null; }   // unreadable art: the dark ground stays
        }

        // Top-down W x H buffer. Pure, so it can be checked outside the game.
        private static Color32[] Draw(CardDef card, Color32[] art, int aw, int ah)
        {
            Color32[] px = new Color32[W * H];
            if (!CardFace.Frames.TryGetValue(card.set.frameColor ?? "", out Color32[] frame)) frame = CardFace.Frames["slate"];

            Fill(px, 0, 0, W, H, frame[2]);
            Fill(px, 1, 1, W - 2, 1, frame[3]); Fill(px, 1, 1, 1, H - 2, frame[3]);
            Fill(px, 1, H - 2, W - 2, 1, frame[1]); Fill(px, W - 2, 1, 1, H - 2, frame[1]);
            Box(px, 0, 0, W, H);
            foreach (var (x, y) in new[] { (0, 0), (W - 1, 0), (0, H - 1), (W - 1, H - 1) }) px[y * W + x] = default;

            // art: a 2:1 downsample of the centre of the card art
            Fill(px, 2, 2, 28, 24, ArtGround);
            if (art != null) DrawArt(px, art, aw, ah, 3, 3, 26, 22);
            Box(px, 2, 2, 28, 24);

            // Power plate, top right, for augments and mods that change Power
            string gem = CardItems.GemText(card).TrimStart('+').Replace("−", "");
            if (gem.Length > 0)
            {
                Fill(px, 22, 1, 9, 9, Ink);
                Fill(px, 23, 2, 7, 7, CreamLight);
                int gx = gem.Length == 1 ? 25 : 23;
                for (int i = 0; i < gem.Length && i < 2; i++) Glyph(px, gem[i], gx + i * 4, 3, Ink);
            }

            // info panel: slot letter and pips
            Fill(px, 2, 27, 28, 18, Cream);
            Box(px, 2, 27, 28, 18);
            Glyph(px, KindLetter(card), 4, 29, Ink);
            int glitch = card.kind == AugRules.HACK && card.effect == AugRules.GLITCH ? card.amount : card.glitch;
            if (card.kind == AugRules.HACK && card.effect == AugRules.SCRAP) Glyph(px, 'X', 10, 29, GlitchRed);
            else Pips(px, 10, 30, Math.Abs(glitch), glitch < 0 ? StableTeal : GlitchRed);
            Pips(px, 4, 37, card.stability, StableTeal);
            return px;
        }

        private static char KindLetter(CardDef card)
        {
            if (card.kind == AugRules.MOD) return 'M';
            if (card.kind == AugRules.HACK) return 'X';
            switch (card.slot)
            {
                case "head": return 'H';
                case "torso": return 'T';
                case "arm": return 'A';
                default: return 'L';
            }
        }

        // A 2:1 downsample of the centre of the art into the given box.
        private static void DrawArt(Color32[] px, Color32[] art, int aw, int ah, int x0, int y0, int w, int h)
        {
            int ox = (aw - w * 2) / 2, oy = (ah - h * 2) / 2;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sx = ox + x * 2, sy = oy + y * 2;
                    if (sx < 0 || sy < 0 || sx >= aw || sy >= ah) continue;
                    Color32 c = art[sy * aw + sx];
                    if (c.a > 0) px[(y0 + y) * W + x0 + x] = c;
                }
            }
        }

        private static void Pips(Color32[] px, int x0, int y, int count, Color32 color)
        {
            for (int i = 0; i < Math.Min(count, 6); i++) Fill(px, x0 + i * 3, y, 2, 2, color);
        }

        private static void Glyph(Color32[] px, char c, int x0, int y0, Color32 color)
        {
            if (!Glyphs.TryGetValue(c, out string[] rows)) return;
            for (int y = 0; y < 5; y++) for (int x = 0; x < 3; x++) if (rows[y][x] == '#') px[(y0 + y) * W + x0 + x] = color;
        }

        private static void Fill(Color32[] px, int x0, int y0, int w, int h, Color32 c)
        {
            for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++) if (x >= 0 && y >= 0 && x < W && y < H) px[y * W + x] = c;
        }

        private static void Box(Color32[] px, int x0, int y0, int w, int h)
        {
            Fill(px, x0, y0, w, 1, Ink); Fill(px, x0, y0 + h - 1, w, 1, Ink);
            Fill(px, x0, y0, 1, h, Ink); Fill(px, x0 + w - 1, y0, 1, h, Ink);
        }
    }
}
