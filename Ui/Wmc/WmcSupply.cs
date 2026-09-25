using System.Collections.Generic;
using System.Globalization;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>SUPPLY (spec WMC rebuild §SUPPLY), the 0.9 shop rebuilt on R4a's rules: INBOUND and ADOPT show only when they
    /// have something to say, then the numbered steps 1 PILOT &amp; CREW · 2 AIRFRAME (with the HANGAR store) · 3 FIT &amp; FUEL ·
    /// 4 LAUNCH BASE in one scroll viewport, and a DISPATCH card pinned to the floor with its blocker always in view.
    /// REQUISITION sends the card as a Call order (airframe, field, pilot, fit, fuel); the executor quotes again on the same
    /// rules and answers in the same words. A client looks; the host requisitions.</summary>
    internal sealed partial class WmcSupply : IWmcPage
    {
        /// <summary>A client sees every control disabled with this one reason.</summary>
        private static readonly string ClientWhy = ShopRules.WingReason(WingBlock.Client, default);

        private readonly Dictionary<string, AvButton> ids;
        private RectTransform page, steps, inboundRoot, adoptRoot;
        private Rect body;
        private float width;
        private WmcScroll scroll;
        private int layoutKey = -1;

        // This refresh's snapshot: Metrics takes it first, Refresh reuses it.
        private WmcContext last;
        private Aircraft caller;
        private FactionHQ hq;
        private bool client;
        private ShopWing wing;
        private ShopAirframe air;
        private ShopQuote quote;
        private AircraftDefinition selected;
        private Airbase field;
        private int snapFrame = -1;

        private string alert, hint;
        private int hintKey = int.MinValue, metricKey = int.MinValue, metricGeneration = -1;
        private readonly List<string> hangarCodes = new List<string>(4);

        public WmcSupply(Dictionary<string, AvButton> controls) => ids = controls;

        public string Hint => hint;

        public string Alert => alert;

        public void Build(RectTransform pageRoot, Rect shellBody)
        {
            page = pageRoot;
            body = WmcUi.Page(page, shellBody, shellBody.height);
            width = body.width;
            float view = BezelLayout.SupplyView(shellBody.height);
            scroll = WmcScroll.Build(page, new Rect(body.x, body.y, width + 8f, view), "SupplyScroll");
            RectTransform s = scroll.Content;
            inboundRoot = Container(s, "SupplyInbound", BezelLayout.InboundBlock(BezelLayout.InboundMax));
            adoptRoot = Container(s, "SupplyAdopt", BezelLayout.AdoptBand);
            steps = Container(s, "SupplySteps", BezelLayout.SupplySteps);
            BuildInbound(inboundRoot);
            BuildAdopt(adoptRoot);
            BuildPilot(steps, 0f);
            BuildAirframe(steps, -BezelLayout.PilotStep);
            BuildFit(steps, -(BezelLayout.PilotStep + BezelLayout.AirframeStep));
            BuildBase(steps, -(BezelLayout.PilotStep + BezelLayout.AirframeStep + BezelLayout.FitStep));
            BuildPin(body.y - view);
            // Popups hang off the page root, never inside the scroll viewport (F6).
            fitPopup = new AvKit.Popup(page, shellBody.width);
            Layout(0, false);
        }

        private RectTransform Container(RectTransform parent, string name, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            AvKit.Place(rt, new Rect(0f, 0f, width, h));
            return rt;
        }

        /// <summary>INBOUND and ADOPT rows only lengthen the scroll: the blocks move, the reader's place is clamped.</summary>
        private void Layout(int inbound, bool adopt)
        {
            int key = BezelLayout.InboundRows(inbound) * 2 + (adopt ? 1 : 0);
            if (key == layoutKey) return;
            layoutKey = key;
            float y = 0f, block = BezelLayout.InboundBlock(inbound);
            inboundRoot.gameObject.SetActive(block > 0f);
            AvKit.Place(inboundRoot, new Rect(0f, y, width, Mathf.Max(1f, block)));
            y -= block;
            adoptRoot.gameObject.SetActive(adopt);
            AvKit.Place(adoptRoot, new Rect(0f, y, width, BezelLayout.AdoptBand));
            y -= BezelLayout.AdoptBlock(adopt);
            AvKit.Place(steps, new Rect(0f, y, width, BezelLayout.SupplySteps));
            scroll.SetContentHeight(BezelLayout.SupplyContent(inbound, adopt));
        }

        /// <summary>The wing, the selected airframe's quote and its launch field, once a refresh (the lists refill at most once
        /// a second, or at once when the page is shown).</summary>
        private void Snapshot(WmcContext c, bool force)
        {
            if (!force && snapFrame == Time.frameCount && ReferenceEquals(last, c)) return;
            snapFrame = Time.frameCount;
            last = c;
            client = c.Client;
            WingService w = c.Wing;
            // The player requisitions; without a player aircraft (automation) the anchor stands in, as the order does.
            caller = w == null || client ? null : w.Player != null ? w.Player : w.Leader;
            if (caller == null)
            {
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (hud != null && hud.aircraft != null) caller = hud.aircraft;
            }
            hq = caller != null ? caller.NetworkHQ : null;
            WingRequisition.RefreshLists(caller, force);
            wing = WingRequisition.Wing(caller);
            if (client) wing.Host = false;
            selected = WingRequisition.Selected;
            air = selected != null ? WingRequisition.For(selected, hq) : default;
            quote = selected != null ? ShopRules.Quote(wing, air) : default;
            field = selected != null && !client ? WingRequisition.PickField(caller, selected) : null;
        }

        public void Refresh(WmcContext c)
        {
            Snapshot(c, false);
            RefreshInbound();
            RefreshAdopt(c);
            Layout(inboundCount, adoptVisible);
            RefreshPilot();
            RefreshAirframe();
            RefreshHangar();
            RefreshFit();
            RefreshBase();
            RefreshPin();
            int k = (client ? 1 : 0) + inboundCount * 2;
            if (k == hintKey) return;
            hintKey = k;
            hint = SupplyWords.Hint(client, inboundCount);
        }

        /// <summary>FUNDS · HANGAR · STOCK (spec WMC rebuild §bezel shell), rebuilt only when their inputs change.</summary>
        public void Metrics(WmcContext c, WmcMetricRow m)
        {
            bool shown = m.Generation != metricGeneration;
            Snapshot(c, shown);
            if (shown) OnShown();
            float funds = wing.Funds, price = selected != null ? quote.Price : 0f;
            int stock = air.FactionStock + air.Held, held = WingSupplyReserve.Count, cap = WingSupplyReserve.Capacity;
            bool offline = client || !WingSupplyReserve.HasFaction;
            int key;
            unchecked
            {
                key = (int)funds * 31 + (int)(price * 10f) * 17 + stock * 101 + held * 7 + cap * 3 + (offline ? 1 : 0) + (client ? 5 : 0)
                    + (wing.Sandbox ? 11 : 0) + (selected != null ? selected.GetHashCode() : 0);
            }
            if (key == metricKey && !shown) return;
            metricKey = key;
            metricGeneration = m.Generation;
            bool afford = price <= 0f || funds >= price;
            m.Set(0, Credits.Short(funds), SupplyWords.FundsCaption(client, wing.Sandbox, selected != null, price),
                afford ? 1f : Mathf.Clamp01(funds / price), afford ? AvTheme.Friendly : AvTheme.Warning);
            hangarCodes.Clear();
            foreach (AircraftDefinition d in WingSupplyReserve.Stored)
                if (d != null) hangarCodes.Add(SupplyWords.Code(d.code, d.unitName));
            m.Set(1, HangarWords.Value(held, cap, offline), HangarWords.Caption(hangarCodes, WingSupplyReserve.IsHost && !client,
                WingSupplyReserve.HasFaction), HangarWords.Level(held, cap), HangarWords.Full(held, cap) ? AvTheme.Warning : AvTheme.Friendly);
            bool counted = selected != null && !wing.Sandbox;
            m.Set(2, selected == null ? WmcText.Unknown : stock.ToString(CultureInfo.InvariantCulture),
                selected != null && wing.Sandbox ? SupplyWords.SandboxStock
                : SupplyWords.StockCaption(selected != null ? SupplyWords.Code(selected.code, selected.unitName) : null, stock, selected != null),
                !counted || stock > 0 ? 1f : 0f, !counted || stock > 0 ? AvTheme.Friendly : AvTheme.Warning);
        }

        /// <summary>SUPPLY came into view: the lists refill now, and a fit deleted on LOADOUT meanwhile goes back to AUTO.</summary>
        private void OnShown()
        {
            string fit = WingRequisition.FitOf(selected);
            if (fit != null && fit != CallSpec.YourLoadout && !WingLoadoutTemplates.Exists(fit)) WingRequisition.SetFit(selected, null);
            fitSet = false;
        }

        // ---------------------------------------------------------------- the pinned DISPATCH card

        private TMP_Text stateText, titleText, lineText, blockerText;
        private Image stateRail, cardRail, cardIcon;
        private AvButton requisition;
        private int pinKey = int.MinValue;
        private bool pinSet;
        private AircraftDefinition pinSelected;
        private WingPilot pinPilot;
        private Airbase pinField;
        private string pinFit;
        private const float RequisitionQuiet = 0.6f;
        private float requisitionQuiet;

        private void BuildPin(float top)
        {
            float x = body.x, y = top - BezelLayout.PinGap;
            AvStyled.Box(page, new Rect(x, y, width, BezelLayout.PinCard), "card");
            cardRail = AvStyled.Rail(page, new Rect(x, y, 3f, BezelLayout.PinCard), "inert");
            var state = new Rect(x + 6f, y - 4f, 84f, 16f);
            AvStyled.Box(page, state, "chip");
            stateRail = AvStyled.Rail(page, new Rect(state.x, state.y, 3f, state.height), "inert");
            stateText = WmcKit.Text(page, new Rect(state.x + 7f, state.y, state.width - 9f, state.height), "row-sub");
            titleText = WmcKit.Text(page, new Rect(x + 96f, y - 3f, width - 124f, 18f), "row-name");
            cardIcon = AvKit.Panel(page, new Rect(x + width - 22f, y - 4f, 16f, 16f), Color.white);
            cardIcon.preserveAspect = true;
            cardIcon.raycastTarget = false;
            cardIcon.enabled = false;
            lineText = WmcKit.Text(page, new Rect(x + 6f, y - 22f, width - 12f, 12f), "row-sub");
            blockerText = WmcKit.Text(page, new Rect(x + 6f, y - 35f, width - 12f, 12f), "row-sub");
            requisition = AvStyled.Button(page, new Rect(x, y - BezelLayout.PinCard - BezelLayout.PinGap2, width, BezelLayout.RequisitionH),
                "REQUISITION", "btn", Requisition, AvButtonStyle.Primary);
            ids["sup.requisition"] = requisition;
        }

        /// <summary>The card says what REQUISITION would send and, on line 3, why it cannot or what being over the faction's AI
        /// limit does — always in words, always in view.</summary>
        private void RefreshPin()
        {
            WingPilot pilot = client ? null : WingPilotRoster.Upcoming;
            string fit = WingRequisition.FitOf(selected);
            int key;
            unchecked
            {
                key = (int)quote.Wing * 3 + (int)quote.Tile * 29 + (int)(quote.Price * 10f) * 7 + (quote.OverLimit ? 1013 : 0)
                    + WingRequisition.FuelPercent * 131 + inboundCount * 17 + wing.FactionAi * 37 + (int)(wing.FactionAiLimit * 10f) * 41
                    + wing.Members * 43 + wing.Pending * 47 + (int)wing.Mode * 53 + (client ? 59 : 0) + wing.PlayerRank * 61
                    + (wing.Sandbox ? 67 : 0) + (int)ShopRules.WingBlocker(wing) * 71 + (quote.Tile == TileBlock.Funds ? (int)wing.Funds * 73 : 0);
            }
            if (pinSet && key == pinKey && ReferenceEquals(selected, pinSelected) && ReferenceEquals(pilot, pinPilot)
                && ReferenceEquals(field, pinField) && ReferenceEquals(fit, pinFit)) return;
            pinSet = true;
            pinKey = key;
            pinSelected = selected;
            pinPilot = pilot;
            pinField = field;
            pinFit = fit;

            string cls = "info", word = client ? "CLIENT" : ShopRules.State(quote, selected != null, inboundCount, out cls);
            WmcKit.Set(stateText, word);
            WmcUi.SetRail(stateRail, cls);
            WmcUi.SetRail(cardRail, cls);
            string blocker;
            if (selected == null)
            {
                WmcKit.Set(titleText, "NO AIRFRAME");
                WmcKit.Set(lineText, "Pick one in step 2");
                cardIcon.enabled = false;
                WingBlock b = client ? WingBlock.Client : ShopRules.WingBlocker(wing);
                blocker = b != WingBlock.None ? "BLOCKED · " + ShopRules.WingReason(b, wing) : null;
                WmcKit.Set(blockerText, blocker ?? "Pick a pilot, an airframe, its fit and a base");
            }
            else
            {
                WmcKit.Set(titleText, selected.unitName + " · " + Credits.Price(quote.Price));
                cardIcon.sprite = IconFactory.Aircraft(selected);
                cardIcon.enabled = cardIcon.sprite != null;
                string fitWord = SupplyWords.Fit(fit, fit != null && fit != CallSpec.YourLoadout ? WingLoadoutTemplates.NameOf(fit) : null);
                WmcKit.Set(lineText, ShopRules.DispatchLine(pilot != null ? WmcText.Cut(pilot.Callsign, PilotPick.CallsignChars) : null, fitWord,
                    WingRequisition.FuelPercent, field != null ? BaseName.Short(WingRequisition.NameOf(field)) : null));
                blocker = ShopRules.Blocker(quote, wing, air);
                WmcKit.Set(blockerText, blocker ?? (quote.OverLimit ? ShopRules.OverLimitNote(wing) : "READY · press REQUISITION"));
            }
            blockerText.color = blocker != null ? WmcUi.LevelColor("warn") : selected != null && quote.OverLimit ? AvTheme.RailInfo
                : selected != null ? WmcUi.LevelColor("ok") : AvTheme.Dim;
            alert = selected != null && !client ? blocker : null;

            bool go = !client && selected != null && quote.Allowed;
            requisition.SetText(selected != null ? SupplyWords.Requisition(quote.Price) : "REQUISITION");
            requisition.SetEnabled(go);
            requisition.WithTooltip(client ? ClientWhy : selected == null ? "Pick an airframe first."
                : blocker ?? "Send the requisition: it is checked again as it goes, and answered in the same words.");
        }

        /// <summary>What the card shows is what the order carries (review focus 1): its airframe, field, pilot, fit and fuel. A
        /// double click sends one requisition (the second press inside <see cref="RequisitionQuiet"/> is ignored); a deliberate
        /// second press sends the next pilot the card shows.</summary>
        private void Requisition()
        {
            if (last == null || Time.unscaledTime < requisitionQuiet) return;
            WmcUi.Order(last, () =>
            {
                AircraftDefinition d = WingRequisition.Selected;
                if (d == null) return;
                if (!ReferenceEquals(pinSelected, d)) WmcPanel.Instance?.Refresh();
                OrderResult sent = WingOrders.Run(new WingOrder
                {
                    Kind = OrderKind.Call, Number = 1,
                    Call = new CallSpec
                    {
                        Airframe = d.jsonKey, Field = pinField != null ? WingRequisition.KeyOf(pinField) : null,
                        Pilot = pinPilot != null ? pinPilot.Callsign : null, Fit = pinFit, Fuel = WingRequisition.FuelPercent / 100f,
                    },
                });
                if (sent.Accepted) requisitionQuiet = Time.unscaledTime + RequisitionQuiet;
                WmcPanel.Instance?.Refresh();
            });
        }
    }
}
