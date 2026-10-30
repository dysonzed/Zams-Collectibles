using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZamsCollectibles
{
    /// <summary>
    /// The card viewer as a card: a frameless window whose body is the card face. Text comes from
    /// the game's own UI labels (so it follows the game's fonts and language) and is moved onto
    /// the card's plates; the wear overlay is drawn on top of everything, text included.
    ///
    /// Layers, bottom to top: card base image, text slots, wear overlay.
    /// Uses only public modding API: CustomUIManager.CreateWindow with a window body of our own
    /// (it carries the public CustomUIWindowFrame component) and the builder's labels.
    /// </summary>
    public static class CardWindow
    {
        // Each open card is its own window: WINDOW_ID.<n>.
        public const string WINDOW_ID = ModEntry.MOD_ID + ".card_viewer";

        // Open cards by item, so opening one twice brings it forward instead.
        private static readonly Dictionary<GameItem, CustomUIWindow> open = new Dictionary<GameItem, CustomUIWindow>();
        private static int serial;

        // New cards open staggered down and to the right, so a card never hides under the last one.
        private const float CASCADE_STEP = 28f;
        private const int CASCADE_STEPS = 8;

        private static readonly Color Ink = new Color32(0x29, 0x22, 0x34, 0xFF);
        private static readonly Color FooterInk = new Color32(0xE8, 0xE4, 0xD6, 0xFF);
        private static readonly Color CostInk = new Color32(0xF4, 0xF1, 0xE4, 0xFF);

        // A graded card is shown sealed in its slab: a plastic case around the card, with the grade
        // label above it. In card pixels:
        private const int SLAB_SIDE = 5, SLAB_TOP = 21;
        private static readonly RectInt GradeText = new RectInt(-1, -17, CardLayout.WIDTH + 2, 12);   // on the label
        private static readonly Color LabelInk = new Color32(0xFF, 0xF4, 0xEE, 0xFF);

        // Where the card sits in the window, in card pixels: (0, 0) unless it's in a slab.
        private static int offX, offY;

        /// <summary>The open card's body (the card, or its slab), for capturing renders.</summary>
        public static RectTransform Body { get; internal set; }

        public static string NextId() => $"{WINDOW_ID}.{++serial}";

        /// <summary>Brings the item's card to the front if it is open; false if it isn't.</summary>
        public static bool Focus(GameItem item)
        {
            if (!open.TryGetValue(item, out CustomUIWindow window)) return false;
            if (window == null || !window.IsAlive) { open.Remove(item); return false; }
            window.BringToFront();
            return true;
        }

        /// <summary>Closes the item's card, if it is open.</summary>
        public static void Close(GameItem item)
        {
            if (open.TryGetValue(item, out CustomUIWindow window) && window != null && window.IsAlive) window.Close();
            open.Remove(item);
        }

        /// <summary>Tracks a newly opened card window and staggers it from the cards already open.</summary>
        public static void Track(GameItem item, CustomUIWindow window)
        {
            foreach (GameItem gone in open.Where(kv => kv.Value == null || !kv.Value.IsAlive).Select(kv => kv.Key).ToList())
                open.Remove(gone);
            int step = open.Count % CASCADE_STEPS;
            window.SetPosition(window.Rect.anchoredPosition + new Vector2(step * CASCADE_STEP, -step * CASCADE_STEP));
            open[item] = window;   // closed windows drop out on the next Focus or Track
        }

        public static void Open(GameItem item, CardDef card, string artKey, string windowId)
        {
            if (!CardFace.TryBuild(item, card, artKey, out Sprite cardSprite, out Sprite wearSprite))
                throw new InvalidOperationException($"card face could not be drawn ({artKey})");

            bool graded = CardItems.IsGraded(item);
            offX = graded ? SLAB_SIDE : 0;
            offY = graded ? SLAB_TOP : 0;
            List<Sprite> sprites = new List<Sprite> { cardSprite, wearSprite };
            GameObject body = BuildBody(cardSprite, wearSprite, graded, sprites);
            CustomUIBuilder builder;
            try
            {
                builder = CustomUIManager.Instance.CreateWindow(windowId, null, CustomUIManager.LAYER_PAPER, body);
            }
            catch
            {
                FreeSprites.Free(sprites);
                throw;
            }
            finally
            {
                UnityEngine.Object.Destroy(body);   // the window made its own copy
            }

            string rules = Loc.CardRules(card);
            string flavor = Loc.CardFlavor(card);
            if (!string.IsNullOrEmpty(flavor))
                rules = (string.IsNullOrEmpty(rules) ? "" : rules + "\n") + $"<i><color=#5f5a66>{flavor}</color></i>";

            builder
                .SetDraggable(true)
                .SetCloseOnEscape(true)
                .AddLabel(Loc.CardTitle(card), "name")
                .AddLabel(CardItems.GemText(card), "cost")
                .AddLabel(CardItems.TypeLine(card), "type")
                .AddLabel(rules, "rules")
                .AddLabel(CardItems.SetNumber(card), "footer");
            float condition = CardItems.GetCondition(item);
            if (graded) builder.AddLabel(Loc.Get("slab_label", CardCondition.GradeOf(condition).ToString("0.0"), CardCondition.Label(condition)), "grade");
            CustomUIWindow window = builder.Center().Show();
            Body = window.root.transform as RectTransform;
            window.root.GetComponent<CloseOnClick>().windowId = windowId;
            // each card owns its textures, freed with its window (other open cards keep theirs)
            window.root.AddComponent<FreeSprites>().sprites = sprites;
            Track(item, window);

            Place(window, "name", CardLayout.NameText, 18f, TextAlignmentOptions.MidlineLeft, Ink, FontStyles.Bold);
            Place(window, "cost", CardLayout.CostText, 16f, TextAlignmentOptions.Center, CostInk, FontStyles.Bold);
            Place(window, "type", CardLayout.TypeText, 12f, TextAlignmentOptions.MidlineLeft, Ink, FontStyles.Normal);
            Place(window, "rules", CardLayout.RulesText, 13f, TextAlignmentOptions.TopLeft, Ink, FontStyles.Normal);
            Place(window, "footer", CardLayout.FooterText, 10f, TextAlignmentOptions.MidlineLeft, FooterInk, FontStyles.Normal);
            if (graded) Place(window, "grade", GradeText, 13f, TextAlignmentOptions.Center, LabelInk, FontStyles.Bold);
        }

        // ------------------------------------------------------------ window body

        private static GameObject BuildBody(Sprite cardSprite, Sprite wearSprite, bool graded, List<Sprite> sprites)
        {
            int z = CardLayout.ZOOM;
            GameObject root = new GameObject("zams_card", typeof(RectTransform));
            root.SetActive(false);   // the copy stays hidden until the builder shows it
            ((RectTransform)root.transform).sizeDelta = new Vector2((CardLayout.WIDTH + 2 * offX) * z, (CardLayout.HEIGHT + offY + offX) * z);

            // In a slab the case is the window's background and the card sits inside it.
            Image back = root.AddComponent<Image>();
            if (graded) sprites.Add(SlabSprite());
            back.sprite = graded ? sprites[sprites.Count - 1] : cardSprite;
            back.raycastTarget = true;
            if (graded)
            {
                Image face = Child(root, "face", new RectInt(0, 0, CardLayout.WIDTH, CardLayout.HEIGHT)).gameObject.AddComponent<Image>();
                face.sprite = cardSprite;
                face.raycastTarget = false;
            }

            // Clicking the card closes it; moving it does not.
            root.AddComponent<CloseOnClick>();

            // Builder content lands here first; Place moves each label onto its slot.
            RectTransform content = Child(root, "content", new RectInt(0, 0, CardLayout.WIDTH, CardLayout.HEIGHT));
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            CustomUIWindowFrame frame = root.AddComponent<CustomUIWindowFrame>();
            frame.contentRoot = content;

            Child(root, "name", CardLayout.NameText);
            Child(root, "cost", CardLayout.CostText);
            Child(root, "type", CardLayout.TypeText);
            Child(root, "rules", CardLayout.RulesText);
            Child(root, "footer", CardLayout.FooterText);

            Image wear = Child(root, "wear", new RectInt(0, 0, CardLayout.WIDTH, CardLayout.HEIGHT)).gameObject.AddComponent<Image>();
            wear.sprite = wearSprite;
            wear.raycastTarget = false;

            if (graded)
            {
                // glare on the plastic, over the card
                Image glass = Child(root, "glass", new RectInt(0, 0, CardLayout.WIDTH, CardLayout.HEIGHT)).gameObject.AddComponent<Image>();
                glass.sprite = GlassSprite();
                sprites.Add(glass.sprite);
                glass.raycastTarget = false;
                Child(root, "grade", GradeText);
            }
            return root;
        }

        private static readonly Color32 SlabInk = new Color32(0x29, 0x22, 0x34, 0xFF);
        private static readonly Color32 SlabPlastic = new Color32(0xC4, 0xD6, 0xDE, 0xFF);
        private static readonly Color32 SlabPlasticHi = new Color32(0xF0, 0xF8, 0xFC, 0xFF);
        private static readonly Color32 SlabPlasticLo = new Color32(0x98, 0xAC, 0xB8, 0xFF);
        private static readonly Color32 SlabLabel = new Color32(0xD6, 0x30, 0x4E, 0xFF);
        private static readonly Color32 SlabLabelHi = new Color32(0xFF, 0x78, 0x82, 0xFF);

        // The slab around the card: an outlined plastic case with a red label at the top.
        private static Sprite SlabSprite()
        {
            int w = CardLayout.WIDTH + 2 * SLAB_SIDE, h = CardLayout.HEIGHT + SLAB_TOP + SLAB_SIDE;
            Color32[] px = new Color32[w * h];
            void Rect(int x0, int y0, int rw, int rh, Color32 c)
            {
                // drawn top-down; Unity textures are bottom-up
                for (int y = y0; y < y0 + rh; y++) for (int x = x0; x < x0 + rw; x++) px[(h - 1 - y) * w + x] = c;
            }
            Rect(0, 0, w, h, SlabInk);
            Rect(1, 1, w - 2, h - 2, SlabPlastic);
            Rect(1, 1, w - 2, 1, SlabPlasticHi); Rect(1, 1, 1, h - 2, SlabPlasticHi);
            Rect(1, h - 2, w - 2, 1, SlabPlasticLo); Rect(w - 2, 1, 1, h - 2, SlabPlasticLo);
            Rect(3, 3, w - 6, 15, SlabInk);                    // the grade label
            Rect(4, 4, w - 8, 13, SlabLabel);
            Rect(4, 4, w - 8, 1, SlabLabelHi);
            Rect(SLAB_SIDE - 1, SLAB_TOP - 1, CardLayout.WIDTH + 2, CardLayout.HEIGHT + 2, SlabInk);   // the card's well
            return MakeSprite(px, w, h);
        }

        // A faint diagonal glare across the card, as through plastic.
        private static Sprite GlassSprite()
        {
            int w = CardLayout.WIDTH, h = CardLayout.HEIGHT;
            Color32[] px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int d = (x + (h - 1 - y)) % 60;
                    if (d < 6 || (d > 9 && d < 11)) px[y * w + x] = new Color32(255, 255, 255, 38);
                }
            return MakeSprite(px, w, h);
        }

        private static Sprite MakeSprite(Color32[] px, int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        // A child rectangle placed in card pixels from the top-left corner.
        private static RectTransform Child(GameObject parent, string name, RectInt r)
        {
            int z = CardLayout.ZOOM;
            RectTransform rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2((r.x + offX) * z, -(r.y + offY) * z);
            rt.sizeDelta = new Vector2(r.width * z, r.height * z);
            return rt;
        }

        // Moves a builder label into its slot and makes it fit: shrink-to-fit text, card ink.
        private static void Place(CustomUIWindow window, string id, RectInt slotRect, float maxSize,
            TextAlignmentOptions alignment, Color color, FontStyles style)
        {
            CustomUIElement element = window.Get(id);
            Transform slot = window.root.transform.Find(id);
            if (element == null || element.root == null || element.text == null || slot == null)
                throw new InvalidOperationException($"card text '{id}' could not be placed");

            Stretch((RectTransform)element.root.transform, slot);
            if (element.text.transform != element.root.transform)
                Stretch((RectTransform)element.text.transform, element.text.transform.parent);
            foreach (LayoutGroup group in element.root.GetComponentsInChildren<LayoutGroup>()) group.enabled = false;
            foreach (ContentSizeFitter fitter in element.root.GetComponentsInChildren<ContentSizeFitter>()) fitter.enabled = false;

            TextMeshProUGUI text = element.text;
            text.enableAutoSizing = true;
            text.fontSizeMin = 6f;
            text.fontSizeMax = maxSize;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Truncate;
            text.alignment = alignment;
            text.fontStyle = style;
            text.color = color;
            text.margin = Vector4.zero;
            text.raycastTarget = false;
        }

        /// <summary>
        /// Closes the card on a click. A Button can't do this: with the drag handler on the same
        /// object, Unity still reports press, drag and release as a click, so a moved card closed.
        /// </summary>
        private class CloseOnClick : MonoBehaviour, IPointerClickHandler
        {
            // Screen pixels the pointer may move between press and release and still count as a click.
            private const float CLICK_SLOP = 6f;

            public string windowId;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button != PointerEventData.InputButton.Left) return;
                if (eventData.dragging || (eventData.position - eventData.pressPosition).sqrMagnitude > CLICK_SLOP * CLICK_SLOP) return;
                CustomUIManager.Instance?.CloseWindow(windowId);
            }
        }

        /// <summary>Frees a card window's own sprites and their textures when the window goes.</summary>
        private class FreeSprites : MonoBehaviour
        {
            public List<Sprite> sprites;

            private void OnDestroy() => Free(sprites);

            public static void Free(List<Sprite> sprites)
            {
                if (sprites == null) return;
                foreach (Sprite sprite in sprites)
                {
                    if (sprite == null) continue;
                    if (sprite.texture != null) Destroy(sprite.texture);
                    Destroy(sprite);
                }
                sprites.Clear();
            }
        }

        private static void Stretch(RectTransform rt, Transform parent)
        {
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }
    }
}
