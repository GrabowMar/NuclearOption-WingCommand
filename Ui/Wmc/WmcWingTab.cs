using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>WING (spec WMC program §4): scoped actions, the deep card of the one selected wingman, then each element in
    /// use — a header (click: the whole element) and its members (click: add or remove from the selection). Headers and
    /// rows are built once and moved into place each refresh.</summary>
    internal sealed class WmcWingTab : IWmcTab
    {
        private const float RowHeight = 52f, HeaderHeight = 24f, Gap = 6f, CardHeight = 88f, BarHeight = 3f;

        private sealed class RowView
        {
            public GameObject Root;
            public RectTransform Rect;
            public TMP_Text Name, Sub, State, Flags;
            public Image Fuel, Ammo, Select, Rail;
            public AvButton Hit;
            public bool Selected, Styled;
            public float BarWidth;
        }

        private sealed class HeaderView
        {
            public GameObject Root;
            public RectTransform Rect;
            public TMP_Text Text;
        }

        private readonly RowView[] rows = new RowView[FormationCatalog.MaxSlots];
        private readonly uint[] rowIds = new uint[FormationCatalog.MaxSlots];
        private readonly HeaderView[] headers = new HeaderView[ElementRoster.MaxElements];
        private readonly int[] order = new int[WcSnapshot.MaxMembers];
        private readonly List<uint> members = new List<uint>();
        private readonly List<string> lines = new List<string>(4);
        private readonly Dictionary<string, AvButton> ids;
        private MemberDetail detail = new MemberDetail { Stores = new StoreLine[DetailLines.MaxStores] };
        private readonly TMP_Text[] cardLines = new TMP_Text[4];
        private TMP_Text cardHint, empty;
        private AvButton center, detach, rtb, refit, release;
        private float x, width, listTop;
        private WmcContext last;
        private float armedAt = float.NegativeInfinity;

        public WmcWingTab(Dictionary<string, AvButton> controls) => ids = controls;

        public float ContentHeight => WmcUi.Row + Gap + CardHeight + Gap + ElementRoster.MaxElements * (HeaderHeight + 4f) +
                                      FormationCatalog.MaxSlots * (RowHeight + Gap);

        public string Hint => last != null && last.Count == 0 ? "No wingmen. Call aircraft from the radial menu."
            : "Click wingmen to choose who orders go to; click an element's header to take it whole.";

        public void Build(RectTransform page, Rect body)
        {
            body = WmcUi.Page(page, body, ContentHeight);
            x = body.x;
            width = body.width;
            float y = WmcUi.Buttons(page, body, body.y, 5, ids,
                ("wing.center", "CENTER", Center, "Centre the map on the selected wingman."),
                ("wing.detach", "DETACH", Detach, "The selected wingmen orbit where they are, as their own element."),
                ("wing.rtb", "RTB", () => Order(OrderKind.Rtb), "Home to the reserve (the selection, or the wing)."),
                ("wing.refit", "REFIT", () => Order(OrderKind.Refit), "Home to refuel and rearm, then back out."),
                ("wing.release", "RELEASE", Release, "Release the selected wingmen to the game's AI (press twice)."));
            center = ids["wing.center"];
            detach = ids["wing.detach"];
            rtb = ids["wing.rtb"];
            refit = ids["wing.refit"];
            release = ids["wing.release"];
            y += WmcUi.Gap - Gap;

            AvStyled.Box(page, new Rect(x, y, width, CardHeight), "card");
            for (int i = 0; i < cardLines.Length; i++)
            {
                cardLines[i] = AvStyled.Label(page, new Rect(x + 12f, y - 8f - i * 18f, width - 20f, 16f), "", i == 0 ? "row-name" : "row-sub");
                cardLines[i].enableWordWrapping = false;
                cardLines[i].overflowMode = TextOverflowModes.Ellipsis;
            }
            cardHint = AvStyled.Label(page, new Rect(x + 12f, y - 8f, width - 20f, CardHeight - 16f), "", "hint");
            y -= CardHeight + Gap;
            listTop = y;

            for (int e = 0; e < headers.Length; e++) headers[e] = BuildHeader(page, e);
            for (int i = 0; i < rows.Length; i++) rows[i] = BuildRow(page, i);
            empty = AvStyled.Label(page, new Rect(x, listTop, width, 40f), "No wingmen yet.", "hint");
        }

        private RectTransform Container(RectTransform page, string name, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(page, false);
            AvKit.Place(rt, new Rect(x, listTop, width, height));
            go.SetActive(false);
            return rt;
        }

        private HeaderView BuildHeader(RectTransform page, int e)
        {
            RectTransform rt = Container(page, "WingHeader" + e, HeaderHeight);
            var v = new HeaderView { Root = rt.gameObject, Rect = rt };
            AvButton hit = WmcUi.Card(rt, new Rect(0f, 0f, width, HeaderHeight), () => PickElement(e), out _, out Image rail);
            WmcUi.SetRail(rail, "info");
            ids["wing.header" + e] = hit;
            v.Text = AvStyled.Label(rt, new Rect(12f, -3f, width - 20f, HeaderHeight - 4f), "", "section-title");
            return v;
        }

        private RowView BuildRow(RectTransform page, int index)
        {
            RectTransform rt = Container(page, "WingRow" + index, RowHeight);
            var v = new RowView { Root = rt.gameObject, Rect = rt };
            v.Hit = WmcUi.Card(rt, new Rect(0f, 0f, width, RowHeight), () => Toggle(index), out v.Select, out v.Rail);
            ids["wing.row" + index] = v.Hit;
            float lx = 12f, w = width - 22f;
            v.Name = AvStyled.Label(rt, new Rect(lx, -5f, w * 0.62f, 18f), "", "row-name");
            v.State = AvStyled.Label(rt, new Rect(lx + w * 0.62f, -5f, w * 0.38f, 18f), "", "row-value",
                align: TextAlignmentOptions.MidlineRight);
            v.Sub = AvStyled.Label(rt, new Rect(lx, -24f, w * 0.4f, 14f), "", "row-sub");
            v.Flags = AvStyled.Label(rt, new Rect(lx + w * 0.4f, -24f, w * 0.6f, 14f), "", "row-sub",
                align: TextAlignmentOptions.MidlineRight);
            v.Sub.enableWordWrapping = v.Flags.enableWordWrapping = false;
            v.Sub.overflowMode = v.Flags.overflowMode = TextOverflowModes.Ellipsis;
            v.BarWidth = (w - 8f) / 2f;
            v.Fuel = WmcUi.Bar(rt, new Rect(lx, -43f, v.BarWidth, BarHeight));
            v.Ammo = WmcUi.Bar(rt, new Rect(lx + v.BarWidth + 8f, -43f, v.BarWidth, BarHeight));
            return v;
        }

        private void Toggle(int index)
        {
            if (last == null || rowIds[index] == 0u) return;
            last.Selection.Toggle(rowIds[index]);
        }

        private void PickElement(int e)
        {
            if (last == null) return;
            members.Clear();
            for (int i = 0; i < last.Count; i++)
                if (last.Rows[i].Element == e) members.Add(last.Rows[i].Id);
            if (members.Count > 0) last.Selection.SelectElement(e, members);
        }

        private void Center()
        {
            if (last != null) WmcMap.Center(WmcContext.UnitOf(last.Selection.Single));
        }

        private void Order(OrderKind kind) => WmcUi.Order(last, () => WingOrders.Run(WingOrder.Of(kind, last.Scope)));

        /// <summary>The selected wingmen orbit the point they are over now, as their own element.</summary>
        private void Detach()
        {
            WmcUi.Order(last, () =>
            {
                if (last.Scope.Kind == ScopeKind.Wing)
                {
                    WingToast.Show("Select the wingmen to detach");
                    return;
                }
                Vec3 sum = Vec3.Zero;
                int n = 0;
                foreach (WingMember m in last.Wing.Members)
                    if ((object)m.Aircraft != null && last.Selection.Contains(m.Aircraft.persistentID.Id))
                    {
                        sum += m.Last.Pos;
                        n++;
                    }
                if (n == 0) return;
                Vec3 c = sum * (1f / n);
                Waypoint at = Waypoint.At(c.X, c.Z);
                at.Altitude = c.Y;
                WingOrders.Run(WingOrder.Tasked(WingTask.Orbit(at), last.Scope));
            });
        }

        private void Release()
        {
            WmcUi.Order(last, () =>
            {
                if (last.Scope.Kind == ScopeKind.Wing)
                {
                    WingToast.Show("Select the wingmen to release");
                    return;
                }
                if (Time.unscaledTime - armedAt > 3f)
                {
                    armedAt = Time.unscaledTime;
                    WingToast.Show($"Release {last.Selection.Label(last.Rows, last.Count)}? Press RELEASE again");
                    return;
                }
                armedAt = float.NegativeInfinity;
                if (WingOrders.Run(WingOrder.Of(OrderKind.Release, last.Scope)).Accepted) last.Selection.Clear();
            });
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            ElementGroups.Order(c.Rows, c.Count, order);
            float y = listTop;
            int r = 0;
            for (int e = 0; e < headers.Length; e++)
            {
                int count = 0;
                for (int i = 0; i < c.Count; i++)
                    if (c.Rows[i].Element == e) count++;
                HeaderView h = headers[e];
                if (h.Root.activeSelf != (count > 0)) h.Root.SetActive(count > 0);
                if (count == 0) continue;
                h.Rect.anchoredPosition = new Vector2(x, y);
                h.Text.text = ElementGroups.Header(e, c.Wing != null ? c.Wing.Roster.Name(e) : ElementRoster.Letter(e), count, TaskWord(c, e));
                y -= HeaderHeight + 4f;
                for (int k = 0; k < c.Count && r < rows.Length; k++)
                {
                    SnapshotMember m = c.Rows[order[k]];
                    if (m.Element != e) continue;
                    RowView v = rows[r];
                    rowIds[r] = m.Id;
                    if (!v.Root.activeSelf) v.Root.SetActive(true);
                    v.Rect.anchoredPosition = new Vector2(x, y);
                    FillRow(v, m, c);
                    y -= RowHeight + Gap;
                    r++;
                }
            }
            for (; r < rows.Length; r++)
            {
                rowIds[r] = 0u;
                if (rows[r].Root.activeSelf) rows[r].Root.SetActive(false);
            }
            empty.gameObject.SetActive(c.Count == 0);
            empty.text = c.Client && c.Stale ? "Waiting for the host's wing." : "No wingmen yet.";
            FillCard(c);
            bool scoped = c.Scope.Kind != ScopeKind.Wing;
            center.SetEnabled(c.Selection.Single != 0u);
            detach.SetEnabled(scoped && c.CanOrder);
            rtb.SetEnabled(c.CanOrder && c.Count > 0);
            refit.SetEnabled(c.CanOrder && c.Count > 0);
            release.SetEnabled(scoped && c.CanOrder);
        }

        private static string TaskWord(WmcContext c, int e)
        {
            if (c.Wing == null) return WmcText.Unknown;
            WingPlanner p = c.Wing.PlannerOf(e);
            return p.Active ? p.Current.Kind.ToString().ToUpperInvariant() : e == 0 ? "FORM" : WmcText.Unknown;
        }

        private void FillRow(RowView v, in SnapshotMember m, WmcContext c)
        {
            bool selected = c.Selection.Contains(m.Id);
            if (!v.Styled || v.Selected != selected)
            {
                v.Styled = true;
                v.Selected = selected;
                if (v.Select != null)
                    v.Hit.SetRowHighlight(v.Select, WmcUi.RowColor(WmcStyle.RowRest(selected)), WmcUi.RowColor(WmcStyle.RowHover(selected)));
            }
            Unit u = WmcContext.UnitOf(m.Id);
            string callsign = u is Aircraft a && !c.Client ? WingPilotRoster.Of(a)?.Callsign : null;
            v.Name.text = WmcStyle.SelectedMark(selected) + WingRows.Number(m.Slot) + "  " + (callsign ?? "");
            v.Sub.text = u != null && u.definition != null ? u.definition.unitName : WmcText.Unknown;
            string state = WingRows.State(m);
            v.State.text = state;
            WmcUi.SetRail(v.Rail, WmcStyle.Rail(state));
            string flags = WingRows.Flags(m.Flags);
            float fuel = WingRows.Fraction(m.Fuel), ammo = WingRows.Fraction(m.Ammo);
            string fuelWord = WmcStyle.LevelWord(fuel), ammoWord = WmcStyle.LevelWord(ammo);
            v.Flags.text = "FUEL " + WmcText.Percent(fuel) + (fuelWord.Length > 0 ? " " + fuelWord : "") +
                           "   AMMO " + WmcText.Percent(ammo) + (ammoWord.Length > 0 ? " " + ammoWord : "") +
                           (flags.Length > 0 ? "   " + flags : "");
            WmcUi.SetBar(v.Fuel, v.BarWidth, fuel, WmcUi.LevelColor(WmcStyle.Level(fuel)));
            WmcUi.SetBar(v.Ammo, v.BarWidth, ammo, WmcUi.LevelColor(WmcStyle.Level(ammo)));
        }

        private void FillCard(WmcContext c)
        {
            WingMember m = c.Client ? null : c.MemberOf(c.Selection.Single);
            bool show = m != null;
            for (int i = 0; i < cardLines.Length; i++) cardLines[i].gameObject.SetActive(show);
            cardHint.gameObject.SetActive(!show);
            if (!show)
            {
                cardHint.text = c.Client ? "Wingman details come from the host."
                    : c.Count == 0 ? "" : "Select one wingman for fuel, stores, target and pilot.";
                return;
            }
            WmcDetail.Gather(m, ref detail);
            DetailLines.Build(detail, lines);
            for (int i = 0; i < cardLines.Length && i < lines.Count; i++) cardLines[i].text = lines[i];
        }
    }
}
