using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZamsCollectibles.AugWarriors
{
    /// <summary>
    /// Pixel art for the Aug Warriors board, drawn at runtime: the skeleton shown in the empty
    /// pockets of each Aug (cut into the board's 16 px cells, used as cell backdrops), and the
    /// stability bar.
    /// </summary>
    public static class AugArt
    {
        private const int CELL = 16;

        private static readonly Color32 Bone = new Color32(0x4E, 0x48, 0x5A, 0xFF);
        private static readonly Color32 BoneLight = new Color32(0x60, 0x5A, 0x6C, 0xFF);
        private static readonly Color32 Hollow = new Color32(0x26, 0x23, 0x2C, 0xFF);

        private static Dictionary<(int, int), Sprite> skeleton;

        /// <summary>The skeleton, one sprite per board cell (board coordinates, y = 0 at the top).</summary>
        public static Dictionary<(int x, int y), Sprite> Skeleton(int columns, int rows)
        {
            if (skeleton != null) return skeleton;
            int w = columns * CELL, h = rows * CELL;
            Color32[] px = new Color32[w * h];
            DrawSkeleton(px, w, h);

            Texture2D tex = ToTexture(px, w, h);
            skeleton = new Dictionary<(int, int), Sprite>();
            for (int cy = 0; cy < rows; cy++)
                for (int cx = 0; cx < columns; cx++)
                    skeleton[(cx, cy)] = Sprite.Create(tex, new Rect(cx * CELL, h - (cy + 1) * CELL, CELL, CELL), new Vector2(0.5f, 0.5f), 100f);
            return skeleton;
        }

        // Pockets (in px): head (48,0), arms (0,64) and (96,64), torso (48,64), legs (48,128); each 32 x 48.
        private static void DrawSkeleton(Color32[] px, int w, int h)
        {
            void Set(int x, int y, Color32 c) { if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = c; }
            void Rect(int x0, int y0, int x1, int y1, Color32 c) { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Set(x, y, c); }
            void Oval(int cx, int cy, int rx, int ry, Color32 c)
            {
                for (int y = -ry; y <= ry; y++)
                    for (int x = -rx; x <= rx; x++)
                        if ((x * x) / (float)(rx * rx) + (y * y) / (float)(ry * ry) <= 1f) Set(cx + x, cy + y, c);
            }

            // head: skull, sockets, nose, teeth
            Oval(64, 19, 11, 12, Bone);
            Oval(62, 16, 7, 7, BoneLight);
            Rect(57, 27, 71, 34, Bone);
            Oval(59, 20, 3, 3, Hollow); Oval(69, 20, 3, 3, Hollow);
            Rect(63, 24, 65, 26, Hollow);
            for (int x = 58; x <= 70; x += 3) Rect(x, 30, x, 33, Hollow);
            Rect(62, 36, 66, 47, Bone);

            // torso: spine, ribs, pelvis
            Rect(62, 64, 66, 105, Bone);
            for (int k = 0; k < 5; k++)
            {
                int y = 68 + k * 6, reach = 12 - k;
                Rect(64 - reach, y, 61, y + 2, Bone); Rect(67, y, 64 + reach, y + 2, Bone);
                Rect(64 - reach, y, 64 - reach + 1, y + 4, Bone); Rect(64 + reach - 1, y, 64 + reach, y + 4, Bone);
            }
            Oval(64, 106, 11, 5, Bone); Oval(64, 106, 5, 2, Hollow);

            // arms: upper arm, elbow, two forearm bones, hand; the right one mirrored
            for (int side = 0; side < 2; side++)
            {
                int Mx(int x) => side == 0 ? x : 96 + (31 - x);
                Oval(Mx(16), 67, 4, 3, BoneLight);
                for (int y = 68; y <= 86; y++) for (int x = 14; x <= 18; x++) Set(Mx(x), y, Bone);
                Oval(Mx(16), 88, 3, 3, BoneLight);
                for (int y = 90; y <= 103; y++) { Set(Mx(13), y, Bone); Set(Mx(14), y, Bone); Set(Mx(18), y, Bone); Set(Mx(19), y, Bone); }
                Oval(Mx(16), 106, 4, 3, Bone);
                for (int f = 0; f < 4; f++) for (int y = 108; y <= 110; y++) Set(Mx(13 + f * 2), y, Bone);
            }

            // legs: two femurs, knees, shins, feet
            foreach (int lx in new[] { 57, 69 })
            {
                Rect(lx, 130, lx + 3, 148, Bone);
                Oval(lx + 1, 150, 3, 2, BoneLight);
                Rect(lx, 152, lx + 1, 168, Bone); Rect(lx + 3, 152, lx + 3, 168, Bone);
                Rect(lx - 1, 170, lx + 5, 172, Bone);
            }
        }

        private static readonly Texture2D[] lastBar = new Texture2D[2];
        private static readonly Sprite[] lastBarSprite = new Sprite[2];

        private static readonly Color32 Ink = new Color32(0x29, 0x22, 0x34, 0xFF);
        private static readonly Color32 Segment = new Color32(0x45, 0x91, 0x8B, 0xFF);
        private static readonly Color32 SegmentLight = new Color32(0x67, 0xAA, 0xA5, 0xFF);
        private static readonly Color32 Used = new Color32(0xE0, 0x50, 0x5A, 0xFF);
        private static readonly Color32 Bolt = new Color32(0xE0, 0xB0, 0x50, 0xFF);

        /// <summary>
        /// One segment per point of Stability; each point of Glitch rings one segment in red as it
        /// uses it up. Past the limit, a lightning bolt at the end.
        /// </summary>
        public static Sprite StabilityBar(int who, int stability, int glitch)
        {
            // A fixed-width canvas with the segments centred: the element keeps its size and place
            // however many segments there are (up to 14 shown).
            const int SEG_W = 6, SEG_H = 7, STEP = 8, BOLT_W = 9, CANVAS = 128, MAX_SEGMENTS = 14;
            int segments = Math.Min(MAX_SEGMENTS, Math.Max(1, stability));
            bool over = glitch > stability;
            int span = segments * STEP + (over ? BOLT_W + 2 : 0);
            int w = CANVAS, h = SEG_H + 3, offset = (CANVAS - span) / 2;
            Color32[] px = new Color32[w * h];
            void Set(int x, int y, Color32 c) { if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = c; }

            for (int i = 0; i < segments; i++)
            {
                int x0 = offset + i * STEP + 1, y0 = 1;
                bool used = i < glitch;
                for (int y = 0; y < SEG_H; y++)
                {
                    for (int x = 0; x < SEG_W; x++)
                    {
                        bool edge = x == 0 || y == 0 || x == SEG_W - 1 || y == SEG_H - 1;
                        Set(x0 + x, y0 + y, edge ? (used ? Used : Ink) : (y < 3 ? SegmentLight : Segment));
                    }
                }
                if (used) for (int y = -1; y <= SEG_H; y++) { Set(x0 - 1, y0 + y, Used); Set(x0 + SEG_W, y0 + y, Used); }
            }
            if (over)
            {
                string[] bolt = { "....##", "...##.", "..##..", ".#####", "...##.", "..##..", ".##...", "##....", "#....." };
                int bx = offset + segments * STEP + 2;
                for (int y = 0; y < bolt.Length && y < h; y++)
                    for (int x = 0; x < bolt[y].Length; x++)
                        if (bolt[y][x] == '#') Set(bx + x, y, Bolt);
            }

            // one bar per player; the previous one is freed
            if (lastBarSprite[who] != null) UnityEngine.Object.Destroy(lastBarSprite[who]);
            if (lastBar[who] != null) UnityEngine.Object.Destroy(lastBar[who]);
            lastBar[who] = ToTexture(px, w, h);
            lastBarSprite[who] = Sprite.Create(lastBar[who], new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            return lastBarSprite[who];
        }

        // ------------------------------------------------------------ coin

        private const int COIN = 24;
        private static readonly Color32 Gold = new Color32(0xE0, 0xB0, 0x50, 0xFF);
        private static readonly Color32 GoldLight = new Color32(0xF2, 0xD6, 0x8A, 0xFF);
        private static readonly Color32 GoldDark = new Color32(0x9B, 0x6A, 0x25, 0xFF);
        private static readonly Dictionary<(bool, int), Sprite> coins = new Dictionary<(bool, int), Sprite>();

        /// <summary>
        /// The toss coin: heads (Zamco's Z) or tails (a diamond), squashed horizontally to
        /// width (1 = face on, near 0 = edge on) for the flip animation. Same canvas size always.
        /// </summary>
        public static Sprite Coin(bool heads, float width)
        {
            int cols = Math.Max(2, (int)Math.Round(COIN * Mathf.Clamp01(width)));
            if (coins.TryGetValue((heads, cols), out Sprite cached)) return cached;

            Color32[] face = new Color32[COIN * COIN];
            void Set(int x, int y, Color32 c) { if (x >= 0 && y >= 0 && x < COIN && y < COIN) face[y * COIN + x] = c; }
            float r = COIN / 2f - 0.5f;
            for (int y = 0; y < COIN; y++)
            {
                for (int x = 0; x < COIN; x++)
                {
                    float dx = x - r, dy = y - r, d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d > r + 0.3f) continue;
                    Set(x, y, d > r - 1f ? Ink : d > r - 3f ? GoldDark : (dx + dy < -4f ? GoldLight : Gold));
                }
            }
            if (heads)
            {
                for (int x = 7; x <= 16; x++) { Set(x, 7, GoldDark); Set(x, 8, GoldDark); Set(x, 15, GoldDark); Set(x, 16, GoldDark); }
                for (int i = 0; i < 7; i++) { Set(15 - i, 9 + i, GoldDark); Set(16 - i, 9 + i, GoldDark); }   // the diagonal of the Z
            }
            else
            {
                for (int y = -5; y <= 5; y++) for (int x = -5 + Math.Abs(y); x <= 5 - Math.Abs(y); x++) Set(12 + x, 12 + y, Math.Abs(x) + Math.Abs(y) >= 4 ? GoldDark : GoldLight);
            }

            // squash: resample the face into `cols` columns, centred on the same canvas
            Color32[] px = new Color32[COIN * COIN];
            int left = (COIN - cols) / 2;
            for (int y = 0; y < COIN; y++)
                for (int x = 0; x < cols; x++)
                    px[y * COIN + left + x] = face[y * COIN + Math.Min(COIN - 1, x * COIN / cols)];

            Texture2D tex = ToTexture(px, COIN, COIN);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, COIN, COIN), new Vector2(0.5f, 0.5f), 100f);
            coins[(heads, cols)] = sprite;
            return sprite;
        }

        private static Sprite blank;

        /// <summary>A single transparent pixel: "hides" a sprite element (they have no hide of their own).</summary>
        public static Sprite Blank()
        {
            if (blank == null) blank = Sprite.Create(ToTexture(new Color32[1], 1, 1), new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
            return blank;
        }

        // Top-down pixels to a point-filtered texture.
        private static Texture2D ToTexture(Color32[] px, int w, int h)
        {
            Color32[] flipped = new Color32[px.Length];
            for (int y = 0; y < h; y++) Array.Copy(px, y * w, flipped, (h - 1 - y) * w, w);
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(flipped);
            tex.Apply();
            return tex;
        }
    }
}
