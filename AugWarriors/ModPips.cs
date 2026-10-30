using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZamsCollectibles.AugWarriors
{
    /// <summary>
    /// The mods installed on an augment, as a row of small pips in the gap below its pocket on
    /// the board: hover one for the mod's tooltip, double-click it to open the card. The row
    /// follows the augment's card and stays centred under it however many mods it holds.
    /// </summary>
    public static class ModPips
    {
        // In card-art pixels: a card is two 16 px cells wide.
        private const float PIP = 6f, GAP = 2f, CARD_PIXELS = 32f;

        private static readonly Dictionary<(int who, Slot slot), Row> rows = new Dictionary<(int, Slot), Row>();
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        /// <summary>Shows the mods on one pocket's augment, under its card (or clears the row).</summary>
        public static void Show(int who, Slot slot, GameGridInventory board, GameItem augment, List<CardDef> mods, System.Func<CardDef, GameItem> makeCard)
        {
            if (!rows.TryGetValue((who, slot), out Row row) || row == null)
            {
                GameObject go = new GameObject($"zams_mod_pips_{who}_{slot}", typeof(RectTransform));
                go.transform.SetParent(board.inner.rectTransform, false);
                row = go.AddComponent<Row>();
                rows[(who, slot)] = row;
            }
            row.anchor = augment;
            row.transform.SetAsLastSibling();
            List<CardDef> shown = augment == null || mods == null ? new List<CardDef>() : mods;
            if (row.mods.SequenceEqual(shown)) return;

            row.Clear();
            row.mods.AddRange(shown);
            foreach (CardDef mod in shown)
            {
                GameObject go = new GameObject("pip", typeof(RectTransform));
                go.transform.SetParent(row.transform, false);
                Image image = go.AddComponent<Image>();
                image.sprite = PipSprite(mod);
                image.raycastTarget = true;
                Pip pip = go.AddComponent<Pip>();
                pip.card = makeCard(mod);
                row.pips.Add(pip);
            }
        }

        /// <summary>Removes every row and the card items behind them (the match is over).</summary>
        public static void ClearAll()
        {
            foreach (Row row in rows.Values) if (row != null) { row.Clear(); Object.Destroy(row.gameObject); }
            rows.Clear();
        }

        // A tiny gem in the mod's rarity colour, outlined in card ink.
        private static Sprite PipSprite(CardDef card)
        {
            string gem = CardDatabase.GetRarity(card.rarity)?.gem ?? "";
            if (sprites.TryGetValue(gem, out Sprite cached) && cached != null) return cached;
            Color32 fill = CardFace.ParseColor(gem, CardFace.Hex("#a8aea5"));
            Color32 light = Color32.Lerp(fill, new Color32(255, 255, 255, 255), 0.45f);
            const int n = (int)PIP;
            Color32[] px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool corner = (x == 0 || x == n - 1) && (y == 0 || y == n - 1);
                    bool edge = x == 0 || y == 0 || x == n - 1 || y == n - 1;
                    px[y * n + x] = corner ? new Color32(0, 0, 0, 0) : edge ? CardFace.Outline
                        : (x == 1 && y == n - 2) || (x == 2 && y == n - 2) || (x == 1 && y == n - 3) ? light : fill;   // glint, top left
                }
            Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply();
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            sprites[gem] = sprite;
            return sprite;
        }

        /// <summary>One pocket's pips. Re-centres under the augment's card every frame, so it follows any change.</summary>
        private class Row : MonoBehaviour
        {
            public GameItem anchor;
            public readonly List<CardDef> mods = new List<CardDef>();
            public readonly List<Pip> pips = new List<Pip>();
            private readonly Vector3[] corners = new Vector3[4];

            public void Clear()
            {
                foreach (Pip pip in pips) if (pip != null) Destroy(pip.gameObject);
                pips.Clear();
                mods.Clear();
            }

            private void LateUpdate()
            {
                RectTransform card = (anchor as GameItemElement)?.rectTransform;
                if (card == null || pips.Count == 0) return;
                // the card's bottom edge, in the board's own units: pips sit centred half a cell below it.
                // The row sits exactly on the board's origin, so its space is the board's (a fresh
                // RectTransform is anchored to the middle of its parent, which put every pip off by half the board).
                RectTransform parent = (RectTransform)transform.parent;
                transform.localPosition = Vector3.zero;
                transform.localScale = Vector3.one;
                transform.localRotation = Quaternion.identity;
                card.GetWorldCorners(corners);
                Vector2 bottomLeft = parent.InverseTransformPoint(corners[0]);
                Vector2 bottomRight = parent.InverseTransformPoint(corners[3]);
                float unit = (bottomRight.x - bottomLeft.x) / CARD_PIXELS;
                if (unit <= 0f) return;
                float size = PIP * unit;
                float step = (PIP + GAP) * unit;
                float width = pips.Count * step - GAP * unit;
                float limit = (CARD_PIXELS + 2 * 16f - 4f) * unit;   // past the card into the gaps beside it, then squeeze
                if (width > limit && pips.Count > 1) { step = (limit - size) / (pips.Count - 1); width = limit; }
                Vector2 centre = new Vector2((bottomLeft.x + bottomRight.x) / 2f, bottomLeft.y - 8f * unit);
                for (int i = 0; i < pips.Count; i++)
                {
                    RectTransform rt = (RectTransform)pips[i].transform;
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(size, size);
                    // anchored to the parent's centre; localPosition puts it in parent space directly
                    rt.localPosition = new Vector3(centre.x - width / 2f + size / 2f + i * step, centre.y, 0f);
                }
            }
        }

        /// <summary>A mod pip: the card's own tooltip on hover, the card viewer on double-click.</summary>
        private class Pip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            public GameItem card;   // a match card item standing for the mod, never placed anywhere
            private RichText tooltip;
            private bool hovered;

            public void OnPointerEnter(PointerEventData eventData) { hovered = true; tooltip = null; }

            public void OnPointerExit(PointerEventData eventData)
            {
                hovered = false;
                OverlayHandler.current?.HideTooltip();
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount == 2 && card != null)
                    CardViewer.Open(card);
            }

            // The game hides the tooltip in Update when nothing under the mouse has one; this
            // runs after it, so the pip's tooltip shows.
            private void LateUpdate()
            {
                if (!hovered || card == null || OverlayHandler.current == null || UIDragHandler.isDraggingAnyUI) return;
                if (tooltip == null) tooltip = (card as GameItemElement)?.GetTooltip();
                if (tooltip == null) return;
                OverlayHandler.current.ShowTooltip(Input.mousePosition, new Vector2(32f, 0f), tooltip);
            }

            private void OnDisable()
            {
                if (hovered) OverlayHandler.current?.HideTooltip();
                hovered = false;
            }

            private void OnDestroy()
            {
                if (card != null) card.Destroy();
                card = null;
            }
        }
    }
}
