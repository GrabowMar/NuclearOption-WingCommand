using System.Collections.Generic;
using System.Globalization;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>TACTICAL's situation (spec bezel v2 §5 TACTICAL): one scroll under the grid with the clickable alerts, the scope's
    /// facts as a two-column readout (TARGET, POSTURE, POOL, the lead's altitude and speed, FORM, FUEL with its bingo time, HULL),
    /// then RECENT — the wing's latest events and radio lines in as many rows as the dock leaves (hidden under two).</summary>
    internal sealed partial class WmcTactical
    {
        private const int AlertRows = 3, KvRows = 4;
        private const float KvKey = 56f, KvGap = 8f;

        private readonly Alert[] alerts = new Alert[AlertList.Max];
        private readonly GameObject[] alertRoots = new GameObject[AlertRows];
        private readonly TMP_Text[] alertTexts = new TMP_Text[AlertRows];
        private readonly Image[] alertRails = new Image[AlertRows];
        private readonly int[] alertKeys = new int[AlertRows];
        private readonly uint[] alertIds = new uint[AlertRows];
        private readonly MemberDetail[] details = new MemberDetail[WcSnapshot.MaxMembers];
        private readonly List<LogRow> recentRows = new List<LogRow>(BezelLayout.RecentMax);
        private readonly GameObject[] recentRoots = new GameObject[BezelLayout.RecentMax];
        private readonly TMP_Text[] recentTexts = new TMP_Text[BezelLayout.RecentMax];
        private readonly Image[] recentRails = new Image[BezelLayout.RecentMax];
        private readonly AvButton[] recentHits = new AvButton[BezelLayout.RecentMax];
        private readonly uint[] recentIds = new uint[BezelLayout.RecentMax];
        private readonly long[] recentKeys = new long[BezelLayout.RecentMax];
        private WmcScroll situationScroll;
        private RectTransform kvRoot, recentRoot;
        private TMP_Text target, posture, pool, lead, form, fuel, hull, leadKey, recentNote, recentEmpty;
        private int alertCount = -1, situationLayout = int.MinValue, recentKey = int.MinValue, recentShown;
        private int targetKey = int.MinValue, poolKey = int.MinValue, teleKey = int.MinValue;
        private string alertText;
        private long recentStamp = long.MinValue;

        private void BuildSituation()
        {
            situationScroll = WmcScroll.Build(page, new Rect(x, listTop, width + 8f, 60f), "SituationScroll");
            RectTransform s = situationScroll.Content;
            float w = situationScroll.Width;
            for (int i = 0; i < AlertRows; i++)
            {
                int k = i;
                RectTransform row = Container(s, "Alert" + i, new Rect(0f, -i * BezelLayout.AlertPitch, w, BezelLayout.AlertPitch - 2f));
                AvButton hit = WmcUi.Card(row, new Rect(0f, 0f, w, BezelLayout.AlertPitch - 2f), () => ClickAlert(k), out _, out alertRails[i]);
                hit.WithTooltip("Centre the map on this aircraft and give it the orders.");
                ids["tac.orders.alert" + i] = hit;
                alertTexts[i] = WmcKit.Text(row, new Rect(10f, -1f, w - 30f, BezelLayout.AlertPitch - 4f), "row-sub");
                AvStyled.Label(row, new Rect(w - 18f, -1f, 12f, BezelLayout.AlertPitch - 4f), "›", "row-sub");
                alertRoots[i] = row.gameObject;
                row.gameObject.SetActive(false);
                alertKeys[i] = int.MinValue;
            }

            float col = (w - KvGap) / 2f, vx = col + KvGap;
            kvRoot = Container(s, "Readout", new Rect(0f, 0f, w, KvRows * BezelLayout.KvPitch));
            target = Kv(kvRoot, 0f, 0, col, "TARGET");
            posture = Kv(kvRoot, vx, 0, col, "POSTURE");
            pool = Kv(kvRoot, 0f, 1, w, "POOL");
            lead = Kv(kvRoot, 0f, 2, col, "ALT", out leadKey);
            form = Kv(kvRoot, vx, 2, col, "FORM");
            fuel = Kv(kvRoot, 0f, 3, col, "FUEL");
            hull = Kv(kvRoot, vx, 3, col, "HULL");

            recentRoot = Container(s, "Recent", new Rect(0f, 0f, w, BezelLayout.RecentHead));
            AvStyled.Label(recentRoot, new Rect(0f, 0f, 120f, BezelLayout.RecentHead - 2f), "RECENT", "section-title");
            recentNote = WmcKit.Text(recentRoot, new Rect(120f, 0f, w - 120f, BezelLayout.RecentHead - 2f), "section-title-note",
                TextAlignmentOptions.MidlineRight);
            recentEmpty = WmcKit.Text(recentRoot, new Rect(0f, -BezelLayout.RecentHead, w, BezelLayout.RecentPitch - 2f), "hint");
            recentEmpty.text = "Nothing logged yet.";
            for (int i = 0; i < BezelLayout.RecentMax; i++)
            {
                int k = i;
                RectTransform row = Container(recentRoot, "Recent" + i,
                    new Rect(0f, -BezelLayout.RecentHead - i * BezelLayout.RecentPitch, w, BezelLayout.RecentPitch - 2f));
                recentHits[i] = WmcUi.Card(row, new Rect(0f, 0f, w, BezelLayout.RecentPitch - 2f), () => ClickRecent(k), out _, out recentRails[i]);
                recentHits[i].WithTooltip("Centre the map on this aircraft and give it the orders.");
                ids["tac.recent.row" + i] = recentHits[i];
                recentTexts[i] = WmcKit.Text(row, new Rect(10f, -1f, w - 16f, BezelLayout.RecentPitch - 4f), "row-sub");
                recentTexts[i].enableWordWrapping = false;
                recentRoots[i] = row.gameObject;
                recentKeys[i] = long.MinValue;
                row.gameObject.SetActive(false);
            }
            for (int i = 0; i < details.Length; i++) details[i] = new MemberDetail { Stores = new StoreLine[8] };
        }

        private static TMP_Text Kv(RectTransform root, float cx, int row, float cw, string key) => Kv(root, cx, row, cw, key, out _);

        private static TMP_Text Kv(RectTransform root, float cx, int row, float cw, string key, out TMP_Text keyText)
        {
            float y = -row * BezelLayout.KvPitch, h = BezelLayout.KvPitch - 2f;
            keyText = AvStyled.Label(root, new Rect(cx, y, KvKey, h), key, "metric-key");
            return WmcKit.Text(root, new Rect(cx + KvKey, y, cw - KvKey, h), "row-sub");
        }

        /// <summary>Alerts take the rows they need; the readout and RECENT follow; RECENT fills what the viewport leaves.</summary>
        private void PlaceSituation(int shown)
        {
            float view = situationScroll != null ? ViewHeight() : 0f;
            int key = shown * 100000 + Mathf.RoundToInt(view);
            if (key == situationLayout) return;
            situationLayout = key;
            alertCount = shown;
            float y = -shown * BezelLayout.AlertPitch - (shown > 0 ? 4f : 0f);
            kvRoot.anchoredPosition = new Vector2(0f, y);
            y -= KvRows * BezelLayout.KvPitch + 6f;
            recentRoot.anchoredPosition = new Vector2(0f, y);
            recentShown = BezelLayout.RecentRows(view + y);
            bool any = recentShown > 0;
            if (recentRoot.gameObject.activeSelf != any) recentRoot.gameObject.SetActive(any);
            recentKey = int.MinValue;
            situationScroll.SetContentHeight(-y + (any ? BezelLayout.RecentHead + recentShown * BezelLayout.RecentPitch : 0f) + 4f);
        }

        private float ViewHeight()
        {
            int max = WingService.MaxMembers;
            bool open = doctrineOpen && !BezelLayout.DoctrineSwaps(body.height, max);
            return Mathf.Max(0f, body.height - BezelLayout.TacticalFixed(max, open));
        }

        /// <summary>Alert rows showing now (automation).</summary>
        public int AlertsShown => alertCount < 0 ? 0 : alertCount;

        /// <summary>RECENT rows the dock shows now (automation).</summary>
        public int RecentShown => recentShown;

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
            // A lost aircraft is no longer anyone's to select: centre on it while it still exists.
            if (alerts[i].Kind == AlertKind.Lost) WmcMap.Center(WmcContext.UnitOf(alertIds[i]));
            else FocusAircraft(alertIds[i]);
        }

        private void ClickRecent(int i)
        {
            // By aircraft, never by seat; a gone aircraft centres nothing (review P3 I5).
            if (last == null || recentIds[i] == 0u || WmcContext.UnitOf(recentIds[i]) == null) return;
            FocusAircraft(recentIds[i]);
        }

        private string AlertLine(in Alert a, WmcContext c)
        {
            Unit u = WmcContext.UnitOf(a.Id);
            string callsign = u is Aircraft air && !c.Client ? WingPilotRoster.Of(air)?.Callsign : null;
            return AlertList.Word(a.Kind) + "  " + WingRows.Number(a.Slot) + (string.IsNullOrEmpty(callsign) ? "" : " " + callsign)
                + " · " + AlertList.Detail(a);
        }

        private void RefreshSituation(WmcContext c)
        {
            int n = AlertList.Fill(c.Rows, c.Count, c.Client ? null : c.Wing?.Events, c.MissionTime, alerts);
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
                int key = (int)alerts[i].Kind * 1000003 + (int)(alerts[i].Id % 1000003u) + alerts[i].Slot * 7 + (int)alerts[i].Why * 131;
                if (key == alertKeys[i]) continue;
                alertKeys[i] = key;
                alertTexts[i].text = AlertLine(alerts[i], c);
                WmcUi.SetRail(alertRails[i], alerts[i].Kind <= AlertKind.Damaged ? "danger" : "armed");
            }
            PlaceSituation(shown);
            // The strip's ALERT line carries only the urgent ones; the list shows the rest.
            alertText = shown > 0 && alerts[0].Kind <= AlertKind.Damaged ? alertTexts[0].text : null;

            RefreshTarget(c);
            RefreshPool(c);
            RefreshTelemetry(c);
            RefreshRecent(c);
        }

        private void RefreshTarget(WmcContext c)
        {
            bool host = c.Wing != null && !c.Client;
            WingMember one = host ? c.MemberOf(c.Selection.Single) : null;
            Unit t = one != null ? (one.AssignedTarget != null ? one.AssignedTarget : one.StandingTarget) : null;
            int key = (t != null ? t.GetInstanceID() : 0) * 31 + PostureKey(c) + (host ? 1 : 0);
            if (key == targetKey) return;
            targetKey = key;
            WmcKit.Set(target, !host ? WmcText.Unknown : t != null && !t.disabled
                ? (t.definition != null ? t.definition.unitName : t.unitName) : one != null ? "NONE" : "PICK ONE AIRCRAFT");
            WmcKit.Set(posture, host ? ScopePosture(c) : WmcText.Unknown);
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
                    WmcKit.Set(pool, "Stores are the host's");
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
            if (key == poolKey) return;
            poolKey = key;
            WmcKit.Set(pool, n == 0 ? WmcText.Unknown : FlightPool.Short(totals) + (n > 1 ? " · " + n.ToString(CultureInfo.InvariantCulture) + " AC" : ""));
        }

        /// <summary>One aircraft's own values; a group's lead (its lowest seat), named so in the key.</summary>
        private void RefreshTelemetry(WmcContext c)
        {
            WingMember m = null;
            bool isLead = false;
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
                    isLead = m != null;
                }
            }
            Aircraft a = m?.Aircraft;
            float alt = m != null ? m.Last.RadarAlt : float.NaN, spd = m != null ? m.Last.Speed : float.NaN;
            float fuelLevel = a != null && !a.disabled ? a.GetFuelLevel() : float.NaN;
            float bingo = m != null ? m.Bingo.SecondsToBingo : float.NaN;
            float hullLevel = a != null && a.partDamageTracker != null ? 1f - a.partDamageTracker.GetDetachedRatio() : float.NaN;
            FormationDefinition def = c.Wing != null && !c.Client ? c.Wing.ShapeOf(c.ScopeElement) : null;
            string shape = def != null ? WmcWords.Shape(def.Id, def.Name) : null;
            int key = R(alt / 10f) * 31 + R(spd * 3.6f) * 131 + R(fuelLevel * 100f) * 7 + R(bingo) * 1009 + R(hullLevel * 100f) * 17
                + (shape?.GetHashCode() ?? 0) + (isLead ? 1 : 0);
            if (key == teleKey) return;
            teleKey = key;
            // A group shows its lead's values: the key says so (review R1 I3).
            WmcKit.Set(leadKey, isLead ? "LEAD" : "ALT");
            WmcKit.Set(lead, WmcWords.Altitude(alt) + " · " + WmcWords.Speed(spd));
            WmcKit.Set(form, string.IsNullOrEmpty(shape) ? WmcText.Unknown : shape);
            string b = WingHudText.BingoTime(bingo);
            WmcKit.Set(fuel, WmcText.Percent(fuelLevel) + (b.Length > 0 ? " · " + b : ""));
            WmcKit.Set(hull, WmcText.Percent(hullLevel));
        }

        private void RefreshRecent(WmcContext c)
        {
            if (recentShown <= 0) return;
            WingEventRing events = c.Client ? null : c.Wing?.Events;
            RadioLog radio = RadioDirector.Instance?.Log;
            // Review U1-U2: Fill allocates while it describes events; it runs only when something was logged (or the rows changed).
            long stamp = LogRows.Stamp(events, radio) * 31L + recentShown;
            if (stamp == recentStamp && recentKey != int.MinValue) return;
            recentStamp = stamp;
            recentKey = 0;
            int n = LogRows.Fill(events, radio, recentRows, recentShown, LogFilter.None);
            WmcKit.Set(recentNote, n > 0 ? "LATEST " + n.ToString(CultureInfo.InvariantCulture) : "");
            if (recentEmpty.gameObject.activeSelf != (n == 0)) recentEmpty.gameObject.SetActive(n == 0);
            for (int i = 0; i < recentRoots.Length; i++)
            {
                bool on = i < n;
                if (recentRoots[i].activeSelf != on) recentRoots[i].SetActive(on);
                recentIds[i] = on ? recentRows[i].Id : 0u;
                if (!on)
                {
                    recentKeys[i] = long.MinValue;
                    continue;
                }
                LogRow r = recentRows[i];
                long k = (long)(r.Time * 10f) * 1009L + r.Member * 31L + (r.Radio ? 7L : 0L) + (r.Text?.Length ?? 0) + r.Id;
                if (k == recentKeys[i]) continue;
                recentKeys[i] = k;
                // A radio line or a wing-level event has no one to centre on: no false click target (review P1 m3).
                recentHits[i].SetEnabled(r.Id != 0u);
                WmcUi.SetRail(recentRails[i], r.Radio ? "info" : r.Member < 0 ? "armed" : "ready");
                recentTexts[i].text = WmcText.Clock(r.Time) + "  " + (r.Radio ? r.Text : LogRows.Who(r.Member) + " " + r.Text);
                recentTexts[i].color = r.Radio ? AvTheme.Dim : AvTheme.TextPrimary;
            }
        }

        private static int R(float v) => float.IsNaN(v) || float.IsInfinity(v) ? -1 : Mathf.RoundToInt(v);
    }
}
