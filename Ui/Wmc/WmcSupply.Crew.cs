using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    // SUPPLY's crew side: INBOUND (launches on their way), ADOPT (friendly AI selected on the map) and step 1 PILOT & CREW.
    internal sealed partial class WmcSupply
    {
        private sealed class InboundView
        {
            public GameObject Root;
            public Image Rail;
            public TMP_Text Text;
            public AircraftDefinition Type;
            public WingPilot Pilot;
            public Airbase Field;
            public int Key = int.MinValue;
        }

        private readonly InboundRow[] inbound = new InboundRow[8];
        private readonly InboundView[] inboundViews = new InboundView[BezelLayout.InboundMax];
        private TMP_Text inboundChip;
        private int inboundCount, inboundChipShown = -1;

        private void BuildInbound(RectTransform r)
        {
            AvStyled.Label(r, new Rect(0f, 0f, width - 130f, BezelLayout.InboundHead), SupplyWords.InboundTitle, "section-title");
            inboundChip = WmcKit.Text(r, new Rect(width - 130f, 0f, 130f, BezelLayout.InboundHead), "row-sub", TextAlignmentOptions.MidlineRight);
            float h = BezelLayout.InboundRow - 2f;
            for (int i = 0; i < inboundViews.Length; i++)
            {
                var go = new GameObject("Inbound" + i, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(r, false);
                AvKit.Place(rt, new Rect(0f, -(BezelLayout.InboundHead + BezelLayout.HeadGap) - i * BezelLayout.InboundRow, width, h));
                AvStyled.Box(rt, new Rect(0f, 0f, width, h), "row");
                inboundViews[i] = new InboundView
                {
                    Root = go, Rail = AvStyled.Rail(rt, new Rect(0f, 0f, 3f, h), "info"), Text = WmcKit.Text(rt, new Rect(10f, 0f, width - 14f, h), "row-sub"),
                };
                go.SetActive(false);
            }
        }

        /// <summary>Launches on their way (queued, spawning, taxiing, departing, joining with an ETA), the last row "+n MORE"
        /// beyond four; a row's text is rebuilt only when its launch, phase or ETA second changes.</summary>
        private void RefreshInbound()
        {
            inboundCount = client || SpawnService.Instance == null ? 0 : SpawnService.Instance.Inbound(inbound);
            if (inboundCount != inboundChipShown)
            {
                inboundChipShown = inboundCount;
                WmcKit.Set(inboundChip, SupplyWords.InboundChip(inboundCount));
            }
            int rows = BezelLayout.InboundRows(inboundCount);
            for (int i = 0; i < inboundViews.Length; i++)
            {
                InboundView v = inboundViews[i];
                bool on = i < rows;
                if (v.Root.activeSelf != on) v.Root.SetActive(on);
                if (!on)
                {
                    v.Key = int.MinValue;
                    continue;
                }
                bool more = i == BezelLayout.InboundMax - 1 && inboundCount > BezelLayout.InboundMax;
                InboundRow row = inbound[i];
                int key = more ? -1 - inboundCount : (int)row.Phase * 100000 + (float.IsNaN(row.Eta) ? 99999 : (int)row.Eta);
                if (key == v.Key && (more || ReferenceEquals(row.Type, v.Type) && ReferenceEquals(row.Pilot, v.Pilot) && ReferenceEquals(row.Field, v.Field)))
                    continue;
                v.Key = key;
                v.Type = row.Type;
                v.Pilot = row.Pilot;
                v.Field = row.Field;
                WmcKit.Set(v.Text, more ? InboundWords.More(inboundCount - (BezelLayout.InboundMax - 1)) : InboundText(row));
                WmcUi.SetRail(v.Rail, more || row.Phase == InboundPhase.Queued || row.Phase == InboundPhase.Spawning ? "inert"
                    : row.Phase == InboundPhase.Joining ? "live" : "info");
            }
        }

        private static string InboundText(in InboundRow r)
        {
            string code = r.Type != null ? SupplyWords.Code(r.Type.code, r.Type.unitName) : WmcText.Unknown;
            string s = InboundWords.Row(code, r.Pilot != null ? WmcText.Cut(r.Pilot.Callsign, PilotPick.CallsignChars) : null, r.Phase, r.Eta);
            return r.Field != null && float.IsNaN(r.Eta) ? s + " · " + BaseName.Short(WingRequisition.NameOf(r.Field)).ToUpperInvariant() : s;
        }

        // ---------------------------------------------------------------- ADOPT

        private AvButton adoptButton;
        private TMP_Text adoptNote;
        private readonly List<Aircraft> recruits = new List<Aircraft>();
        private ConfirmGate adoptGate = new ConfirmGate();
        private float adoptCost;
        private int adoptSkipped, adoptHash = int.MinValue, adoptShown = int.MinValue;
        private string adoptWhy, adoptKey;
        private bool adoptVisible;

        private void BuildAdopt(RectTransform r)
        {
            adoptButton = AvStyled.Button(r, new Rect(0f, 0f, 220f, BezelLayout.AdoptBand), "ADOPT", "btn", Adopt, AvButtonStyle.Primary);
            adoptButton.WithTooltip("Take command of the friendly AI selected on the map: press twice, the cost is shown.");
            ids["sup.adopt"] = adoptButton;
            adoptNote = WmcKit.Text(r, new Rect(226f, 0f, width - 226f, BezelLayout.AdoptBand), "row-sub");
        }

        /// <summary>Friendly AI selected on the map that can join (host only) and what they cost; the faction's others that
        /// cannot, with the first reason. Enemies, our own members and the player are not counted at all.</summary>
        private void CountAdopt(WmcContext c)
        {
            recruits.Clear();
            adoptCost = 0f;
            adoptSkipped = 0;
            adoptWhy = null;
            WingService w = c.Wing;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            Aircraft player = w != null ? w.Player : null;
            if (map == null || w == null || client || player == null || map.selectedIcons == null) return;
            int room = WingService.MaxMembers - w.Members.Count - (SpawnService.Instance != null ? SpawnService.Instance.PendingTotal : 0);
            foreach (MapIcon icon in map.selectedIcons)
            {
                if (!(icon is UnitMapIcon u) || !(u.unit is Aircraft a) || a == player || a.NetworkHQ != player.NetworkHQ || w.IsMember(a)) continue;
                string why;
                if (recruits.Count >= room || recruits.Count >= WingOrder.MaxUnits) why = "the wing is full";
                else if (w.CanRecruit(a, out why))
                {
                    CallQuote q = WingRecruitment.Quote(a);
                    if (q.Allowed)
                    {
                        recruits.Add(a);
                        adoptCost += q.Charge;
                        continue;
                    }
                    why = q.Reason;
                }
                adoptSkipped++;
                if (adoptWhy == null) adoptWhy = why;
            }
        }

        /// <summary>The row shows only when something can join; a changed selection asks again (review focus 3).</summary>
        private void RefreshAdopt(WmcContext c)
        {
            CountAdopt(c);
            adoptVisible = AdoptRow.Visible(recruits.Count, client);
            int hash = recruits.Count;
            unchecked
            {
                foreach (Aircraft a in recruits) hash = hash * 486187739 + (int)a.persistentID.Id;
            }
            if (hash != adoptHash)
            {
                adoptHash = hash;
                adoptGate = new ConfirmGate();
                adoptKey = adoptVisible ? AdoptRow.Key(RecruitIds()) : null;
            }
            if (!adoptVisible) return;
            bool asking = adoptKey != null && adoptGate.IsArmed(adoptKey, Time.unscaledTime);
            int key;
            unchecked
            {
                key = hash * 31 + (int)(adoptCost * 10f) * 7 + adoptSkipped * 131 + (asking ? 1 : 0) + (adoptWhy != null ? adoptWhy.GetHashCode() : 0);
            }
            if (key == adoptShown) return;
            adoptShown = key;
            adoptButton.SetText(AdoptRow.Label(recruits.Count, adoptCost, asking));
            adoptButton.SetLatched(asking);
            WmcKit.Set(adoptNote, AdoptRow.Note(adoptSkipped, adoptWhy));
        }

        private uint[] RecruitIds()
        {
            var units = new uint[recruits.Count];
            for (int i = 0; i < units.Length; i++) units[i] = recruits[i].persistentID.Id;
            return units;
        }

        private void Adopt()
        {
            if (last == null || recruits.Count == 0 || adoptKey == null) return;
            WmcUi.Order(last, () =>
            {
                if (!adoptGate.Press(adoptKey, Time.unscaledTime))
                {
                    WingToast.Show(AdoptRow.Ask(recruits.Count, adoptCost));
                    adoptShown = int.MinValue;
                    WmcPanel.Instance?.Refresh();
                    return;
                }
                if (!WingOrders.Run(new WingOrder { Kind = OrderKind.Recruit, Units = RecruitIds() }).Accepted) return;
                // The adopted leave the game's selection (or the player's target list), as in 0.9.
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                bool flying = hud != null && hud.aircraft != null && !hud.aircraft.disabled;
                WingService w = last.Wing;
                foreach (Aircraft a in recruits)
                {
                    if (a == null || w == null || !w.IsMember(a)) continue;
                    if (flying && hud.GetTargetList().Contains(a)) hud.DeSelectUnit(a);
                    else if (map != null) map.DeselectIcon(a);
                }
                recruits.Clear();
                WmcPanel.Instance?.Refresh();
            });
        }

        // ---------------------------------------------------------------- step 1 PILOT & CREW

        private const string PilotTip = "Choose who flies the next requisition: free pilots only, the most senior first.";
        private TMP_Text pilotState, pilotName, pilotRank, pilotStatus, pilotCounter;
        private Image pilotRail, pilotCardRail;
        private WmcPortrait portrait;
        private AvButton[] pilotStepper;
        private readonly List<WingPilot> free = new List<WingPilot>();
        private int pilotVersion = int.MinValue;
        private bool pilotClient;

        private void BuildPilot(RectTransform r, float y)
        {
            pilotState = WmcKit.StepHeader(r, new Rect(0f, y, width, BezelLayout.StepHead), 1, SupplyWords.PilotTitle, out pilotRail);
            float cy = y - BezelLayout.StepHead - BezelLayout.HeadGap;
            // The card is not a click target (0.9 critique: a dead card click); only the stepper acts.
            Image card = AvStyled.Box(r, new Rect(0f, cy, width, BezelLayout.PilotCard), "row");
            if (card != null) card.raycastTarget = false;
            pilotCardRail = AvStyled.Rail(r, new Rect(0f, cy, 3f, BezelLayout.PilotCard), "inert");
            portrait = WmcPortrait.Build(r, new Rect(8f, cy - 2f, 36f, 44f));
            pilotName = WmcKit.Text(r, new Rect(52f, cy - 2f, 272f, 16f), "row-name");
            pilotRank = WmcKit.Text(r, new Rect(52f, cy - 18f, 272f, 14f), "row-sub");
            pilotStatus = WmcKit.Text(r, new Rect(52f, cy - 32f, 272f, 14f), "row-sub");
            pilotStepper = AvKit.Stepper(r, width - 126f, cy - 9f, 120f, out pilotCounter, () => StepPilot(-1), () => StepPilot(1), PilotTip);
            ids["sup.pilot.prev"] = pilotStepper[0];
            ids["sup.pilot.next"] = pilotStepper[1];
        }

        /// <summary>The pilot the next launch seats (the roster's Upcoming); rebuilt only when the roster's version moves.</summary>
        private void RefreshPilot()
        {
            int v = WingPilotRoster.Version;
            if (v == pilotVersion && client == pilotClient) return;
            pilotVersion = v;
            pilotClient = client;
            if (client)
            {
                WmcKit.Set(pilotName, WmcText.Unknown);
                WmcKit.Set(pilotRank, "THE HOST KEEPS THE ROSTER");
                WmcKit.Set(pilotStatus, "");
                WmcKit.Set(pilotCounter, WmcText.Unknown);
                portrait.Set(null);
                WmcUi.SetRail(pilotCardRail, "inert");
                WmcKit.SetStep(pilotState, pilotRail, WmcText.Unknown, "inert");
                SetStepper(false, ClientWhy);
                return;
            }
            WingPilotRoster.FreePilots(free);
            WingPilot up = WingPilotRoster.Upcoming;
            int i = PilotPick.Index(free.IndexOf(up), free.Count);
            WmcKit.Set(pilotName, PilotPick.NameLine(up?.Callsign, up?.Name));
            WmcKit.Set(pilotRank, PilotPick.RankLine(up != null ? WingPilotRoster.RankName(up.Rank) : null, up != null ? up.Xp : 0));
            WmcKit.Set(pilotStatus, PilotPick.Status(up != null));
            WmcKit.Set(pilotCounter, PilotPick.Counter(i, free.Count));
            portrait.Set(up);
            WmcUi.SetRail(pilotCardRail, up != null ? "info" : "inert");
            WmcKit.SetStep(pilotState, pilotRail, PilotPick.State(free.Count), free.Count > 0 ? "live" : "info");
            SetStepper(free.Count > 1, free.Count == 1 ? "Only one pilot is free." : "Nobody is free: a new pilot is drafted at launch.");
        }

        private void SetStepper(bool on, string why)
        {
            foreach (AvButton b in pilotStepper)
            {
                b.SetEnabled(on);
                b.WithTooltip(on ? PilotTip : why);
            }
        }

        private void StepPilot(int dir)
        {
            if (client) return;
            WingPilotRoster.FreePilots(free);
            if (free.Count <= 1) return;
            int i = PilotPick.Index(free.IndexOf(WingPilotRoster.Upcoming), free.Count);
            WingPilotRoster.Select(free[PilotPick.Step(i, free.Count, dir)]);
            WmcPanel.Instance?.Refresh();
        }
    }
}
