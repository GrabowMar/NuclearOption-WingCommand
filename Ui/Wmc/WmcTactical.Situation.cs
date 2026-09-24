using System.Collections.Generic;
using System.Globalization;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>TACTICAL › ORDERS' situation (spec WMC rebuild §TACTICAL; the 0.9 bento modernized): clickable alerts, the
    /// scope's TASK card and FLIGHT POOL card side by side, and the telemetry strip — every block at its full height inside the
    /// sub-page's scroll, nothing cut.</summary>
    internal sealed partial class WmcTactical
    {
        private const int AlertRows = 3, CardLines = 3, PoolLines = 4;
        private const float AlertPitch = 20f, CardH = 88f, TeleH = 28f;

        private readonly Alert[] alerts = new Alert[AlertList.Max];
        private readonly GameObject[] alertRoots = new GameObject[AlertRows];
        private readonly TMP_Text[] alertTexts = new TMP_Text[AlertRows];
        private readonly Image[] alertRails = new Image[AlertRows];
        private readonly int[] alertKeys = new int[AlertRows];
        private readonly uint[] alertIds = new uint[AlertRows];
        private readonly TMP_Text[] taskLines = new TMP_Text[CardLines];
        private readonly TMP_Text[] poolTexts = new TMP_Text[PoolLines];
        private readonly TMP_Text[] tele = new TMP_Text[5];
        private readonly MemberDetail[] details = new MemberDetail[WcSnapshot.MaxMembers];
        private readonly List<string> textLines = new List<string>(8);
        private RectTransform cardsRoot, teleRoot;
        private TMP_Text taskTitle, poolTitle;
        private AvButton planLink;
        private float situationTop;
        private int alertCount = -1, taskKey = int.MinValue, poolKey = int.MinValue, teleKey = int.MinValue;
        private string alertText;

        private void BuildSituation(RectTransform s, float w)
        {
            for (int i = 0; i < AlertRows; i++)
            {
                int k = i;
                RectTransform row = Container(s, "Alert" + i, new Rect(0f, situationTop - i * AlertPitch, w, AlertPitch - 2f));
                AvButton hit = WmcUi.Card(row, new Rect(0f, 0f, w, AlertPitch - 2f), () => ClickAlert(k), out _, out alertRails[i]);
                hit.WithTooltip("Centre the map on this aircraft and give it the orders.");
                ids["tac.orders.alert" + i] = hit;
                alertTexts[i] = WmcKit.Text(row, new Rect(10f, -1f, w - 30f, AlertPitch - 4f), "row-sub");
                AvStyled.Label(row, new Rect(w - 18f, -1f, 12f, AlertPitch - 4f), "›", "row-sub");
                alertRoots[i] = row.gameObject;
                row.gameObject.SetActive(false);
                alertKeys[i] = int.MinValue;
            }

            float half = (w - WmcUi.Gap * 2f) / 2f;
            cardsRoot = Container(s, "Cards", new Rect(0f, situationTop, w, CardH));
            AvStyled.Box(cardsRoot, new Rect(0f, 0f, half, CardH), "card");
            taskTitle = WmcKit.Text(cardsRoot, new Rect(8f, -4f, half - 70f, 16f), "section-title");
            planLink = AvStyled.Button(cardsRoot, new Rect(half - 60f, -3f, 54f, 18f), "PLAN ›", "btn",
                () => WmcRoom.Instance?.Open(RoomNotches.Plan), AvButtonStyle.Quiet);
            planLink.WithTooltip("Open the planning room on this element's task.");
            ids["tac.orders.plan"] = planLink;
            for (int i = 0; i < CardLines; i++) taskLines[i] = WmcKit.Text(cardsRoot, new Rect(8f, -24f - i * 20f, half - 14f, 18f), "row-sub");

            float px = half + WmcUi.Gap * 2f;
            AvStyled.Box(cardsRoot, new Rect(px, 0f, half, CardH), "card");
            poolTitle = WmcKit.Text(cardsRoot, new Rect(px + 8f, -4f, half - 14f, 16f), "section-title");
            for (int i = 0; i < PoolLines; i++) poolTexts[i] = WmcKit.Text(cardsRoot, new Rect(px + 8f, -22f - i * 16f, half - 14f, 16f), "row-sub");

            teleRoot = Container(s, "Telemetry", new Rect(0f, situationTop - CardH - 6f, w, TeleH));
            AvStyled.Box(teleRoot, new Rect(0f, 0f, w, TeleH), "row");
            float[] widths = { 0.18f, 0.2f, 0.2f, 0.24f, 0.18f };
            float tx = 6f;
            for (int i = 0; i < tele.Length; i++)
            {
                float fw = (w - 12f) * widths[i];
                tele[i] = WmcKit.Text(teleRoot, new Rect(tx, -2f, fw - 4f, TeleH - 4f), "row-sub");
                tx += fw;
            }
            for (int i = 0; i < details.Length; i++) details[i] = new MemberDetail { Stores = new StoreLine[8] };
            PlaceSituation(0);
        }

        /// <summary>Alerts take the rows they need; the cards and the strip follow; the scroll content ends under the strip.</summary>
        private void PlaceSituation(int shown)
        {
            if (shown == alertCount) return;
            alertCount = shown;
            float y = situationTop - shown * AlertPitch - (shown > 0 ? 4f : 0f);
            cardsRoot.anchoredPosition = new Vector2(0f, y);
            teleRoot.anchoredPosition = new Vector2(0f, y - CardH - 6f);
            ordersScroll.SetContentHeight(-(y - CardH - 6f - TeleH) + 4f);
        }

        /// <summary>Alert rows showing now (automation).</summary>
        public int AlertsShown => alertCount < 0 ? 0 : alertCount;

        /// <summary>The ids of the order-grid cells that cannot be pressed now (automation).</summary>
        public List<string> DisabledOrders()
        {
            var off = new List<string>();
            for (int k = 0; k < shownCells.Length; k++)
                if (!shownEnabled[k] && shownCells[k].Id != null) off.Add(shownCells[k].Id);
            return off;
        }

        private void ClickAlert(int i)
        {
            if (last == null || alertIds[i] == 0u) return;
            FocusAircraft(alertIds[i]);
        }

        private string AlertLine(in Alert a, WmcContext c)
        {
            Unit u = WmcContext.UnitOf(a.Id);
            string callsign = u is Aircraft air && !c.Client ? WingPilotRoster.Of(air)?.Callsign : null;
            return AlertList.Word(a.Kind) + "  " + WingRows.Number(a.Slot) + (string.IsNullOrEmpty(callsign) ? "" : " " + callsign)
                + " · " + AlertList.Detail(a.Kind);
        }

        private void RefreshSituation(WmcContext c)
        {
            int n = AlertList.Fill(c.Rows, c.Count, alerts);
            int shown = n < AlertRows ? n : AlertRows;
            for (int i = 0; i < AlertRows; i++)
            {
                bool on = i < shown;
                if (alertRoots[i].activeSelf != on) alertRoots[i].SetActive(on);
                if (!on)
                {
                    alertIds[i] = 0u;
                    alertKeys[i] = int.MinValue;
                    continue;
                }
                alertIds[i] = alerts[i].Id;
                int key = (int)alerts[i].Kind * 1000003 + (int)(alerts[i].Id % 1000003u) + alerts[i].Slot * 7;
                if (key == alertKeys[i]) continue;
                alertKeys[i] = key;
                alertTexts[i].text = AlertLine(alerts[i], c);
                WmcUi.SetRail(alertRails[i], alerts[i].Kind <= AlertKind.Damaged ? "danger" : "armed");
            }
            PlaceSituation(shown);
            // The strip's ALERT line carries only the urgent ones; the list shows the rest.
            alertText = shown > 0 && alerts[0].Kind <= AlertKind.Damaged ? alertTexts[0].text : null;

            RefreshTask(c);
            RefreshPool(c);
            RefreshTelemetry(c);
        }

        private void RefreshTask(WmcContext c)
        {
            bool host = c.Wing != null && !c.Client;
            int e = c.ScopeElement;
            WingPlanner p = host && c.Wing.Roster.InUse(e) ? c.Wing.PlannerOf(e) : null;
            WingMember one = host ? c.MemberOf(c.Selection.Single) : null;
            Unit target = one != null ? (one.AssignedTarget != null ? one.AssignedTarget : one.StandingTarget) : null;
            int eta = p != null && p.Active && p.Lead != null && p.Lead.Speed > 1f ? Mathf.RoundToInt(Time.unscaledTime) : 0;
            int key = e * 7 + (p != null && p.Active ? (int)p.Current.Kind * 131 + p.Leg * 17 + eta * 1009 : -1)
                + (target != null ? target.GetInstanceID() : 0) + WingRows.Summary(c.Rows, c.Count).Count * 3
                + PostureKey(c);
            if (key == taskKey) return;
            taskKey = key;
            string name = host ? c.Wing.Roster.Name(e) : ElementRoster.Letter(e);
            taskTitle.text = "TASK · " + (string.IsNullOrEmpty(name) ? ElementRoster.Letter(e) : name);
            taskLines[0].text = !host ? "Tasks are the host's" : p == null || !p.Active ? (e == 0 ? "FORM · on you" : "FORM")
                : TaskCard.Short(p.Current, p.Leg, p.Lead != null ? p.Lead.Position : Vec3.Zero, p.Lead != null ? p.Lead.Speed : 0f);
            taskLines[1].text = "TGT " + (target != null && !target.disabled
                ? (target.definition != null ? target.definition.unitName : target.unitName) : "NONE");
            taskLines[2].text = "POSTURE " + ScopePosture(c);
        }

        private int PostureKey(WmcContext c)
        {
            int k = 0;
            for (int i = 0; i < c.Count; i++)
                if (InScope(c, c.Rows[i])) k = k * 7 + c.Rows[i].Duty + 1;
            return k;
        }

        private readonly SnapshotMember[] scoped = new SnapshotMember[WcSnapshot.MaxMembers];

        private string ScopePosture(WmcContext c)
        {
            int n = 0;
            for (int i = 0; i < c.Count; i++)
                if (InScope(c, c.Rows[i])) scoped[n++] = c.Rows[i];
            return WmcWords.Posture(scoped, n);
        }

        private void RefreshPool(WmcContext c)
        {
            if (c.Wing == null || c.Client)
            {
                if (poolKey != -2)
                {
                    poolKey = -2;
                    poolTitle.text = "FLIGHT POOL";
                    SetPool("Stores are the host's");
                }
                return;
            }
            int n = 0;
            foreach (WingMember m in c.Wing.Members)
            {
                if (n >= details.Length) break;
                if (m.Released || !m.Alive || (object)m.Aircraft == null) continue;
                int i = WingRows.IndexOf(c.Rows, c.Count, m.Aircraft.persistentID.Id);
                if (i < 0 || !InScope(c, c.Rows[i])) continue;
                WmcDetail.Stores(m, ref details[n++]);
            }
            var totals = new PoolTotals { Aircraft = n };
            int key = n * 7919;
            for (int k = 0; k < n; k++)
                for (int s = 0; s < details[k].StoreCount; s++)
                {
                    FlightPool.Add(ref totals, details[k].Stores[s]);
                    key = key * 31 + details[k].Stores[s].Ammo + (int)details[k].Stores[s].Class * 7;
                }
            if (n == 1) key ^= 0x5bd1e995;
            if (key == poolKey) return;
            poolKey = key;
            if (n == 1)
            {
                poolTitle.text = "STORES";
                FlightPool.StationLines(details[0], textLines);
            }
            else
            {
                poolTitle.text = "FLIGHT POOL · " + n.ToString(CultureInfo.InvariantCulture) + " AC";
                FlightPool.Lines(totals, textLines);
            }
            for (int i = 0; i < PoolLines; i++)
            {
                string line = i < textLines.Count ? textLines[i] : "";
                if (i == PoolLines - 1 && textLines.Count > PoolLines) line = "+" + (textLines.Count - PoolLines + 1).ToString(CultureInfo.InvariantCulture) + " MORE · INSPECT ›";
                WmcKit.Set(poolTexts[i], line);
            }
        }

        private void SetPool(string only)
        {
            for (int i = 0; i < PoolLines; i++) WmcKit.Set(poolTexts[i], i == 0 ? only : "");
        }

        /// <summary>One aircraft's own values; a group's lead (its lowest seat), named so in the key.</summary>
        private void RefreshTelemetry(WmcContext c)
        {
            WingMember m = null;
            bool lead = false;
            if (c.Wing != null && !c.Client)
            {
                m = c.MemberOf(c.Selection.Single);
                if (m == null)
                {
                    int best = int.MaxValue;
                    foreach (WingMember w in c.Wing.Members)
                    {
                        if (w.Released || !w.Alive || (object)w.Aircraft == null) continue;
                        int i = WingRows.IndexOf(c.Rows, c.Count, w.Aircraft.persistentID.Id);
                        if (i < 0 || !InScope(c, c.Rows[i]) || c.Rows[i].Slot >= best) continue;
                        best = c.Rows[i].Slot;
                        m = w;
                    }
                    lead = m != null;
                }
            }
            Aircraft a = m?.Aircraft;
            float alt = m != null ? m.Last.RadarAlt : float.NaN, spd = m != null ? m.Last.Speed : float.NaN;
            float fuel = a != null && !a.disabled ? a.GetFuelLevel() : float.NaN;
            float bingo = m != null ? m.Bingo.SecondsToBingo : float.NaN;
            float hull = a != null && a.partDamageTracker != null ? 1f - a.partDamageTracker.GetDetachedRatio() : float.NaN;
            FormationDefinition def = c.Wing != null && !c.Client ? c.Wing.ShapeOf(c.ScopeElement) : null;
            string shape = def != null ? WmcWords.Shape(def.Id, def.Name) : null;
            int key = R(alt / 10f) * 31 + R(spd * 3.6f) * 131 + R(fuel * 100f) * 7 + R(bingo) * 1009 + R(hull * 100f) * 17
                + (shape?.GetHashCode() ?? 0) + (lead ? 1 : 0);
            if (key == teleKey) return;
            teleKey = key;
            // A group shows its lead's values: the key says so instead of ALT (review R1 I3: "LEAD ALT …" did not fit).
            tele[0].text = (lead ? "LEAD " : "ALT ") + WmcWords.Altitude(alt);
            tele[1].text = "SPD " + WmcWords.Speed(spd);
            tele[2].text = "FORM " + (string.IsNullOrEmpty(shape) ? WmcText.Unknown : shape);
            string b = WingHudText.BingoTime(bingo);
            tele[3].text = "FUEL " + WmcText.Percent(fuel) + (b.Length > 0 ? " · " + b : "");
            tele[4].text = "HULL " + WmcText.Percent(hull);
        }

        private static int R(float v) => float.IsNaN(v) || float.IsInfinity(v) ? -1 : Mathf.RoundToInt(v);
    }
}
