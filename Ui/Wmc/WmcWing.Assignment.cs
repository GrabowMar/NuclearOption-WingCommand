using System.Globalization;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    // WING's pinned AIRFRAME ASSIGNMENT bar: where the dossier's pilot is (the airframe it flies, is queued in or last flew, and a
    // slot line that may wrap), AIR SAR for that pilot (not the nearest) and an affordable LOCAL SAR with its countdown. Both SAR
    // buttons always show, disabled with their reason (0.9 critique §8.6).
    internal sealed partial class WmcWing
    {
        private Image barRail, barIcon;
        private TMP_Text barName, barSlot;
        private AvButton airSar, localSar;
        private readonly ConfirmGate localGate = new ConfirmGate();
        private readonly InboundRow[] inboundRows = new InboundRow[8];
        private int barKey = int.MinValue, iconFor = int.MinValue;
        private string airWhy, localWhy;

        private void BuildAssignment(float top)
        {
            float x = body.x, y = top - BezelLayout.WingPinGap;
            const float h = BezelLayout.AssignBar;
            AvStyled.Box(page, new Rect(x, y, width, h), "card");
            barRail = AvStyled.Rail(page, new Rect(x, y, 3f, h), "inert");
            AvStyled.Label(page, new Rect(x + 8f, y - 3f, 220f, 12f), SquadronWords.AssignTitle, "metric-key");
            barIcon = AvKit.Panel(page, new Rect(x + 8f, y - 20f, 24f, 24f), Color.white);
            barIcon.preserveAspect = true;
            barIcon.raycastTarget = false;
            barIcon.enabled = false;
            barName = WmcKit.Text(page, new Rect(x + 40f, y - 16f, 304f, 14f), "row-name");
            barSlot = AvStyled.Label(page, new Rect(x + 40f, y - 31f, 304f, 24f), "", "row-sub");
            barSlot.raycastTarget = false;
            airSar = AvStyled.Button(page, new Rect(x + 352f, y - 4f, width - 354f, 24f), "AIR SAR", "btn", AirSar);
            ids["wing.airsar"] = airSar;
            localSar = AvStyled.Button(page, new Rect(x + 352f, y - 30f, width - 354f, 24f), "LOCAL SAR", "btn", LocalSar);
            ids["wing.localsar"] = localSar;
        }

        /// <summary>The bar, rebuilt only when what it says changed: the roster, the pilot, its member's duty, its launch's phase, the
        /// helicopter going, the search's second, the ask, the funds, or whether a helicopter can go.</summary>
        private void RefreshAssignment()
        {
            WingPilot p = client ? null : inspected;
            int at = IndexOf(p);
            PilotStatus s = at >= 0 ? status[at] : PilotStatus.Free;
            WingMember m = p != null && s == PilotStatus.Flying ? MemberOf(p) : null;
            int row = m != null ? WingRows.IndexOf(last.Rows, last.Count, m.Aircraft.persistentID.Id) : -1;
            InboundRow launch = default;
            bool isInbound = (s == PilotStatus.Flying || s == PilotStatus.Inbound) && FindLaunch(p, out launch);
            float left = s == PilotStatus.LocalSar ? WingSearchAndRescue.LocalRecoveryRemaining(p) : -1f;
            float cost = p != null && (s == PilotStatus.Downed || s == PilotStatus.Missing) ? WingSearchAndRescue.LocalCost(p) : -1f;
            float funds = cost > 0f && GameManager.GetLocalPlayer(out Player player) && player != null ? player.Allocation : 0f;
            bool asking = p != null && localGate.IsArmed(p.Callsign, Time.unscaledTime);
            // AIR SAR's own check (the helicopter, the survivor on land) moves without the roster: asked every refresh while it matters.
            string air = null;
            PilotDismounted survivor = s == PilotStatus.Downed ? WingSearchAndRescue.SurvivorOf(p) : null;
            bool airCan = survivor != null && wing != null && wing.CanRescue(survivor, out air);
            if (s == PilotStatus.Downed && survivor == null) air = SquadronWords.LocalGone;
            int key;
            unchecked
            {
                key = scanVersion * 31 + at * 7 + (client ? 3 : 0) + (asking ? 5 : 0) + (int)(funds * 10f) * 13 + (int)(cost * 10f) * 17
                    + (left >= 0f ? (int)left + 1 : 0) * 19 + (airCan ? 23 : 0) + (air != null ? air.GetHashCode() : 0)
                    + (row >= 0 ? last.Rows[row].Duty * 29 + last.Rows[row].Behaviour * 37 + last.Rows[row].Flags * 41 + last.Rows[row].Element * 43 : 0)
                    + (isInbound ? (int)launch.Phase * 47 + 1 : 0);
            }
            if (key == barKey) return;
            barKey = key;
            if (p == null)
            {
                WmcKit.Set(barName, client ? SquadronWords.ClientWhy : SquadronWords.NoFocus);
                WmcKit.Set(barSlot, "");
                WmcUi.SetRail(barRail, "inert");
                SetIcon(null);
                airWhy = localWhy = client ? SquadronWords.ClientWhy : SquadronWords.NoFocus;
                SetSar(airSar, SquadronWords.AirLabel(0), false, false, airWhy);
                SetSar(localSar, SquadronWords.LocalLabel(false, null), false, false, localWhy);
                return;
            }
            bool next = ReferenceEquals(p, upcoming);
            WmcUi.SetRail(barRail, SquadronWords.Rail(s, next));
            AircraftDefinition current = m != null ? m.Aircraft.definition : isInbound ? launch.Type : null;
            WmcKit.Set(barName, WmcText.Cut(SquadronWords.Airframe(current?.unitName, p.LastAircraft), 44));
            SetIcon(current ?? LastFlown(p));
            WmcKit.Set(barSlot, Slot(p, s, next, m, row, isInbound, launch, left, cost, airCan));

            // AIR SAR: the dossier's pilot, down on land; a helicopter already going latches its number.
            int rescuer = s == PilotStatus.Rescue ? number[at] : 0;
            airWhy = SquadronWords.AirWhy(s, false) ?? (airCan ? null : "AIR SAR · " + air);
            SetSar(airSar, SquadronWords.AirLabel(rescuer), airWhy == null, rescuer > 0, airWhy ?? SquadronWords.AirTip);

            // LOCAL SAR: two presses, priced on the slot line; the countdown latches while it runs.
            string countdown = left >= 0f ? WmcText.Clock(left) : null;
            if (SquadronWords.LocalWhy(s, false) is string stateWhy) localWhy = stateWhy;
            else localWhy = WingSearchAndRescue.CanOrganizeLocalRecovery(p, out string why) ? null : why;
            SetSar(localSar, SquadronWords.LocalLabel(asking, countdown), localWhy == null, asking || countdown != null,
                localWhy ?? SquadronWords.LocalTip(Credits.Price(cost)));
        }

        /// <summary>The slot line (wraps to two lines): the member's number, element and duty; the launch's phase and field; what a
        /// search costs; the countdown; the loss.</summary>
        private string Slot(WingPilot p, PilotStatus s, bool next, WingMember m, int row, bool isInbound, in InboundRow launch, float left,
            float cost, bool airCan)
        {
            switch (s)
            {
                case PilotStatus.Flying:
                    if (isInbound) return "#" + (m != null ? m.Number : 0).ToString(CultureInfo.InvariantCulture) + " · "
                        + SquadronWords.InboundSlot(InboundWords.Phase(launch.Phase), FieldName(launch.Field));
                    if (m == null || row < 0) return SquadronWords.Row(s, false, 0);
                    return SquadronWords.FlyingSlot(m.Number, "ELEMENT " + WmcText.Cut(wing.Roster.Name(last.Rows[row].Element), 12),
                        WingRows.State(last.Rows[row]));
                case PilotStatus.Inbound:
                    return isInbound ? SquadronWords.InboundSlot(InboundWords.Phase(launch.Phase), FieldName(launch.Field))
                        : SquadronWords.InboundSlot(InboundWords.Phase(InboundPhase.Queued), null);
                case PilotStatus.Downed: return SquadronWords.DownedSlot(Credits.Price(cost), airCan);
                case PilotStatus.Rescue: return SquadronWords.RescueSlot(number[IndexOf(p)]);
                case PilotStatus.Missing: return SquadronWords.MissingSlot(Credits.Price(cost), WmcText.Clock(WingSearchAndRescue.LocalRecoveryDuration));
                case PilotStatus.LocalSar: return SquadronWords.LocalSarSlot(WmcText.Clock(left));
                case PilotStatus.Captured: return SquadronWords.CapturedSlot;
                case PilotStatus.Kia: return SquadronWords.KiaSlot(p.LossCause, p.KilledBy);
                default: return SquadronWords.FreeSlot(next);
            }
        }

        private static string FieldName(Airbase field) => field != null ? BaseName.Short(WingRequisition.NameOf(field)).ToUpperInvariant() : null;

        /// <summary>The launch carrying this pilot (queued, spawning, taxiing, departing or joining), if any.</summary>
        private bool FindLaunch(WingPilot p, out InboundRow launch)
        {
            launch = default;
            if (p == null || SpawnService.Instance == null) return false;
            int n = SpawnService.Instance.Inbound(inboundRows);
            for (int i = 0; i < n && i < inboundRows.Length; i++)
                if (ReferenceEquals(inboundRows[i].Pilot, p))
                {
                    launch = inboundRows[i];
                    return true;
                }
            return false;
        }

        /// <summary>The airframe this pilot last flew, looked up by name once when the bar changes.</summary>
        private static AircraftDefinition LastFlown(WingPilot p)
        {
            Encyclopedia enc = Encyclopedia.i;
            if (p == null || string.IsNullOrEmpty(p.LastAircraft) || enc == null || enc.aircraft == null) return null;
            foreach (AircraftDefinition d in enc.aircraft)
                if (d != null && d.unitName == p.LastAircraft) return d;
            return null;
        }

        private void SetIcon(AircraftDefinition d)
        {
            int k = d != null ? d.GetHashCode() : 0;
            if (k == iconFor) return;
            iconFor = k;
            barIcon.sprite = d != null ? IconFactory.Aircraft(d) : null;
            barIcon.enabled = barIcon.sprite != null;
        }

        private static void SetSar(AvButton b, string label, bool on, bool latched, string tip)
        {
            b.SetText(label);
            b.SetLatched(latched);
            b.SetEnabled(on);
            b.WithTooltip(tip);
        }

        /// <summary>AIR SAR: a Rescue order naming this pilot's survivor (the executor answers in its own words).</summary>
        private void AirSar()
        {
            WingPilot p = inspected;
            if (last == null || p == null || client || airWhy != null) return;
            WmcUi.Order(last, () =>
            {
                PilotDismounted survivor = WingSearchAndRescue.SurvivorOf(p);
                if (survivor == null) return;
                WingOrders.Run(new WingOrder { Kind = OrderKind.Rescue, Units = new[] { survivor.persistentID.Id } });
                barKey = int.MinValue;
                WmcPanel.Instance?.Refresh();
            });
        }

        /// <summary>LOCAL SAR, pressed twice: the first says what it costs, the second pays and starts the search.</summary>
        private void LocalSar()
        {
            WingPilot p = inspected;
            if (p == null || client || localWhy != null) return;
            if (!localGate.Press(p.Callsign, Time.unscaledTime))
            {
                WingToast.Show(SquadronWords.LocalAsk(WmcText.Cut(p.Callsign, PilotPick.CallsignChars), Credits.Price(WingSearchAndRescue.LocalCost(p)),
                    WmcText.Clock(WingSearchAndRescue.LocalRecoveryDuration)));
            }
            else WingSearchAndRescue.OrganizeLocalRecovery(p);
            barKey = int.MinValue;
            WmcPanel.Instance?.Refresh();
        }
    }
}
