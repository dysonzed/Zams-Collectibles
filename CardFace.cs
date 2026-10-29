using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZamsCollectibles
{
    /// <summary>
    /// Pixel layout of the card face, in card pixels (shown at ZOOM). y = 0 is the top.
    /// The base image and the window's text slots both use these rectangles.
    /// </summary>
    public static class CardLayout
    {
        public const int WIDTH = 73;
        public const int HEIGHT = 104;
        public const int ZOOM = 3;

        public static readonly RectInt NamePlate = new RectInt(4, 4, 65, 10);
        public static readonly RectInt ArtWell = new RectInt(4, 15, 65, 48);   // keyline box around the art
        public static readonly RectInt Art = new RectInt(5, 16, 63, 46);       // art is drawn 63 x 46
        public static readonly RectInt TypePlate = new RectInt(4, 64, 65, 7);
        public static readonly RectInt TextBox = new RectInt(4, 72, 65, 24);

        public static readonly Vector2Int CostGem = new Vector2Int(63, 9);
        public static readonly Vector2Int RarityGem = new Vector2Int(66, 99);

        // Where the game-font text goes.
        public static readonly RectInt NameText = new RectInt(7, 4, 50, 10);
        public static readonly RectInt CostText = new RectInt(59, 5, 9, 9);
        public static readonly RectInt TypeText = new RectInt(7, 64, 59, 7);
        public static readonly RectInt RulesText = new RectInt(7, 73, 59, 22);
        public static readonly RectInt FooterText = new RectInt(6, 97, 54, 5);
    }

    /// <summary>
    /// Draws the card face in two layers: the base (frame, plates, gems and art, with the
    /// physical edge wear: rubbed corners, nicks, a torn corner) and a transparent wear overlay
    /// (fading, scratches, creases, stains) that the viewer lays over the text as well.
    /// Wear scales smoothly with the hidden exact condition and is placed by a seed from the
    /// copy's unique id: every copy is damaged differently, the same copy always the same.
    /// </summary>
    public static class CardFace
    {
        private const int W = CardLayout.WIDTH;
        private const int H = CardLayout.HEIGHT;

        internal static readonly Color32 Outline = Hex("#292234");
        private static readonly Color32 Cream = Hex("#d5d2b8");
        private static readonly Color32 CreamLight = Hex("#e8e4d6");
        private static readonly Color32 CreamShade = Hex("#aaa982");
        private static readonly Color32 CreamShadow = Hex("#cfc8b0");
        private static readonly Color32 ArtBackground = Hex("#242227");
        private static readonly Color32 Paper = Hex("#e8e4d6");   // exposed card stock
        private static readonly Color32 Grime = Hex("#8a8270");
        private static readonly Color32 Stain = Hex("#6b523a");
        private static readonly Color32 Wash = Hex("#b5ab94");

        // Frame ramps, dark to light, named by the set's "frameColor".
        private static readonly Dictionary<string, Color32[]> Frames = new Dictionary<string, Color32[]>
        {
            ["red"] = new[] { Hex("#64343b"), Hex("#853737"), Hex("#9c3738"), Hex("#cc7755") },
            ["teal"] = new[] { Hex("#275654"), Hex("#2f756f"), Hex("#45918b"), Hex("#67aaa5") },
            ["purple"] = new[] { Hex("#453943"), Hex("#6b5269"), Hex("#8c6a95"), Hex("#a272ac") },
            ["slate"] = new[] { Hex("#303a41"), Hex("#44464c"), Hex("#5f6868"), Hex("#80929b") },
        };
        private static readonly Color32[] CostGemRamp = { Hex("#2f756f"), Hex("#45918b"), Hex("#67aaa5") };

        // The viewer shows one card at a time; the previous textures are freed when a new card is drawn.
        private static readonly List<UnityEngine.Object> lastObjects = new List<UnityEngine.Object>();

        /// <summary>Draws a card's base and wear sprites. Returns false when the art can't be read.</summary>
        public static bool TryBuild(GameItem item, CardDef card, string artKey, out Sprite cardSprite, out Sprite wearSprite)
        {
            cardSprite = wearSprite = null;
            Sprite art = CustomUIManager.Instance.GetSprite(artKey, true);
            if (art == null) return false;

            Rect r = art.textureRect;
            int aw = (int)r.width, ah = (int)r.height;
            Color[] src = art.texture.GetPixels((int)r.x, (int)r.y, aw, ah);
            Color32[] artPx = new Color32[aw * ah];
            for (int y = 0; y < ah; y++)
                for (int x = 0; x < aw; x++)
                    artPx[y * aw + x] = src[(ah - 1 - y) * aw + x];   // to top-down

            Color32 gem = ParseColor(CardDatabase.GetRarity(card.rarity)?.gem, Hex("#a8aea5"));
            float severity = CardCondition.WearSeverity(CardItems.GetCondition(item));
            int seed = item.uniqueId != 0 ? item.uniqueId * 7919 + card.number : card.number;
            Render(artPx, aw, ah, card.set.frameColor, gem, severity, seed, out Color32[] basePx, out Color32[] wearPx);

            foreach (UnityEngine.Object o in lastObjects) if (o != null) UnityEngine.Object.Destroy(o);
            lastObjects.Clear();
            cardSprite = ToSprite(basePx);
            wearSprite = ToSprite(wearPx);
            return true;
        }

        /// <summary>
        /// The pure drawing step, top-down W x H buffers. Separate from Unity's textures so it
        /// can be run and checked outside the game.
        /// </summary>
        public static void Render(Color32[] art, int artW, int artH, string frameColor, Color32 rarityGem,
            float severity, int seed, out Color32[] basePx, out Color32[] wearPx)
        {
            basePx = new Color32[W * H];
            DrawBase(basePx, art, artW, artH, frameColor, rarityGem);
            wearPx = new Color32[W * H];
            new Wear(basePx, wearPx, severity, seed).Apply();
        }

        // ------------------------------------------------------------ base

        private static void DrawBase(Color32[] px, Color32[] art, int artW, int artH, string frameColor, Color32 rarityGem)
        {
            if (frameColor == null || !Frames.TryGetValue(frameColor, out Color32[] frame)) frame = Frames["slate"];

            // bevelled frame
            Fill(px, 0, 0, W, H, frame[2]);
            Fill(px, 1, 1, W - 2, 1, frame[3]);
            Fill(px, 1, 1, 1, H - 2, frame[3]);
            Fill(px, 1, H - 2, W - 2, 1, frame[1]);
            Fill(px, W - 2, 1, 1, H - 2, frame[1]);
            Box(px, new RectInt(0, 0, W, H), Outline);
            foreach (Vector2Int c in new[] { new Vector2Int(0, 0), new Vector2Int(W - 1, 0), new Vector2Int(0, H - 1), new Vector2Int(W - 1, H - 1) })
                px[c.y * W + c.x] = default;
            foreach (Vector2Int c in new[] { new Vector2Int(1, 1), new Vector2Int(W - 2, 1), new Vector2Int(1, H - 2), new Vector2Int(W - 2, H - 2) })
                px[c.y * W + c.x] = Outline;

            Plate(px, CardLayout.NamePlate, Cream, CreamLight, CreamShade);
            Plate(px, CardLayout.TypePlate, Cream, CreamLight, CreamShade);

            // text box: lighter stock with a soft inner shadow
            RectInt tb = CardLayout.TextBox;
            Fill(px, tb.x, tb.y, tb.width, tb.height, CreamLight);
            Fill(px, tb.x + 1, tb.y + 1, tb.width - 2, 1, CreamShadow);
            Fill(px, tb.x + 1, tb.y + 1, 1, tb.height - 2, CreamShadow);
            Box(px, tb, Outline);

            // art: centre-cropped into the well, on a dark ground where it is smaller
            RectInt a = CardLayout.Art;
            Fill(px, CardLayout.ArtWell.x, CardLayout.ArtWell.y, CardLayout.ArtWell.width, CardLayout.ArtWell.height, ArtBackground);
            int ox = (artW - a.width) / 2, oy = (artH - a.height) / 2;
            for (int y = 0; y < a.height; y++)
            {
                for (int x = 0; x < a.width; x++)
                {
                    int sx = x + ox, sy = y + oy;
                    if (sx < 0 || sy < 0 || sx >= artW || sy >= artH) continue;
                    Color32 c = art[sy * artW + sx];
                    if (c.a > 0) px[(a.y + y) * W + a.x + x] = c;
                }
            }
            Box(px, CardLayout.ArtWell, Outline);

            Gem(px, CardLayout.CostGem, 3, CostGemRamp[1], CostGemRamp[2], CostGemRamp[0]);
            Gem(px, CardLayout.RarityGem, 2, rarityGem, Color32.Lerp(rarityGem, CreamLight, 0.5f), Color32.Lerp(rarityGem, Outline, 0.4f));
        }

        // A cream plate: dark border, highlight along the top, shade along the bottom.
        private static void Plate(Color32[] px, RectInt r, Color32 fill, Color32 light, Color32 shade)
        {
            Fill(px, r.x, r.y, r.width, r.height, fill);
            Fill(px, r.x + 1, r.y + 1, r.width - 2, 1, light);
            Fill(px, r.x + 1, r.y + r.height - 2, r.width - 2, 1, shade);
            Box(px, r, Outline);
        }

        // A diamond gem lit from the top left.
        private static void Gem(Color32[] px, Vector2Int c, int radius, Color32 mid, Color32 light, Color32 dark)
        {
            for (int dy = -radius - 1; dy <= radius + 1; dy++)
            {
                for (int dx = -radius - 1; dx <= radius + 1; dx++)
                {
                    int d = Math.Abs(dx) + Math.Abs(dy);
                    if (d > radius + 1) continue;
                    Color32 col = d == radius + 1 ? Outline : (dx + dy < 0 ? light : dx + dy > 0 ? dark : mid);
                    Set(px, c.x + dx, c.y + dy, col);
                }
            }
        }

        // ------------------------------------------------------------ wear

        private class Wear
        {
            private readonly Color32[] basePx;
            private readonly Color32[] wear;
            private readonly float s;
            private readonly System.Random rng;

            public Wear(Color32[] basePx, Color32[] wear, float severity, int seed)
            {
                this.basePx = basePx;
                this.wear = wear;
                s = severity;
                rng = new System.Random(seed);
            }

            public void Apply()
            {
                if (s < 0.02f) return; // a 9.8+ card is pristine

                // physical edge wear, on the card itself
                for (int corner = 0; corner < 4; corner++) RubCorner(corner, s * 7f * Range(0.6f, 1.2f));
                int nicks = (int)(s * 14f + rng.NextDouble() * s * 4f);
                for (int i = 0; i < nicks; i++) EdgeNick((int)(1 + rng.NextDouble() * s * 4f));

                // surface wear, on the overlay so it also covers the text
                Paint(0, 0, W, H, Wash, s * 0.32f);
                int scratches = (int)(s * s * 9f + (s > 0.15f ? rng.NextDouble() * 2.0 : 0.0));
                for (int i = 0; i < scratches; i++) Scratch();
                if (s > 0.4f) Crease();
                if (s > 0.75f) Crease();
                if (s > 0.6f)
                {
                    int stains = 1 + (int)((s - 0.6f) * 5f);
                    for (int i = 0; i < stains; i++) StainBlob();
                }

                if (s > 0.85f) TearCorner(3 + (int)((s - 0.85f) * 30f));

                // the overlay only exists where the card does
                for (int i = 0; i < wear.Length; i++) if (basePx[i].a == 0) wear[i] = default;
            }

            private void RubCorner(int corner, float radius)
            {
                int cx = (corner & 1) == 0 ? 0 : W - 1;
                int cy = (corner & 2) == 0 ? 0 : H - 1;
                int r = (int)Math.Ceiling(radius);
                for (int dy = 0; dy <= r; dy++)
                {
                    for (int dx = 0; dx <= r; dx++)
                    {
                        float d = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (d >= radius || rng.NextDouble() > 1f - d / radius) continue;
                        Rub(cx == 0 ? dx : cx - dx, cy == 0 ? dy : cy - dy, 0.7f);
                    }
                }
            }

            private void EdgeNick(int length)
            {
                int edge = rng.Next(4);
                bool horizontal = edge < 2;
                int span = horizontal ? W : H;
                int start = rng.Next(3, span - 3 - length);
                int depth = 1 + rng.Next(2);
                for (int i = 0; i < length; i++)
                {
                    for (int d = 1; d <= depth; d++)
                    {
                        int along = start + i;
                        if (horizontal) Rub(along, edge == 0 ? d : H - 1 - d, 0.6f);
                        else Rub(edge == 2 ? d : W - 1 - d, along, 0.6f);
                    }
                }
            }

            // Rubbing exposes white stock on dark ink, but shows as grime on light stock.
            private void Rub(int x, int y, float amount)
            {
                if (x < 0 || y < 0 || x >= W || y >= H) return;
                Color32 c = basePx[y * W + x];
                if (c.a == 0 || Same(c, Outline)) return;
                bool light = Luma(c) > 185f;
                basePx[y * W + x] = Color32.Lerp(c, light ? Grime : Paper, light ? amount * 0.6f : amount);
            }

            private void Scratch()
            {
                double x = rng.NextDouble() * W, y = rng.NextDouble() * H;
                double angle = rng.NextDouble() * Math.PI * 2;
                int length = 6 + (int)(rng.NextDouble() * s * 20f);
                float strength = 0.35f + s * 0.2f;
                for (int i = 0; i < length; i++)
                {
                    if (rng.NextDouble() < 0.85) Mark((int)x, (int)y, strength);
                    x += Math.Cos(angle);
                    y += Math.Sin(angle);
                }
            }

            // A fold across the whole card: a light ridge with a shadow beside it.
            private void Crease()
            {
                double x = rng.NextDouble() * W, y = rng.NextDouble() * H;
                double angle = rng.NextDouble() * Math.PI;
                double dx = Math.Cos(angle), dy = Math.Sin(angle);
                int sx = (int)Math.Round(-dy), sy = (int)Math.Round(dx);
                for (int dir = -1; dir <= 1; dir += 2)
                {
                    double cx = x, cy = y;
                    while (cx >= 0 && cy >= 0 && cx < W && cy < H)
                    {
                        Mark((int)cx, (int)cy, 0.45f);
                        Paint((int)cx + sx, (int)cy + sy, 1, 1, Outline, 0.25f);
                        cx += dx * dir;
                        cy += dy * dir;
                    }
                }
            }

            private void StainBlob()
            {
                int cx = rng.Next(W), cy = rng.Next(H);
                float r = 3f + (float)rng.NextDouble() * s * 5f;
                for (int y = (int)(cy - r - 1); y <= cy + r + 1; y++)
                    for (int x = (int)(cx - r - 1); x <= cx + r + 1; x++)
                    {
                        float d = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        if (d < r * Range(0.75f, 1.05f)) Paint(x, y, 1, 1, Stain, 0.35f);
                    }
            }

            private void TearCorner(int size)
            {
                int corner = rng.Next(4);
                bool left = (corner & 1) == 0, top = (corner & 2) == 0;
                for (int dy = 0; dy <= size; dy++)
                {
                    for (int dx = 0; dx <= size; dx++)
                    {
                        int x = left ? dx : W - 1 - dx, y = top ? dy : H - 1 - dy;
                        if (dx + dy < size) basePx[y * W + x] = default;
                        else if (dx + dy == size && basePx[y * W + x].a != 0) basePx[y * W + x] = Outline;
                    }
                }
            }

            // A light mark: paper on dark areas, grime on light ones.
            private void Mark(int x, int y, float amount)
            {
                if (x < 0 || y < 0 || x >= W || y >= H) return;
                bool light = Luma(basePx[y * W + x]) > 185f;
                Paint(x, y, 1, 1, light ? Grime : Paper, light ? amount * 0.6f : amount);
            }

            // Alpha-composites a colour over the overlay.
            private void Paint(int x0, int y0, int w, int h, Color32 col, float alpha)
            {
                for (int y = Math.Max(0, y0); y < Math.Min(H, y0 + h); y++)
                {
                    for (int x = Math.Max(0, x0); x < Math.Min(W, x0 + w); x++)
                    {
                        Color32 d = wear[y * W + x];
                        float da = d.a / 255f;
                        float oa = alpha + da * (1f - alpha);
                        if (oa <= 0f) continue;
                        wear[y * W + x] = new Color32(
                            Byte((col.r * alpha + d.r * da * (1f - alpha)) / oa),
                            Byte((col.g * alpha + d.g * da * (1f - alpha)) / oa),
                            Byte((col.b * alpha + d.b * da * (1f - alpha)) / oa),
                            Byte(oa * 255f));
                    }
                }
            }

            private float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);
        }

        // ------------------------------------------------------------ helpers

        private static Sprite ToSprite(Color32[] px)
        {
            Color32[] flipped = new Color32[px.Length];
            for (int y = 0; y < H; y++) Array.Copy(px, y * W, flipped, (H - 1 - y) * W, W);
            Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels32(flipped);
            tex.Apply();
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
            lastObjects.Add(tex);
            lastObjects.Add(sprite);
            return sprite;
        }

        private static void Fill(Color32[] px, int x0, int y0, int w, int h, Color32 c)
        {
            for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++) Set(px, x, y, c);
        }

        private static void Box(Color32[] px, RectInt r, Color32 c)
        {
            Fill(px, r.x, r.y, r.width, 1, c);
            Fill(px, r.x, r.y + r.height - 1, r.width, 1, c);
            Fill(px, r.x, r.y, 1, r.height, c);
            Fill(px, r.x + r.width - 1, r.y, 1, r.height, c);
        }

        private static void Set(Color32[] px, int x, int y, Color32 c)
        {
            if (x >= 0 && y >= 0 && x < W && y < H) px[y * W + x] = c;
        }

        private static float Luma(Color32 c) => c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;

        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

        private static byte Byte(float v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

        internal static Color32 Hex(string hex) =>
            new Color32(Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16), 0xFF);

        internal static Color32 ParseColor(string hex, Color32 fallback) =>
            !string.IsNullOrEmpty(hex) && hex.Length == 7 && hex[0] == '#' ? Hex(hex) : fallback;
    }
}
