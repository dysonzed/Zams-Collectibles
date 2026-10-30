using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace ZamsCollectibles
{
    /// <summary>
    /// The card grader: a customer drawn by the dealer certificate, on the same odds as the other
    /// card customers (its own count of missed days). They offer to grade cards for a fee per card;
    /// yes opens their grading case, with room for a few cards. Submitted cards go with them, and
    /// the next day they're the first customer in, handing the cards back sealed in graded slabs.
    /// Cards in their hands are kept as saved data (the card and its condition), so a save in
    /// between keeps them.
    /// </summary>
    public static class CardGrading
    {
        private const int FEE_PER_CARD = 12;
        private const int MIN_SLOTS = 3, MAX_SLOTS = 8;

        private const string PENDING_KEY = "grading_pending";   // JSON: the cards out for grading
        private const string MISSED_KEY = "grader_missed";
        private const string DAY_KEY = "grader_day";

        private class CardOut
        {
            public string id;
            public float condition;
        }

        private class Pending
        {
            public int returnDay;
            public List<CardOut> cards = new List<CardOut>();
        }

        private static Pending Load()
        {
            try { return JsonConvert.DeserializeObject<Pending>(ModHelper.GetModData(ModEntry.MOD_ID, PENDING_KEY, "") ?? ""); }
            catch (JsonException) { return null; }
        }

        private static void Save(Pending pending) =>
            ModHelper.SetModData(ModEntry.MOD_ID, PENDING_KEY, pending == null ? "" : JsonConvert.SerializeObject(pending));

        // ------------------------------------------------------------ visits

        /// <summary>Once a day: maybe the grader, by the certificate's odds (not while they have cards).</summary>
        public static void OnGenerateCustomer(StoreClientManager manager)
        {
            if (CardDatabase.Sets.Count == 0 || Load() != null) return;
            int day = StoreStation.GetDayCounter();
            if (ModHelper.GetModDataInt(ModEntry.MOD_ID, DAY_KEY, -1) == day) return;
            ModHelper.SetModDataInt(ModEntry.MOD_ID, DAY_KEY, day);

            int missed = ModHelper.GetModDataInt(ModEntry.MOD_ID, MISSED_KEY, 0);
            if (RNG.Roll(DealerCertificate.ChanceFor(missed)))
            {
                manager.AddClient(CreateGrader());
                ModHelper.SetModDataInt(ModEntry.MOD_ID, MISSED_KEY, 0);
            }
            else if (DealerCertificate.IsDisplayed())
            {
                ModHelper.SetModDataInt(ModEntry.MOD_ID, MISSED_KEY, missed + 1);
            }
        }

        /// <summary>The day after a submission, the grader is the first customer in.</summary>
        public static void OnGenerateCustomerLast(StoreClientManager manager)
        {
            Pending pending = Load();
            if (pending == null || StoreStation.GetDayCounter() < pending.returnDay) return;
            StoreClient client = CreateReturn();
            ModHelper.SetClientSource(client, ModEntry.MOD_ID);
            manager.clientStack.Insert(0, client);
        }

        private static StoreClient NewClient(string id)
        {
            StoreClient client = new StoreClient();
            client.identifier = ModEntry.MOD_ID + id;
            client.displayName = Loc.Get("client_grader");
            StoreClientFactionSetup.InitLowerLevelCitizen(client);
            client.spriteName = SpriteDict.GetRandomScavSpriteName();
            client.clientIntent = StoreClient.ClientIntent.UNDEFINED;   // no trade: just talk and the grading case
            return client;
        }

        public static StoreClient CreateGrader()
        {
            StoreClient client = NewClient(".grader");
            int slots = RNG.GetRandomInt(MIN_SLOTS, MAX_SLOTS);
            // A choice's dialogue is the player's answer; the line after it is the grader's reply.
            Dialogue yes = new Dialogue().SetText("PLAYER", Loc.Get("grader_choice_yes"));
            yes.NextDialogue().SetText(client.displayName, Loc.Get("grader_accepted", slots))
                .SetEndAction(() => OpenCase(client, slots));
            Dialogue no = new Dialogue().SetText("PLAYER", Loc.Get("grader_choice_no"));
            no.NextDialogue().SetText(client.displayName, Loc.Get("grader_declined")).SetEndAction(() => Leave(client));
            client.mainDialogue.SetText(client.displayName, Loc.Get("grader_offer", FEE_PER_CARD))
                .AddChoice(DirectoryMaster.Item("yes"), Loc.Get("grader_choice_yes"), "", yes, () => { })
                .AddChoice(DirectoryMaster.Item("cancel"), Loc.Get("grader_choice_no"), "", no, () => { });
            client.CompleteClientCreation();
            return client;
        }

        private static StoreClient CreateReturn()
        {
            StoreClient client = NewClient(".grader_return");
            client.mainDialogue.SetText(client.displayName, Loc.Get("grader_return")).SetEndAction(() =>
            {
                ReturnCards();
                Leave(client);
            });
            client.CompleteClientCreation();
            return client;
        }

        private static void Leave(StoreClient client)
        {
            if (PlayerStore.Instance.currentClientInstance?.GetClientBlueprint() == client)
                PlayerStore.Instance.DismissCurrentClient();
        }

        private static void Say(StoreClient client, string line, bool thenLeave)
        {
            Dialogue d = new Dialogue().SetText(client.displayName, line);
            if (thenLeave) d.SetEndAction(() => Leave(client));
            DialogUIManager.Instance.StartDialogue(d, forcedStart: true);
        }

        // ------------------------------------------------------------ the grading case

        private static PixelWindow window;
        private static GameGridInventory slotsGrid;
        private static MachineryText costText;
        private static MachineryButton submitButton;
        private static StoreClient forClient;
        private static bool submitted;

        private static void OpenCase(StoreClient client, int slots)
        {
            if (window != null && window.handler != null) UnityEngine.Object.Destroy(window.handler.gameObject);
            forClient = client;
            submitted = false;

            slotsGrid = new GameGridInventory(slots, 1).SetMachineryStyle();
            slotsGrid.mayInventoryAddItemFunc = (item, _) =>
                GeneralHelper.IsItemOwned(item) && CardItems.IsCard(item) && !CardItems.IsGraded(item) && !AugWarriors.AugBoard.IsMatchCard(item);
            slotsGrid.onSlotAddItemEndFunc += (_, _, _) => Refresh();
            slotsGrid.onSlotRemoveItemEndFunc += (_, _, _) => Refresh();
            costText = new MachineryText();
            submitButton = new MachineryButton(Loc.Get("grader_submit"), Submit);

            window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.Panel(Loc.Get("grader_case", slots), MachineryStack.Vertical(slotsGrid, costText, submitButton), "grading"));
            window.SetTitle(Loc.Get("grader_title"));
            window.closeCallback += Closed;
            Refresh();
            window.Show(new Vector2(Screen.width * 0.5f, Screen.height * 0.6f), Vector2.zero);
        }

        private static int Cost() => slotsGrid.childItems.Count * FEE_PER_CARD;

        private static void Refresh()
        {
            if (window == null) return;
            int count = slotsGrid.childItems.Count;
            costText.SetText(Loc.Get("grader_cost", count, Cost()));
            if (count > 0 && PlayerStore.Instance.CanPay(Cost())) submitButton.Enable(); else submitButton.Disable();
        }

        private static void Submit()
        {
            List<GameItem> cards = slotsGrid.childItems.ToList();
            if (cards.Count == 0 || !PlayerStore.Instance.CanPay(Cost())) return;
            PlayerStore.Instance.PayMoney(Cost(), playRegisterSound: true);
            Pending pending = new Pending { returnDay = StoreStation.GetDayCounter() + 1 };
            foreach (GameItem card in cards)
            {
                pending.cards.Add(new CardOut { id = card.identifier, condition = CardItems.GetCondition(card) });
                slotsGrid.Expel(card);
                card.Destroy();
            }
            Save(pending);
            submitted = true;
            window.Hide();
            Say(forClient, Loc.Get("grader_submitted", cards.Count), thenLeave: true);
        }

        // Closed with its X: the cards go back to the counter and the grader leaves.
        private static void Closed()
        {
            if (submitted) return;
            foreach (GameItem card in slotsGrid.childItems.ToList())
            {
                slotsGrid.Expel(card);
                GraphUtils.EmporiumTryAdd(EmporiumEntry.Instance.backInvinvElement, card);
            }
            Say(forClient, Loc.Get("grader_declined"), thenLeave: true);
        }

        // Hands the cards back, graded and slabbed, on the counter.
        private static void ReturnCards()
        {
            Pending pending = Load();
            if (pending == null) return;
            foreach (CardOut c in pending.cards)
            {
                GameItem card = DirectoryMaster.Item(c.id);
                if (card == null) continue;
                CardItems.SetCondition(card, c.condition);
                CardItems.Grade(card);
                Progression.PlaceOnCounter(card);
            }
            Save(null);
        }

        // ------------------------------------------------------------ debug

        /// <summary>Debug: the grader comes back right away with whatever they have.</summary>
        public static StoreClient DebugReturnNow()
        {
            Pending pending = Load();
            if (pending == null) return null;
            pending.returnDay = StoreStation.GetDayCounter();
            Save(pending);
            return CreateReturn();
        }
    }
}
