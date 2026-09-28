using System.Collections.Generic;
using System.Globalization;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>INSPECT (spec bezel v2 §5 INSPECT; the room's deep card, now a tab): one aircraft in depth — a chip per member, its
    /// title and CENTER, what it is doing, fuel, ammo, hull, radar, height and speed, its task, its stores by station, its pilot
    /// (DOSSIER › opens SQUADRON on them), its own recent events, and RTB · REFIT · RELEASE for that aircraft only. Inspecting never
    /// changes who orders go to (review R1 I1): the buttons act on the inspected aircraft.</summary>
    internal sealed class WmcInspect : IWmcPage
    {
        private const int MaxChips = WcSnapshot.MaxMembers, StoreRows = 4, RecentMax = 12;
        private const float Row = 24f, Line = 20f, ChipW = 40f, KvKey = 56f, Foot = 26f;

        private readonly Dictionary<string, AvButton> ids;
        private readonly AvButton[] chips = new AvButton[MaxChips];
        private readonly uint[] chipIds = new uint[MaxChips];
        private readonly TMP_Text[] stores = new TMP_Text[StoreRows * 2];
        private readonly List<string> storeLines = new List<string>(8);
        private readonly List<LogRow> recentRows = new List<LogRow>(RecentMax);
        private readonly TMP_Text[] recentTexts = new TMP_Text[RecentMax];
        private readonly GameObject[] recentRoots = new GameObject[RecentMax];
        private readonly ConfirmGate rtbGate = new ConfirmGate(), releaseGate = new ConfirmGate();
        private MemberDetail detail = new MemberDetail { Stores = new StoreLine[8] };
        private RectTransform page, content;
        private Rect body;
        private TMP_Text title, status, fuel, ammo, hull, radar, alt, task, storesHead, pilot, recentHead, empty;
        private AvButton center, dossier, rtb, refit, release;
        private GameObject detailRoot;
        private float width;
        private int recentRowsFit, key = int.MinValue, chipsKey = int.MinValue, recentKey = int.MinValue;
        private uint inspected;
        private string pilotCallsign;
        private long inspectStamp = long.MinValue;
        private WmcContext last;

        public WmcInspect(Dictionary<string, AvButton> controls) => ids = controls;

        public string Hint => "INSPECT shows one aircraft; its buttons act on it alone. A member chip picks another.";

        public string Alert => null;

        /// <summary>The aircraft on show (automation).</summary>
        public uint Inspected => inspected;

        /// <summary>Show this aircraft (INSPECT › on TACTICAL, a chip, automation).</summary>
        public void Focus(uint id)
        {
            inspected = id;
            key = int.MinValue;
            recentKey = int.MinValue;
        }

        public void Build(RectTransform pageRoot, Rect shellBody)
        {
            page = pageRoot;
            body = WmcUi.Page(page, shellBody, shellBody.height);
            width = body.width;
            float x = body.x, y = body.y;
            for (int i = 0; i < MaxChips; i++)
            {
                int k = i;
                chips[i] = AvStyled.Button(page, new Rect(x + i * (ChipW + 3f), y, ChipW, Row - 2f), "", "btn", () => PickChip(k), AvButtonStyle.Toggle);
                chips[i].gameObject.SetActive(false);
                ids["insp.m" + i] = chips[i];
            }
            center = AvStyled.Button(page, new Rect(x + width - 72f, y, 72f, Row - 2f), "CENTER", "btn", Center);
            center.WithTooltip("Centre the map on this aircraft (the view stays there until you move it or press FIT).");
            ids["insp.center"] = center;
            title = WmcKit.Text(page, new Rect(x + 120f, y, width - 200f, Row - 2f), "row-name", TextAlignmentOptions.MidlineRight);
            empty = WmcKit.Text(page, new Rect(x, y - Row - 4f, width, Line), "hint");

            content = Container(page, "InspectDetail", new Rect(x, y - Row - 4f, width, body.height - Row - 4f));
            detailRoot = content.gameObject;
            RectTransform r = content;
            float cy = 0f;
            AvButton statusCard = WmcUi.Card(r, new Rect(0f, cy, width, Line), null, out _, out Image rail);
            statusCard.SetEnabled(false);
            WmcUi.SetRail(rail, "info");
            status = WmcKit.Text(r, new Rect(10f, cy - 1f, width - 16f, Line - 2f), "row-name");
            cy -= Line + 4f;
            float col = (width - 8f) / 2f, vx = col + 8f;
            fuel = Kv(r, 0f, cy, col, "FUEL");
            ammo = Kv(r, vx, cy, col, "AMMO");
            cy -= Line;
            hull = Kv(r, 0f, cy, col, "HULL");
            radar = Kv(r, vx, cy, col, "RADAR");
            cy -= Line;
            alt = Kv(r, 0f, cy, col, "ALT");
            task = Kv(r, vx, cy, col, "TASK");
            cy -= Line + 6f;
            storesHead = WmcKit.Text(r, new Rect(0f, cy, width, Line - 2f), "section-title");
            cy -= Line;
            for (int i = 0; i < stores.Length; i++)
                stores[i] = WmcKit.Text(r, new Rect(i < StoreRows ? 0f : vx, cy - (i % StoreRows) * 18f, col, 17f), "row-sub");
            cy -= StoreRows * 18f + 6f;
            pilot = Kv(r, 0f, cy, width - 104f, "PILOT");
            dossier = AvStyled.Button(r, new Rect(width - 100f, cy, 100f, Line), "DOSSIER ›", "btn", OpenDossier);
            dossier.WithTooltip("This pilot's record on SQUADRON.");
            ids["insp.dossier"] = dossier;
            cy -= Line + 8f;
            recentHead = WmcKit.Text(r, new Rect(0f, cy, width, Line - 2f), "section-title");
            cy -= Line;
            float foot = body.height - Row - 4f - Foot;
            recentRowsFit = Mathf.Clamp((int)((foot + cy - 4f) / Line), 0, RecentMax);
            for (int i = 0; i < RecentMax; i++)
            {
                RectTransform row = Container(r, "InspectRecent" + i, new Rect(0f, cy - i * Line, width, Line - 2f));
                recentTexts[i] = WmcKit.Text(row, new Rect(8f, -1f, width - 12f, Line - 4f), "row-sub");
                recentRoots[i] = row.gameObject;
                row.gameObject.SetActive(false);
            }
            float fy = -foot, bw = (width - 2f * WmcUi.Gap) / 3f;
            rtb = AvStyled.Button(r, new Rect(0f, fy, bw, Foot - 2f), "RTB", "btn", Rtb);
            refit = AvStyled.Button(r, new Rect(bw + WmcUi.Gap, fy, bw, Foot - 2f), "REFIT", "btn", Refit);
            release = AvStyled.Button(r, new Rect(2f * (bw + WmcUi.Gap), fy, bw, Foot - 2f), "RELEASE", "btn", Release);
            rtb.WithTooltip("This aircraft goes home to the reserve (press twice).");
            refit.WithTooltip("This aircraft refuels and rearms, then comes back.");
            release.WithTooltip("This aircraft leaves the wing to the game's AI (press twice).");
            ids["insp.rtb"] = rtb;
            ids["insp.refit"] = refit;
            ids["insp.release"] = release;
        }

        private static TMP_Text Kv(RectTransform r, float cx, float y, float cw, string k)
        {
            AvStyled.Label(r, new Rect(cx, y, KvKey, Line - 2f), k, "metric-key");
            return WmcKit.Text(r, new Rect(cx + KvKey, y, cw - KvKey, Line - 2f), "row-sub");
        }

        private static RectTransform Container(RectTransform parent, string name, Rect rect)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            AvKit.Place(rt, rect);
            return rt;
        }

        public void Shown(WmcContext c)
        {
            key = chipsKey = recentKey = int.MinValue;
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            // The inspected aircraft left the wing: the first member stands in, never a stale id.
            if (inspected == 0u || WingRows.IndexOf(c.Rows, c.Count, inspected) < 0)
                inspected = c.Selection.Single != 0u ? c.Selection.Single : c.Count > 0 ? c.Rows[0].Id : 0u;
            RefreshChips(c);
            WingMember m = c.Client ? null : c.MemberOf(inspected);
            bool show = m != null && (object)m.Aircraft != null;
            if (detailRoot.activeSelf != show) detailRoot.SetActive(show);
            if (empty.gameObject.activeSelf != !show) empty.gameObject.SetActive(!show);
            center.SetEnabled(inspected != 0u);
            if (!show)
            {
                WmcKit.Set(empty, InspectWords.Empty(c.Client));
                WmcKit.Set(title, "");
                return;
            }
            RefreshDetail(c, m);
            RefreshRecent(c);
            RefreshFoot(c);
        }

        private void RefreshChips(WmcContext c)
        {
            int k = c.Count;
            for (int i = 0; i < c.Count; i++) k = k * 31 + c.Rows[i].Slot + (int)(c.Rows[i].Id % 997u);
            k = k * 7 + (int)(inspected % 1009u);
            if (k == chipsKey) return;
            chipsKey = k;
            for (int i = 0; i < MaxChips; i++)
            {
                bool on = i < c.Count;
                if (chips[i].gameObject.activeSelf != on) chips[i].gameObject.SetActive(on);
                chipIds[i] = on ? c.Rows[i].Id : 0u;
                if (!on) continue;
                chips[i].SetText(InspectWords.Chip(c.Rows[i].Slot));
                chips[i].SetLatched(c.Rows[i].Id == inspected);
                chips[i].WithTooltip("Inspect " + WingRows.Number(c.Rows[i].Slot) + ".");
            }
        }

        private void RefreshDetail(WmcContext c, WingMember m)
        {
            WmcDetail.Gather(m, ref detail);
            int row = WingRows.IndexOf(c.Rows, c.Count, inspected);
            int e = c.Wing.ElementOf(m);
            WingPlanner p = c.Wing.Roster.InUse(e) ? c.Wing.PlannerOf(e) : null;
            unchecked
            {
                int k = (int)inspected * 7 + R(detail.Fuel * 100f) * 31 + R(detail.Ammo * 100f) * 131 + R(detail.Damage * 100f) * 17
                    + detail.Radar * 3 + R(m.Last.RadarAlt / 50f) * 1009 + R(m.Last.Speed * 0.36f) * 97 + R(detail.BingoSeconds / 10f)
                    + m.Seat * 7919 + e * 104729
                    + (detail.Target?.GetHashCode() ?? 0) + (row >= 0 ? c.Rows[row].Duty * 13 + c.Rows[row].Flags * 19 : 0)
                    + (p != null && p.Active ? (int)p.Current.Kind * 23 + p.Leg * 29 : -1) + detail.StoreCount * 41;
                for (int i = 0; i < detail.StoreCount && i < detail.Stores.Length; i++) k = k * 3 + detail.Stores[i].Ammo;
                if (k == key) return;
                key = k;
            }
            Aircraft a = m.Aircraft;
            string type = a.definition != null && !string.IsNullOrEmpty(a.definition.code) ? a.definition.code : WmcText.Unknown;
            WmcKit.Set(title, InspectWords.Title(m.Seat, detail.Callsign, type));
            string state = row >= 0 ? WingRows.State(c.Rows[row]) : WmcText.Unknown;
            string name = c.Wing.Roster.Name(e);
            string letter = ElementRoster.Letter(e);
            WmcKit.Set(status, state + " · ELEMENT " + (string.IsNullOrEmpty(name) || name == letter ? letter : letter + " " + name));
            string b = WingHudText.BingoTime(detail.BingoSeconds);
            WmcKit.Set(fuel, WmcText.Percent(detail.Fuel) + (b.Length > 0 ? " · " + b : ""));
            WmcKit.Set(ammo, WmcText.Percent(detail.Ammo));
            WmcKit.Set(hull, WmcText.Percent(float.IsNaN(detail.Damage) ? float.NaN : 1f - detail.Damage));
            WmcKit.Set(radar, (detail.Radar < 0 ? "NONE" : detail.Radar > 0 ? "ON" : "OFF") + " · TGT "
                + (string.IsNullOrEmpty(detail.Target) ? "NONE" : detail.Target));
            WmcKit.Set(alt, WmcWords.Altitude(m.Last.RadarAlt) + " · " + WmcWords.Speed(m.Last.Speed));
            WmcKit.Set(task, p == null || !p.Active ? (e == 0 ? "FORM · on you" : "FORM")
                : TaskCard.Short(p.Current, p.Leg, p.Lead != null ? p.Lead.Position : Vec3.Zero, p.Lead != null ? p.Lead.Speed : 0f));
            FlightPool.StationLines(detail, storeLines);
            WmcKit.Set(storesHead, "STORES · " + detail.StoreCount.ToString(CultureInfo.InvariantCulture)
                + (detail.StoreCount == 1 ? " STATION" : " STATIONS"));
            for (int i = 0; i < stores.Length; i++) WmcKit.Set(stores[i], i < storeLines.Count ? storeLines[i] : "");
            WingPilot wp = WingPilotRoster.Of(a);
            pilotCallsign = wp?.Callsign;
            WmcKit.Set(pilot, InspectWords.Pilot(wp?.Callsign, detail.Rank, wp?.Xp ?? 0, wp?.Kills ?? 0, null));
            dossier.SetEnabled(wp != null);
        }

        private void RefreshRecent(WmcContext c)
        {
            long stamp = LogRows.Stamp(c.Wing?.Events, null) * 31L + inspected;
            if (stamp == inspectStamp && recentKey != int.MinValue) return;
            inspectStamp = stamp;
            var filter = new LogFilter { Element = -1, ById = true, Id = inspected, Rows = c.Rows, Count = c.Count };
            int n = LogRows.Fill(c.Wing?.Events, null, recentRows, recentRowsFit, filter);
            int k = n * 7919 + (int)(inspected % 1009u) + (n > 0 ? (int)(recentRows[0].Time * 10f) : 0);
            if (k == recentKey) return;
            recentKey = k;
            WmcKit.Set(recentHead, n > 0 ? "RECENT · " + WingRows.Number(Seat()) : "RECENT · NOTHING LOGGED");
            for (int i = 0; i < RecentMax; i++)
            {
                bool on = i < n;
                if (recentRoots[i].activeSelf != on) recentRoots[i].SetActive(on);
                if (on) recentTexts[i].text = WmcText.Clock(recentRows[i].Time) + "  " + recentRows[i].Text;
            }
        }

        private void RefreshFoot(WmcContext c)
        {
            if (inspected != footFor)
            {
                footFor = inspected;
                footId = inspected.ToString(CultureInfo.InvariantCulture);
            }
            bool asking = rtbGate.IsArmed(footId, Time.unscaledTime);
            if (asking != rtbAsking)
            {
                rtbAsking = asking;
                rtb.SetText(asking ? "RTB?" : "RTB");
            }
            asking = releaseGate.IsArmed(footId, Time.unscaledTime);
            if (asking != releaseAsking)
            {
                releaseAsking = asking;
                release.SetText(asking ? "RELEASE?" : "RELEASE");
            }
            rtb.SetEnabled(c.CanOrder);
            refit.SetEnabled(c.CanOrder);
            release.SetEnabled(c.CanOrder);
        }

        private uint footFor;
        private string footId = "";
        private bool rtbAsking, releaseAsking;

        private void PickChip(int i)
        {
            if (chipIds[i] == 0u) return;
            Focus(chipIds[i]);
            WmcPanel.Instance?.Refresh();
        }

        private void Center()
        {
            Unit u = WmcContext.UnitOf(inspected);
            if (u != null) WmcMap.Center(u);
        }

        /// <summary>SQUADRON's ROSTER on this pilot (review U3-U4: it could land on STUDIO, and before SQUADRON had ever been shown the
        /// empty roster made it open on the next free pilot instead).</summary>
        private void OpenDossier()
        {
            WmcPanel panel = WmcPanel.Instance;
            if (pilotCallsign == null || panel == null || panel.WingPage == null) return;
            panel.Show(WmcTabs.Squadron);
            panel.WingPage.ShowSub(WmcWing.SubRoster);
            panel.Refresh();
            panel.WingPage.Inspect(pilotCallsign);
            panel.Refresh();
        }

        private void Rtb() => Confirmed(rtbGate, OrderKind.Rtb, "Send " + WingRows.Number(Seat()) + " home? Press RTB again");

        private void Release() => Confirmed(releaseGate, OrderKind.Release, "Release " + WingRows.Number(Seat()) + " to the game's AI? Press RELEASE again");

        private void Refit() => WmcUi.Order(last, () => WingOrders.Run(WingOrder.Of(OrderKind.Refit, WingScope.OfMembers(inspected))));

        private void Confirmed(ConfirmGate gate, OrderKind kind, string ask)
        {
            if (last == null || inspected == 0u) return;
            uint id = inspected;
            WmcUi.Order(last, () =>
            {
                if (!gate.Press(id.ToString(CultureInfo.InvariantCulture), Time.unscaledTime))
                {
                    WingToast.Show(ask);
                    return;
                }
                WingOrders.Run(WingOrder.Of(kind, WingScope.OfMembers(id)));
            });
            WmcPanel.Instance?.Refresh();
        }

        private int Seat()
        {
            int i = last != null ? WingRows.IndexOf(last.Rows, last.Count, inspected) : -1;
            return i >= 0 ? last.Rows[i].Slot : 0;
        }

        /// <summary>Automation: the page's state.</summary>
        public void Report(Dictionary<string, object> into)
        {
            into["inspect_member"] = inspected != 0u && last != null && WingRows.IndexOf(last.Rows, last.Count, inspected) >= 0
                ? last.Rows[WingRows.IndexOf(last.Rows, last.Count, inspected)].Slot + 2 : 0;
            into["inspect_stores"] = storeLines.Count;
        }

        private static int R(float v) => float.IsNaN(v) || float.IsInfinity(v) ? -1 : Mathf.RoundToInt(v);
    }
}
