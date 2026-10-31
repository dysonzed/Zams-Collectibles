using System;
using HarmonyLib;
using UnityEngine;

namespace ZamsCollectibles
{
    /// <summary>
    /// Opens a card when it is activated (double-click by default). The card is shown as a
    /// worn card face (CardWindow); if that ever fails, for example after a game update changes
    /// the UI, the viewer logs why and falls back to a plain window with the art and text.
    /// Any number of cards can be open at once; opening one that is already open brings it to the front.
    /// </summary>
    public static class CardViewer
    {
        public const string PLACEHOLDER_ART = "art_placeholder";

        // Fallback window (its title, viewer_title, is kept short: the title bar does not wrap).
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

            if (CardWindow.Focus(item)) return;
            string id = CardWindow.NextId();
            try
            {
                CardWindow.Open(item, card, artKey, id);
            }
            catch (Exception e)
            {
                ModEntry.Instance.Log.Warning($"Card viewer: {e.GetType().Name}: {e.Message}. Showing the plain viewer.");
                CustomUIManager.Instance.CloseWindow(id);
                OpenPlain(item, card, artKey, id);
            }
        }

        private static void OpenPlain(GameItem item, CardDef card, string artKey, string id)
        {
            float condition = CardItems.GetCondition(item);
            string conditionLine = CardItems.IsGraded(item)
                ? Loc.Get("tooltip_graded", CardCondition.GradeOf(condition).ToString("0.0"), CardCondition.Label(condition))
                : "";   // ungraded: the condition is for the player to judge
            string flavor = Loc.CardFlavor(card);

            CustomUIBuilder builder = CustomUIManager.Window(id, Loc.Get("viewer_title"), CustomUIManager.LAYER_PAPER)
                .UseMachineryStyle()
                .SetSize(FALLBACK_WIDTH, FALLBACK_HEIGHT)
                .SetDraggable(true)
                .SetCloseOnEscape(true)
                .BeginColumn(6f, TextAnchor.UpperCenter)
                    .AddHeader(CardItems.CardName(card))
                    .AddImage(artKey, ART_WIDTH * ZOOM, ART_HEIGHT * ZOOM)
                    .AddLabel(Loc.Get("tooltip_card_set", Loc.SetName(card.set), card.set.code, card.number.ToString("000"), card.set.cards.Length.ToString("000")))
                    .AddLabel(CardItems.CardText(card));
            if (!string.IsNullOrEmpty(flavor))
                builder.AddLabel($"<i>{flavor}</i>");
            builder
                    .AddLabel(conditionLine)
                .End()
                .Center();
            CardWindow.Track(item, builder.Show());
        }
    }

    // The game only lets the player activate items they own (documents aside), so singles for
    // sale and the cards on the Aug Warriors board wouldn't open. Reading a card is always allowed;
    // this only affects activation, not moving, buying or dropping them.
    [HarmonyPatch(typeof(GeneralHelper), nameof(GeneralHelper.MayInteractNonOwnedActivate))]
    public static class CardViewerNonOwnedPatch
    {
        static void Postfix(GameItem item, ref bool __result)
        {
            if (CardItems.IsCard(item)) __result = true;
        }
    }
}
