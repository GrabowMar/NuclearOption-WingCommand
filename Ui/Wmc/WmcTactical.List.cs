using System.Globalization;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>TACTICAL's flight list (spec WMC rebuild §TACTICAL): the 0.9 rows — slot, airframe icon, type, callsign, state
    /// — with [RTB] (press twice), [RDR], [EJ] and [INSPECT ›], under element headers. Views are built once and moved into
    /// place; text is set only when it changes.</summary>
    internal sealed partial class WmcTactical
    {
        private const int MaxRows = WcSnapshot.MaxMembers, MaxLines = MaxRows + ElementRoster.MaxElements;
        private const float RowH = 28f, HeaderH = 20f, EmptyH = 48f;

        private sealed class RowView
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Fill, Rail, SlotBox, Icon;
            public TMP_Text Slot, Type, Callsign, State;
            public AvButton Hit, Rtb, Rdr, Ej, Inspect;
            public uint Id;
            public string IdText;
            public int Key = int.MinValue;
            public bool Selected, Styled, RtbAsking, EjAsking;
            public string RdrText;
        }

        private sealed class HeaderView
        {
            public GameObject Root;
            public RectTransform Rect;
            public TMP_Text Name, Task, Count;
            public int Key = int.MinValue;
            public bool Placed;
        }

        private readonly RowView[] rowViews = new RowView[MaxRows];
        private readonly HeaderView[] headerViews = new HeaderView[ElementRoster.MaxElements];
        private readonly FlightLine[] lines = new FlightLine[MaxLines];
        private readonly int[] order = new int[MaxRows];
        private readonly ConfirmGate rtbGate = new ConfirmGate();
        // Its own gate (eject.md): RTB then EJ on one row must not confirm the ejection on the first EJ press.
        private readonly ConfirmGate ejGate = new ConfirmGate();
        private GameObject pagerRoot, emptyRoot;
        private RectTransform pagerRect;
        private TMP_Text pagerLabel, emptyText;
        private AvButton pagerPrev, pagerNext;
        private int listPage, pageCount = 1, pagerKey = int.MinValue;

        private void BuildList()
        {
            for (int e = 0; e < headerViews.Length; e++) headerViews[e] = BuildHeader(e);
            for (int i = 0; i < rowViews.Length; i++) rowViews[i] = BuildRow(i);

            pagerRect = Container(page, "TacticalPager", new Rect(x, listTop, width, BezelLayout.Pager));
            pagerRoot = pagerRect.gameObject;
            pagerPrev = AvStyled.Button(pagerRect, new Rect(0f, 0f, 30f, BezelLayout.Pager - 2f), "‹", "btn", () => TurnPage(-1), AvButtonStyle.Quiet);
            pagerNext = AvStyled.Button(pagerRect, new Rect(width - 30f, 0f, 30f, BezelLayout.Pager - 2f), "›", "btn", () => TurnPage(1), AvButtonStyle.Quiet);
            pagerLabel = WmcKit.Text(pagerRect, new Rect(34f, 0f, width - 68f, BezelLayout.Pager - 2f), "row-sub", TextAlignmentOptions.Center);
            ids["tac.list.prev"] = pagerPrev;
            ids["tac.list.next"] = pagerNext;
            pagerRoot.SetActive(false);

            RectTransform empty = Container(page, "TacticalEmpty", new Rect(x, listTop, width, EmptyH));
            emptyRoot = empty.gameObject;
            AvStyled.Box(empty, new Rect(0f, 0f, width, EmptyH), "card", "inert");
            emptyText = WmcKit.Text(empty, new Rect(12f, -6f, width - 150f, 18f), "row-name");
            AvStyled.Label(empty, new Rect(12f, -26f, width - 150f, 16f), "Requisition wingmen on SUPPLY, or call them from the radial menu.",
                "row-sub");
            AvButton supply = AvStyled.Button(empty, new Rect(width - 128f, -12f, 120f, 24f), "OPEN SUPPLY", "btn",
                () => WmcPanel.Instance?.Show(WmcPanel.TabSupply));
            supply.WithTooltip("Requisition a wingman: pilot, airframe, fit and base.");
            ids["tac.list.supply"] = supply;
            emptyRoot.SetActive(false);
        }

        private HeaderView BuildHeader(int e)
        {
            RectTransform rt = Container(page, "TacticalHeader" + e, new Rect(x, listTop, width, HeaderH));
            var v = new HeaderView { Root = rt.gameObject, Rect = rt };
            AvButton hit = WmcUi.Card(rt, new Rect(0f, 0f, width, HeaderH), () => PickElement(e), out _, out Image rail);
            WmcUi.SetRail(rail, "info");
            hit.WithTooltip("Orders go to this whole element.");
            ids["tac.list.el" + e] = hit;
            v.Name = WmcKit.Text(rt, new Rect(8f, -2f, 142f, HeaderH - 4f), "section-title");
            v.Task = WmcKit.Text(rt, new Rect(154f, -2f, width - 214f, HeaderH - 4f), "row-sub");
            v.Count = WmcKit.Text(rt, new Rect(width - 56f, -2f, 50f, HeaderH - 4f), "row-sub", TextAlignmentOptions.MidlineRight);
            v.Root.SetActive(false);
            return v;
        }

        private RowView BuildRow(int index)
        {
            RectTransform rt = Container(page, "TacticalRow" + index, new Rect(x, listTop, width, RowH));
            var v = new RowView { Root = rt.gameObject, Rect = rt };
            string id = "tac.list.row" + index;
            v.Fill = AvStyled.Box(rt, new Rect(0f, 0f, width, RowH), "row");
            if (v.Fill != null) v.Fill.raycastTarget = false;
            v.Rail = AvStyled.Rail(rt, new Rect(0f, 0f, 3f, RowH), "info");
            // The identity (slot to state) is the selection target; the buttons keep their own clicks.
            v.Hit = AvKit.HitButton(rt, new Rect(0f, 0f, 252f, RowH), () => ClickRow(index));
            v.Hit.WithTooltip("Click: orders go to this wingman. Shift-click: add or remove it.");
            ids[id] = v.Hit;
            v.SlotBox = AvKit.Panel(rt, new Rect(6f, -5f, 18f, 18f), AvTheme.SurfaceInert);
            v.SlotBox.raycastTarget = false;
            v.Slot = WmcKit.Text(rt, new Rect(6f, -5f, 18f, 18f), "row-name", TextAlignmentOptions.Center);
            v.Icon = AvKit.Panel(rt, new Rect(28f, -6f, 16f, 16f), AvTheme.TextPrimary);
            v.Icon.raycastTarget = false;
            v.Icon.preserveAspect = true;
            v.Type = WmcKit.Text(rt, new Rect(48f, -4f, 48f, 20f), "row-sub");
            v.Callsign = WmcKit.Text(rt, new Rect(100f, -4f, 90f, 20f), "row-name");
            v.State = WmcKit.Text(rt, new Rect(194f, -4f, 56f, 20f), "row-value");
            v.Rtb = RowButton(rt, 256f, 38f, "RTB", () => AskRtb(index), id + ".rtb",
                "Send this wingman home to the reserve (press twice).");
            v.Rdr = RowButton(rt, 297f, 44f, "RDR", () => ToggleRadar(index), id + ".rdr",
                "This aircraft's radar: RDR on, EMCON silent or off. Press to switch it (the rest of the wing keeps its setting).");
            v.Ej = RowButton(rt, 344f, 32f, "EJ", () => AskEject(index), id + ".ej",
                "Eject this pilot (press twice): the aircraft is lost, search and rescue picks the pilot up.");
            v.Inspect = RowButton(rt, 380f, width - 380f, "INSPECT ›", () => Inspect(index), id + ".inspect",
                "Open this aircraft in the planning room: stores, damage, fuel and what its AI is doing.");
            v.Root.SetActive(false);
            return v;
        }

        private AvButton RowButton(RectTransform row, float bx, float bw, string text, System.Action click, string id, string tip)
        {
            AvButton b = AvStyled.Button(row, new Rect(bx, -3f, bw, RowH - 6f), text, "btn", click);
            b.WithTooltip(tip);
            ids[id] = b;
            return b;
        }

        private void ClickRow(int index)
        {
            if (last == null || rowViews[index].Id == 0u) return;
            uint id = rowViews[index].Id;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift) last.Selection.Toggle(id);
            else last.Selection.SelectOnly(id);
            // Review P3 I3: an order pressed right after the click goes to what the click chose.
            last.Rescope();
        }

        private void AskRtb(int index)
        {
            RowView v = rowViews[index];
            if (last == null || v.Id == 0u) return;
            WmcUi.Order(last, () =>
            {
                if (!rtbGate.Press(v.IdText, Time.unscaledTime))
                {
                    WingToast.Show("Send " + v.Callsign.text + " home? Press RTB? again");
                    return;
                }
                WingOrders.Run(WingOrder.Of(OrderKind.Rtb, WingScope.OfMembers(v.Id)));
            });
        }

        private void ToggleRadar(int index)
        {
            RowView v = rowViews[index];
            WingMember m = last?.MemberOf(v.Id);
            if (m == null) return;
            bool on = last.Wing.DoctrineFor(m).Radar == RadarPolicy.On;
            WmcUi.Order(last, () => WingOrders.Run(new WingOrder
            {
                Kind = OrderKind.SetOverride, Number = (int)DoctrineAxis.Radar, Text = on ? "Off" : "On", Scope = WingScope.OfMembers(v.Id),
            }));
        }

        private void AskEject(int index)
        {
            RowView v = rowViews[index];
            if (last == null || v.Id == 0u) return;
            WmcUi.Order(last, () =>
            {
                if (!ejGate.Press(v.IdText, Time.unscaledTime))
                {
                    WingToast.Show("Eject " + v.Callsign.text + "? The aircraft is lost. Press EJ? again");
                    return;
                }
                WingOrders.Run(new WingOrder { Kind = OrderKind.Eject, Scope = WingScope.OfMembers(v.Id), Flag = true });
            });
        }

        private void Inspect(int index)
        {
            if (last == null || rowViews[index].Id == 0u) return;
            last.Selection.Inspect(rowViews[index].Id);
            WmcRoom.Instance?.Open(RoomNotches.Plan);
        }

        private void TurnPage(int dir)
        {
            listPage += dir;
            if (listPage < 0) listPage = 0;
            if (listPage >= pageCount) listPage = pageCount - 1;
        }

        private void RefreshList(WmcContext c)
        {
            float cap = BezelLayout.ListCap(body.height);
            int n = FlightList.Page(c.Rows, c.Count, order, cap, listPage, lines, out pageCount);
            if (listPage >= pageCount) listPage = pageCount - 1;

            float y = listTop, height = 0f;
            int r = 0;
            for (int e = 0; e < headerViews.Length; e++) headerViews[e].Placed = false;
            for (int k = 0; k < n; k++)
            {
                FlightLine line = lines[k];
                if (line.Header)
                {
                    HeaderView h = headerViews[line.Element];
                    h.Placed = true;
                    if (!h.Root.activeSelf) h.Root.SetActive(true);
                    h.Rect.anchoredPosition = new Vector2(x, y);
                    FillHeader(h, line.Element, c);
                    y -= BezelLayout.HeaderPitch;
                    height += BezelLayout.HeaderPitch;
                }
                else if (r < rowViews.Length)
                {
                    RowView v = rowViews[r++];
                    if (!v.Root.activeSelf) v.Root.SetActive(true);
                    v.Rect.anchoredPosition = new Vector2(x, y);
                    FillRow(v, c.Rows[line.Row], c);
                    y -= BezelLayout.RowPitch;
                    height += BezelLayout.RowPitch;
                }
            }
            for (int e = 0; e < headerViews.Length; e++)
                if (!headerViews[e].Placed && headerViews[e].Root.activeSelf) headerViews[e].Root.SetActive(false);
            for (; r < rowViews.Length; r++)
            {
                rowViews[r].Id = 0u;
                if (rowViews[r].Root.activeSelf) rowViews[r].Root.SetActive(false);
            }

            bool paged = pageCount > 1;
            if (pagerRoot.activeSelf != paged) pagerRoot.SetActive(paged);
            if (paged)
            {
                pagerRect.anchoredPosition = new Vector2(x, y);
                int key = listPage * 100 + pageCount;
                if (key != pagerKey)
                {
                    pagerKey = key;
                    pagerLabel.text = (listPage + 1).ToString(CultureInfo.InvariantCulture) + " / " + pageCount.ToString(CultureInfo.InvariantCulture);
                }
                pagerPrev.SetEnabled(listPage > 0);
                pagerNext.SetEnabled(listPage < pageCount - 1);
                height += BezelLayout.Pager;
            }

            bool empty = c.Count == 0;
            if (emptyRoot.activeSelf != empty) emptyRoot.SetActive(empty);
            if (empty)
            {
                WmcKit.Set(emptyText, c.Client && c.Stale ? "WAITING FOR THE HOST'S WING" : "NO WINGMEN");
                height = EmptyH;
            }
            Layout(height);
        }

        private void FillHeader(HeaderView h, int e, WmcContext c)
        {
            int count = 0;
            for (int i = 0; i < c.Count; i++)
                if (c.Rows[i].Element == e) count++;
            WingPlanner p = c.Wing != null && !c.Client && c.Wing.Roster.InUse(e) ? c.Wing.PlannerOf(e) : null;
            string name = c.Wing != null && !c.Client ? c.Wing.Roster.Name(e) : ElementRoster.Letter(e);
            // The ETA ticks by the second; everything else changes rarely.
            int eta = p != null && p.Active ? (int)(p.Lead != null && p.Lead.Speed > 1f ? p.Leg * 100000 + Mathf.RoundToInt(Time.unscaledTime) : p.Leg) : -1;
            int key = count * 1000003 + eta * 31 + (p != null && p.Active ? (int)p.Current.Kind + 1 : 0) + (name?.GetHashCode() ?? 0);
            if (key == h.Key) return;
            h.Key = key;
            string letter = ElementRoster.Letter(e);
            h.Name.text = string.IsNullOrEmpty(name) || name == letter ? letter : letter + " · " + name;
            h.Task.text = p == null ? (e == 0 ? "FORM · on you" : WmcText.Unknown)
                : !p.Active ? (e == 0 ? "FORM · on you" : "FORM")
                : TaskCard.Short(p.Current, p.Leg, p.Lead != null ? p.Lead.Position : Vec3.Zero, p.Lead != null ? p.Lead.Speed : 0f);
            h.Count.text = count.ToString(CultureInfo.InvariantCulture) + " AC";
        }

        private void FillRow(RowView v, in SnapshotMember m, WmcContext c)
        {
            if (v.Id != m.Id)
            {
                v.Id = m.Id;
                v.IdText = m.Id.ToString(CultureInfo.InvariantCulture);
                v.Key = int.MinValue;
            }
            bool selected = c.Selection.Contains(m.Id);
            if (!v.Styled || v.Selected != selected)
            {
                v.Styled = true;
                v.Selected = selected;
                if (v.Fill != null) v.Hit.SetRowHighlight(v.Fill, WmcUi.RowColor(WmcStyle.RowRest(selected)), WmcUi.RowColor(WmcStyle.RowHover(selected)));
                v.SlotBox.color = selected ? AvTheme.Accent : AvTheme.SurfaceInert;
                v.Slot.color = selected ? AvTheme.TextInk : AvTheme.TextPrimary;
            }
            string state = WingRows.State(m);
            int key = m.Slot * 131 + m.Duty * 17 + m.Behaviour * 7 + m.Flags + state.GetHashCode();
            if (key != v.Key)
            {
                v.Key = key;
                Unit u = WmcContext.UnitOf(m.Id);
                AircraftDefinition def = u is Aircraft a ? a.definition : null;
                string callsign = u is Aircraft air && !c.Client ? WingPilotRoster.Of(air)?.Callsign : null;
                v.Slot.text = (m.Slot + 2).ToString(CultureInfo.InvariantCulture);
                Sprite icon = def != null ? IconFactory.Aircraft(def) : null;
                v.Icon.sprite = icon;
                v.Icon.enabled = icon != null;
                v.Type.text = def != null && !string.IsNullOrEmpty(def.code) ? def.code : WmcText.Unknown;
                v.Callsign.text = string.IsNullOrEmpty(callsign) ? WingRows.Number(m.Slot) : callsign;
                v.State.text = state;
                WmcUi.SetRail(v.Rail, WmcStyle.Rail(state));
            }
            bool asking = rtbGate.IsArmed(v.IdText, Time.unscaledTime);
            if (asking != v.RtbAsking)
            {
                v.RtbAsking = asking;
                v.Rtb.SetText(asking ? "RTB?" : "RTB");
            }
            v.Rtb.SetEnabled(c.CanOrder);
            asking = ejGate.IsArmed(v.IdText, Time.unscaledTime);
            if (asking != v.EjAsking)
            {
                v.EjAsking = asking;
                v.Ej.SetText(asking ? "EJ?" : "EJ");
            }
            v.Ej.SetEnabled(c.CanOrder);
            WingMember wm = c.CanOrder ? c.MemberOf(m.Id) : null;
            bool hasRadar = wm != null && wm.Aircraft != null && wm.Aircraft.radar is Radar;
            string rdr = wm == null ? "RDR" : !hasRadar ? "RDR —" : c.Wing.DoctrineFor(wm).Radar == RadarPolicy.On ? "RDR" : "EMCON";
            if (!ReferenceEquals(rdr, v.RdrText))
            {
                v.RdrText = rdr;
                v.Rdr.SetText(rdr);
            }
            v.Rdr.SetEnabled(hasRadar);
        }
    }
}
