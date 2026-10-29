using System;
using TMPro;
using UnityEngine;
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
        public const string WINDOW_ID = ModEntry.MOD_ID + ".card_viewer";

        private static readonly Color Ink = new Color32(0x29, 0x22, 0x34, 0xFF);
        private static readonly Color FooterInk = new Color32(0xE8, 0xE4, 0xD6, 0xFF);
        private static readonly Color CostInk = new Color32(0xF4, 0xF1, 0xE4, 0xFF);

        public static void Open(GameItem item, CardDef card, string artKey)
        {
            if (!CardFace.TryBuild(item, card, artKey, out Sprite cardSprite, out Sprite wearSprite))
                throw new InvalidOperationException($"card face could not be drawn ({artKey})");

            GameObject body = BuildBody(cardSprite, wearSprite);
            CustomUIBuilder builder;
            try
            {
                builder = CustomUIManager.Instance.CreateWindow(WINDOW_ID, null, CustomUIManager.LAYER_PAPER, body);
            }
            finally
            {
                UnityEngine.Object.Destroy(body);   // the window made its own copy
            }

            string rules = CardItems.RulesText(card);
            if (!string.IsNullOrEmpty(card.flavor))
                rules = (string.IsNullOrEmpty(rules) ? "" : rules + "\n") + $"<i><color=#5f5a66>{card.flavor}</color></i>";

            CustomUIWindow window = builder
                .SetDraggable(true)
                .SetCloseOnEscape(true)
                .AddLabel(card.name, "name")
                .AddLabel(card.cost.ToString(), "cost")
                .AddLabel(CardItems.TypeLine(card), "type")
                .AddLabel(rules, "rules")
                .AddLabel($"{card.set.code} {card.number:000}/{card.set.cards.Length:000}", "footer")
                .Center()
                .Show();

            Place(window, "name", CardLayout.NameText, 18f, TextAlignmentOptions.MidlineLeft, Ink, FontStyles.Bold);
            Place(window, "cost", CardLayout.CostText, 16f, TextAlignmentOptions.Center, CostInk, FontStyles.Bold);
            Place(window, "type", CardLayout.TypeText, 12f, TextAlignmentOptions.MidlineLeft, Ink, FontStyles.Normal);
            Place(window, "rules", CardLayout.RulesText, 13f, TextAlignmentOptions.TopLeft, Ink, FontStyles.Normal);
            Place(window, "footer", CardLayout.FooterText, 10f, TextAlignmentOptions.MidlineLeft, FooterInk, FontStyles.Normal);
        }

        // ------------------------------------------------------------ window body

        private static GameObject BuildBody(Sprite cardSprite, Sprite wearSprite)
        {
            int z = CardLayout.ZOOM;
            GameObject root = new GameObject("zams_card", typeof(RectTransform));
            root.SetActive(false);   // the copy stays hidden until the builder shows it
            ((RectTransform)root.transform).sizeDelta = new Vector2(CardLayout.WIDTH * z, CardLayout.HEIGHT * z);

            Image face = root.AddComponent<Image>();
            face.sprite = cardSprite;
            face.raycastTarget = true;

            // Clicking the card closes it (a drag does not count as a click).
            Button close = root.AddComponent<Button>();
            close.transition = Selectable.Transition.None;

            // Builder content lands here first; Place moves each label onto its slot.
            RectTransform content = Child(root, "content", new RectInt(0, 0, CardLayout.WIDTH, CardLayout.HEIGHT));
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            CustomUIWindowFrame frame = root.AddComponent<CustomUIWindowFrame>();
            frame.contentRoot = content;
            frame.closeButton = close;

            Child(root, "name", CardLayout.NameText);
            Child(root, "cost", CardLayout.CostText);
            Child(root, "type", CardLayout.TypeText);
            Child(root, "rules", CardLayout.RulesText);
            Child(root, "footer", CardLayout.FooterText);

            Image wear = Child(root, "wear", new RectInt(0, 0, CardLayout.WIDTH, CardLayout.HEIGHT)).gameObject.AddComponent<Image>();
            wear.sprite = wearSprite;
            wear.raycastTarget = false;
            return root;
        }

        // A child rectangle placed in card pixels from the top-left corner.
        private static RectTransform Child(GameObject parent, string name, RectInt r)
        {
            int z = CardLayout.ZOOM;
            RectTransform rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(r.x * z, -r.y * z);
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
