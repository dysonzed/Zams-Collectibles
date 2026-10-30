using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HarmonyLib;

namespace ZamsCollectibles
{
    /// <summary>
    /// Entry point of the mod. The loader finds the one class implementing IMod, creates it,
    /// applies the [HarmonyPatch] classes of this assembly, then calls Init and OnEnable.
    /// All of that happens in the main menu: no save is loaded yet, so do not touch game
    /// state here. Subscribe to ModHook events instead (see ModdingGuide.md, "Hook Points").
    /// </summary>
    public class ModEntry : IMod
    {
        public const string MOD_ID = "zams-collectibles";

        public static ModEntry Instance { get; private set; }
        public ModManifest Manifest { get; private set; }
        public ModLog Log { get; private set; }

        public void Init(ModManifest manifest)
        {
            Instance = this;
            Manifest = manifest;
            Log = new ModLog(manifest);
            Log.Log($"{manifest.Name} v{manifest.ModVersion} loaded.");
        }

        public void OnEnable()
        {
            ModHook.OnModItemDirectoryInit += CardItems.Register;
            ModHook.OnCreateTooltipLate += OnCreateTooltip;
            ModHook.OnCreateDevTooltipLate += OnCreateDevTooltip;
            ModHook.OnGenerateCustomerLate += Collectibles.OnGenerateCustomer;
            ModHook.OnGenerateCustomerNormal += Progression.OnGenerateCustomer;
            ModHook.OnPlaceInventorInventoryItemLate += Progression.OnInventorStock;
            Progression.Register();
            ModHook.OnGameLoadedInit += Progression.OnRunStarted;
            ModHook.OnNewGameStarted += Progression.OnRunStarted;
            ModHook.OnGameLoadedLate += OnGameLoaded;
            DownsideDen.Register();
            RegisterDebugButtons();
            Log.Log("Enabled.");
        }

        public void OnDisable()
        {
            ModHook.OnModItemDirectoryInit -= CardItems.Register;
            ModHook.OnCreateTooltipLate -= OnCreateTooltip;
            ModHook.OnCreateDevTooltipLate -= OnCreateDevTooltip;
            ModHook.OnGenerateCustomerLate -= Collectibles.OnGenerateCustomer;
            ModHook.OnGenerateCustomerNormal -= Progression.OnGenerateCustomer;
            ModHook.OnPlaceInventorInventoryItemLate -= Progression.OnInventorStock;
            ModHook.OnGameLoadedInit -= Progression.OnRunStarted;
            ModHook.OnNewGameStarted -= Progression.OnRunStarted;
            ModHelper.RemoveNetworkUpgrades(MOD_ID);
            ModHook.OnGameLoadedLate -= OnGameLoaded;
            ModHelper.RemoveAfterhourLocations(MOD_ID);
            ModHelper.RemoveDebugFunctions(MOD_ID);
            Log.Log("Disabled.");
        }

        private void OnCreateTooltip(RichTextBuilder builder, GameItem item)
        {
            if (DeckOrders.IsOrderBox(item)) { DeckOrders.Tooltip(builder, item); return; }

            if (item.IsTag(CardItems.BUNDLE_TAG))
            {
                float avg = item.GetTagReadonly(CardItems.BUNDLE_AVG_CONDITION_FLOAT).GetFloat();
                if (avg > 0f) builder.AddLine(Loc.Get("tooltip_sealed_condition", CardCondition.Label(avg)));
                return;
            }

            CardSetDef boosterSet = CardItems.GetBoosterSet(item);
            if (boosterSet != null)
            {
                builder.AddLine(Loc.Get("tooltip_booster_set", Loc.SetName(boosterSet), boosterSet.code, boosterSet.cards.Length), bold: true);
                builder.AddLine(item.identifier == CardItems.BoosterBoxId(boosterSet)
                    ? Loc.Get("boosterbox_contents", CardItems.BoxPacks(boosterSet))
                    : Loc.Get("tooltip_booster_contents", CardItems.PackContents(boosterSet)));
                return;
            }

            CardDef card = CardItems.GetCardDef(item);
            if (card == null) return;

            RarityDef rarity = CardDatabase.GetRarity(card.rarity);
            builder.AddLine(Loc.Get("tooltip_card_set", Loc.SetName(card.set), card.set.code, card.number.ToString("000"), card.set.cards.Length.ToString("000")));
            builder.AddLine(Loc.RarityName(card.rarity), color: ParseColor(rarity?.color), bold: true);
            if (AugWarriors.AugBoard.IsMatchCard(item)) return;   // a card in play: its condition doesn't matter

            float condition = CardItems.GetCondition(item);
            if (CardItems.IsGraded(item))
                builder.AddLine(Loc.Get("tooltip_graded", CardCondition.GradeOf(condition).ToString("0.0"), CardCondition.Label(condition)), color: RenderHandler.ColorPalette.Yellow);
            else
                builder.AddLine(Loc.Get("tooltip_ungraded", CardCondition.Label(condition)));
        }

