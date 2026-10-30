using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ZamsCollectibles
{
    /// <summary>
    /// Debug: captures cards exactly as the game draws them, for the Workshop page. Each card is
    /// opened in the real card viewer, the screen is captured at the end of the frame and cropped
    /// to the card, and the card's transparent corners are cut back out. The PNGs go to
    /// Pictures\Zams Card Renders, outside the mod folder (which is uploaded to the Workshop).
    /// </summary>
    public static class CardRenders
    {
        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Zams Card Renders");

        // Time for the window to open and its text to fit before the capture.
        private const float SETTLE_SECONDS = 0.4f;

        private static Runner runner;

        public static bool HasArt(CardDef card) =>
            CustomUIManager.Instance?.GetSprite($"{ModEntry.MOD_ID}:{CardViewer.ArtKey(card)}", true) != null;

        /// <summary>
        /// Captures every card that has art, in mint condition; graded captures the rares and
        /// ultras with art sealed in a gem 10 slab instead.
        /// </summary>
        public static void CaptureAll(bool graded)
        {
            if (runner != null) { ModEntry.Instance.Log.Warning("Card renders: already capturing."); return; }
            List<CardDef> cards = CardDatabase.Sets.SelectMany(s => s.cards).Where(HasArt).ToList();
            if (graded) cards = cards.Where(c => c.rarity == "rare" || c.rarity == "ultra").ToList();
            if (cards.Count == 0) { ModEntry.Instance.Log.Warning("Card renders: no cards with art to capture."); return; }

            GameObject host = new GameObject("zams_card_renders");
            UnityEngine.Object.DontDestroyOnLoad(host);
            runner = host.AddComponent<Runner>();
            runner.StartCoroutine(Capture(cards, graded));
        }

        private static IEnumerator Capture(List<CardDef> cards, bool graded)
        {
            System.IO.Directory.CreateDirectory(Folder);
            ModEntry.Instance.Log.Log($"Card renders: capturing {cards.Count} card(s) to {Folder}");
            int saved = 0;
            foreach (CardDef card in cards)
            {
                GameItem item;
                try
                {
                    item = ModHelper.SpawnItem(CardItems.CardId(card));
                    CardItems.SetCondition(item, 10f);   // gem mint: no wear
                    if (graded) CardItems.Grade(item);
                    CardWindow.Body = null;   // set again only if the card face opens (not the plain fallback)
                    CardViewer.Open(item);
                }
                catch (Exception e)
                {
                    ModEntry.Instance.Log.Warning($"Card renders: {card.set.code} {card.number}: {e.Message}");
                    continue;
                }

                yield return new WaitForSecondsRealtime(SETTLE_SECONDS);
                yield return new WaitForEndOfFrame();
                try
                {
                    if (Save(card, graded)) saved++;
                    else ModEntry.Instance.Log.Warning($"Card renders: {card.set.code} {card.number}: the card window wasn't found.");
                }
                catch (Exception e)
                {
                    ModEntry.Instance.Log.Warning($"Card renders: {card.set.code} {card.number}: {e.Message}");
                }
                CardWindow.Close(item);
                item.Destroy();
                yield return null;
            }
            ModEntry.Instance.Log.Log($"Card renders: saved {saved} of {cards.Count} to {Folder}");
            UnityEngine.Object.Destroy(runner.gameObject);
            runner = null;
        }

        // The card's rectangle on screen, cropped from a screenshot, with its corners cut out.
        private static bool Save(CardDef card, bool graded)
        {
            RectTransform body = CardWindow.Body;
            if (body == null) return false;
            Canvas canvas = body.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector3[] corners = new Vector3[4];
            body.GetWorldCorners(corners);   // bottom-left, top-left, top-right, bottom-right
            Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

            Texture2D screen = ScreenCapture.CaptureScreenshotAsTexture();
            Texture2D shot = null;
            try
            {
                int x = Mathf.Clamp(Mathf.RoundToInt(min.x), 0, screen.width);
                int y = Mathf.Clamp(Mathf.RoundToInt(min.y), 0, screen.height);
                int w = Mathf.Min(Mathf.RoundToInt(max.x), screen.width) - x;
                int h = Mathf.Min(Mathf.RoundToInt(max.y), screen.height) - y;
                if (w <= 0 || h <= 0) return false;

                Color[] px = screen.GetPixels(x, y, w, h);
                CutCorners(body, px, w, h);
                shot = new Texture2D(w, h, TextureFormat.RGBA32, false);
                shot.SetPixels(px);
                shot.Apply();
                string name = $"{card.set.id}_{card.number:00}{(graded ? "_graded" : "")}.png";
                File.WriteAllBytes(Path.Combine(Folder, name), shot.EncodeToPNG());
                return true;
            }
            finally
            {
                UnityEngine.Object.Destroy(screen);
                if (shot != null) UnityEngine.Object.Destroy(shot);
            }
        }

        // Where the card (or slab) sprite is transparent, so is the render: no game scene in the corners.
        private static void CutCorners(RectTransform body, Color[] px, int w, int h)
        {
            Sprite shape = body.GetComponent<Image>()?.sprite;
            if (shape == null) return;
            Rect r = shape.textureRect;
            Texture2D tex = shape.texture;
            try
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int sx = (int)r.x + Mathf.Min((int)r.width - 1, (int)((x + 0.5f) * r.width / w));
                        int sy = (int)r.y + Mathf.Min((int)r.height - 1, (int)((y + 0.5f) * r.height / h));
                        if (tex.GetPixel(sx, sy).a < 0.5f) px[y * w + x] = Color.clear;
                    }
            }
            catch (UnityException) { }   // an unreadable sprite: keep the corners as captured
        }

        private class Runner : MonoBehaviour { }
    }
}
