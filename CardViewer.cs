using System;
using UnityEngine;

namespace ZamsCollectibles
{
    /// <summary>
    /// Opens a card when it is activated (double-click by default). The card is shown as a
    /// worn card face (CardWindow); if that ever fails, for example after a game update changes
    /// the UI, the viewer logs why and falls back to a plain window with the art and text.
    /// One viewer at a time: opening another card replaces it.
    /// </summary>
    public static class CardViewer
    {
        public const string PLACEHOLDER_ART = "art_placeholder";

        // Fallback window. Kept short: the title bar does not wrap.
        private const string FALLBACK_TITLE = "Aug Warriors";
        private const int FALLBACK_WIDTH = 300;
        private const int FALLBACK_HEIGHT = 470;
        private const int ART_WIDTH = 63;
        private const int ART_HEIGHT = 46;
        private const int ZOOM = 3;

        /// <summary>Textures/Items key of a card's art: the card's own "art" field, or art_&lt;set&gt;_&lt;number&gt;.</summary>
        public static string ArtKey(CardDef card) =>
            string.IsNullOrEmpty(card.art) ? $"art_{card.set.id}_{card.number:00}" : card.art;

        public static void Open(GameItem item)
        {
            CardDef card = CardItems.GetCardDef(item);
            if (card == null || CustomUIManager.Instance == null) return;

            string artKey = $"{ModEntry.MOD_ID}:{ArtKey(card)}";
            if (CustomUIManager.Instance.GetSprite(artKey, true) == null)
                artKey = $"{ModEntry.MOD_ID}:{PLACEHOLDER_ART}";

            try
            {
                CardWindow.Open(item, card, artKey);
            }
            catch (Exception e)
            {
                ModEntry.Instance.Log.Warning($"Card viewer: {e.GetType().Name}: {e.Message}. Showing the plain viewer.");
                CustomUIManager.Instance.CloseWindow(CardWindow.WINDOW_ID);
                OpenPlain(item, card, artKey);
            }
        }

        private static void OpenPlain(GameItem item, CardDef card, string artKey)
        {
            RarityDef rarity = CardDatabase.GetRarity(card.rarity);
            float condition = CardItems.GetCondition(item);
            string conditionLine = CardItems.IsGraded(item)
                ? $"Graded {CardCondition.GradeOf(condition):0.0} ({CardCondition.Label(condition)})"
                : $"{CardCondition.Label(condition)}, ungraded";

            CustomUIBuilder builder = CustomUIManager.Window(CardWindow.WINDOW_ID, FALLBACK_TITLE, CustomUIManager.LAYER_PAPER)
                .UseMachineryStyle()
                .SetSize(FALLBACK_WIDTH, FALLBACK_HEIGHT)
                .SetDraggable(true)
                .SetCloseOnEscape(true)
                .BeginColumn(6f, TextAnchor.UpperCenter)
                    .AddHeader(CardItems.CardName(card))
                    .AddImage(artKey, ART_WIDTH * ZOOM, ART_HEIGHT * ZOOM)
                    .AddLabel($"{card.set.name} · {card.set.code} {card.number:000}/{card.set.cards.Length:000}")
                    .AddLabel(rarity?.name ?? card.rarity)
                    .AddLabel(CardItems.CardText(card));
            if (!string.IsNullOrEmpty(card.flavor))
                builder.AddLabel($"<i>{card.flavor}</i>");
            builder
                    .AddLabel(conditionLine)
                .End()
                .Center()
                .Show();
        }
    }
}