        // Item names and descriptions are saved with the items, in the language they were made in:
        // re-apply ours in the current language whenever a save loads.
        private void OnGameLoaded()
        {
            EmporiumEntry store = EmporiumEntry.Instance;
            if (store == null) return;
            int refreshed = 0;
            foreach (GraphNodeStorage root in new GraphNodeStorage[] { store.invElement, store.frontInvinvElement, store.backInvinvElement, store.showcaseElement, store.afterhourInventory })
            {
                if (root == null) continue;
                foreach (GameItem item in GraphUtils.FindAllChildrenType<GameItem>(root))
                    if (CardItems.RefreshText(item)) refreshed++;
            }
            if (refreshed > 0) Log.Log($"Refreshed the text of {refreshed} item(s).");
            ZamcoGuide.GiveIfMissed();
        }

        // The F9 dev tooltip shows the hidden exact condition, for testing.
        private void OnCreateDevTooltip(RichTextBuilder builder, GameItem item)
        {
            if (CardItems.IsCard(item))
                builder.AddLine($"[dev] exact condition {CardItems.GetCondition(item):0.00}");
        }

        // One F9 button that opens our own debug window, so the shared menu stays uncluttered.
        private const string DEBUG_WINDOW_ID = MOD_ID + ".debug";


        private void RegisterDebugButtons()
        {
            ModHelper.AddDebugFunction(MOD_ID, ModHelper.DEBUG_CATEGORY_SPECIAL, "Zam's Collectibles", OpenDebugWindow);
        }

        private void OpenDebugWindow()
        {
            if (CustomUIManager.Instance == null) return;
            CustomUIManager.Window(DEBUG_WINDOW_ID, "Zam's Collectibles", CustomUIManager.LAYER_OVERLAY)
                .SetDraggable(true)
                .BeginColumn(4f)
                    .AddHeader("Spawn")
                    .AddButton("Booster Packs (one per set)", () =>
                    {
                        foreach (CardSetDef set in CardDatabase.Sets)
                            ModHelper.MoveItemToInventory(ModHelper.SpawnItem(CardItems.BoosterId(set)));
                    })
                    .AddButton("Booster Boxes (one per set)", () =>
                    {
                        foreach (CardSetDef set in CardDatabase.Sets)
                            ModHelper.MoveItemToInventory(ModHelper.SpawnItem(CardItems.BoosterBoxId(set)));
                    })
                    .AddButton("Collector's Box", () =>
                        ModHelper.MoveItemToInventory(ModHelper.SpawnItem(CardItems.COLLECTOR_BOX_ID)))
                    .AddButton("Collection Binder", () =>
                        ModHelper.MoveItemToInventory(ModHelper.SpawnItem(CardBinder.BINDER_ID)))
                    .AddButton("Zamco Stockist Guide", () =>
                        ModHelper.MoveItemToInventory(ModHelper.SpawnPaperItem(MOD_ID, ZamcoGuide.BOOK_ID)))
                    .AddButton("Deck Box", () =>
                        ModHelper.MoveItemToInventory(ModHelper.SpawnItem(AugWarriors.DeckBox.DECKBOX_ID)))
                    .AddButton("Test Match (random deck, no stakes)", () =>
                        AugWarriors.AugBoard.Start(null, "Rook", AugWarriors.AugPersonality.Cautious, 0))
                    .AddButton("Full Card Set (first set)", () =>
                    {
                        CardSetDef set = CardDatabase.Sets.Count > 0 ? CardDatabase.Sets[0] : null;
                        if (set == null) return;
                        foreach (CardDef card in set.cards)
                            ModHelper.MoveItemToInventory(ModHelper.SpawnItem(CardItems.CardId(card)));
                    })
                    .AddHeader("Command")
                    // Stand-in for a real grading service: grades every card in the player's inventory.
                    .AddButton("Unlock Downside Den", () => { ModHelper.GetNetworkUpgrade(Progression.DEN_UPGRADE)?.Unlock(); Log.Log("Downside Den unlocked."); })
                    .AddButton("Next Customer: Deck Builder", () => NextCustomer(DeckOrders.CreateCustomer()))
                    .AddButton("Next Customer: Pack Flipper", () => NextCustomer(Progression.CreatePackFlipper()))
                    .AddButton("Next Customer: Pack Flipper (asks for a game)", () => NextCustomer(Progression.CreatePackFlipper(100)))
                    .AddButton("Grade Inventory Cards", () =>
                    {
                        int graded = 0;
                        foreach (GameItem item in GraphUtils.FindChildrenType<GameItem>(EmporiumEntry.Instance.invElement, CardItems.IsCard))
                            if (CardItems.Grade(item)) graded++;
                        Log.Log($"Graded {graded} card(s).");
                    })
                    .AddButton("Close", () => CustomUIManager.Instance.CloseWindow(DEBUG_WINDOW_ID))
                .End()
                .Center()
                .Show();
        }

        // Puts a customer at the front of the queue: they are the next to walk in.
        private void NextCustomer(StoreClient client)
        {
            StoreClientManager manager = PlayerStore.Instance?.storeClientManager;
            if (manager == null) return;
            ModHelper.SetClientSource(client, MOD_ID);
            manager.clientStack.Insert(0, client);
            Log.Log($"Next customer: {client.displayName}.");
        }

        private static RenderHandler.ColorPalette ParseColor(string name) =>
            Enum.TryParse(name, out RenderHandler.ColorPalette color) ? color : RenderHandler.ColorPalette.None;
    }
}
