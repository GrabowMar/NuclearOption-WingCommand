using System.Collections.Generic;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>TACTICAL's boards (spec WMC program §6): an element card per element in use (task, members, the legs still to
    /// fly; SELECT, SKIP, FORM UP) and the deep member card for the one selected wingman (CENTER, RTB, REFIT).</summary>
    internal sealed partial class RoomTactical
    {
        private const int LegRows = 3, CardLines = 10;
        private const float Block = 136f, LineHeight = 15f, SmallButton = 24f;

        private sealed class ElementBlock
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Rail;
            public TMP_Text Header, Members, Task;
            public readonly TMP_Text[] Legs = new TMP_Text[LegRows];
            public AvButton Select, Skip, Form;
        }

        private readonly ElementBlock[] blocks = new ElementBlock[ElementRoster.MaxElements];
        private readonly List<RouteLeg> cardLegs = new List<RouteLeg>();
        private readonly List<RouteRing> cardRings = new List<RouteRing>();
        private readonly List<uint> elementIds = new List<uint>();
        private readonly List<string> detailLines = new List<string>();
        private readonly TMP_Text[] memberLines = new TMP_Text[CardLines];
        private MemberDetail detail;
        private TMP_Text memberHint;
        private AvButton center, rtb, refit;
        private int cardsShown;

        public int Cards => cardsShown;
        public uint CardMember { get; private set; }

        private void BuildBoards(RectTransform body, Rect area)
        {
            float x = Pad, top = -Pad;
            AvStyled.Label(body, new Rect(x, top, Board, 16f), "ELEMENTS", "section-title");
            for (int e = 0; e < blocks.Length; e++)
            {
                int k = e;
                var b = new ElementBlock();
                var go = new GameObject("Element" + ElementRoster.Letter(e), typeof(RectTransform));
                b.Root = go;
                b.Rect = (RectTransform)go.transform;
                b.Rect.SetParent(body, false);
                AvKit.Place(b.Rect, new Rect(x, top - 22f - e * Block, Board, Block - 8f));
                AvButton cardHit = WmcUi.Card(b.Rect, new Rect(0f, 0f, Board, Block - 8f), null, out _, out b.Rail);
                cardHit.SetEnabled(false);
                b.Header = Line(b.Rect, 10f, -6f, Board - 16f, 18f, "row-name");
                b.Members = Line(b.Rect, 10f, -24f, Board - 16f, LineHeight, "row-sub");
                b.Task = Line(b.Rect, 10f, -24f - LineHeight, Board - 16f, LineHeight, "row-sub");
                for (int i = 0; i < LegRows; i++) b.Legs[i] = Line(b.Rect, 18f, -24f - LineHeight * (2 + i), Board - 24f, LineHeight, "hint");
                float by = -Block + 8f + SmallButton + 6f, bw = (Board - 20f - 2f * WmcUi.Gap) / 3f;
                b.Select = AvStyled.Button(b.Rect, new Rect(10f, by, bw, SmallButton), "SELECT", "btn", () => SelectElement(k));
                b.Skip = AvStyled.Button(b.Rect, new Rect(10f + bw + WmcUi.Gap, by, bw, SmallButton), "SKIP", "btn", () => SkipElement(k));
                b.Form = AvStyled.Button(b.Rect, new Rect(10f + 2f * (bw + WmcUi.Gap), by, bw, SmallButton), "FORM UP", "btn", () => FormElement(k));
                b.Select.WithTooltip("Orders go to this element.");
                b.Skip.WithTooltip("The element's task goes on to its next point now.");
                b.Form.WithTooltip(e == 0 ? "The wing forms up on you." : "The element rejoins A.");
                ids["room.el" + e + ".select"] = b.Select;
                ids["room.el" + e + ".skip"] = b.Skip;
                ids["room.el" + e + ".form"] = b.Form;
                go.SetActive(false);
                blocks[e] = b;
            }

            float cx = area.width - Pad - Card;
            AvStyled.Label(body, new Rect(cx, top, Card, 16f), "MEMBER", "section-title");
            AvButton panel = WmcUi.Card(body, new Rect(cx, top - 22f, Card, CardLines * 18f + 20f), null, out _, out Image rail);
            panel.SetEnabled(false);
            WmcUi.SetRail(rail, "info");
            for (int i = 0; i < CardLines; i++) memberLines[i] = Line(body, cx + 10f, top - 30f - i * 18f, Card - 16f, 17f, "row-sub");
            memberHint = Line(body, cx + 10f, top - 30f, Card - 16f, 17f, "hint");
            float by2 = top - 22f - CardLines * 18f - 28f, bw2 = (Card - 2f * WmcUi.Gap) / 3f;
            center = AvStyled.Button(body, new Rect(cx, by2, bw2, SmallButton + 4f), "CENTER", "btn", CenterMember);
            rtb = AvStyled.Button(body, new Rect(cx + bw2 + WmcUi.Gap, by2, bw2, SmallButton + 4f), "RTB", "btn", () => MemberOrder(OrderKind.Rtb));
            refit = AvStyled.Button(body, new Rect(cx + 2f * (bw2 + WmcUi.Gap), by2, bw2, SmallButton + 4f), "REFIT", "btn", () => MemberOrder(OrderKind.Refit));
            center.WithTooltip("Centre the map on this wingman.");
            rtb.WithTooltip("This wingman goes home.");
            refit.WithTooltip("This wingman refuels and rearms, then comes back.");
            ids["room.card.center"] = center;
            ids["room.card.rtb"] = rtb;
            ids["room.card.refit"] = refit;
        }

        private static TMP_Text Line(RectTransform parent, float x, float y, float w, float h, string classes)
        {
            TMP_Text t = AvStyled.Label(parent, new Rect(x, y, w, h), "", classes);
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private void RefreshBoards(WmcContext c)
        {
            WingService w = c.Wing;
            cardsShown = 0;
            for (int e = 0; e < blocks.Length; e++)
            {
                ElementBlock b = blocks[e];
                int count = 0;
                for (int i = 0; i < c.Count; i++)
                    if (c.Rows[i].Element == e) count++;
                bool on = w != null && (e == 0 || (w.Roster.InUse(e) && count > 0));
                if (b.Root.activeSelf != on) b.Root.SetActive(on);
                if (!on) continue;
                // In use, top down: a merged element leaves no gap (review focus 5).
                b.Rect.anchoredPosition = new Vector2(Pad, -Pad - 22f - cardsShown * Block);
                cardsShown++;
                WingPlanner p = w.PlannerOf(e);
                string task = p.Active ? p.Current.Kind.ToString().ToUpperInvariant() : e == 0 ? "FORM" : null;
                b.Header.text = ElementCard.Header(e, w.Roster.Name(e), task, count);
                b.Header.color = WmcMapOverlay.ElementColor(e);
                b.Members.text = ElementCard.Members(c.Rows, c.Count, e);
                b.Task.text = TaskCard.Text(p.Active ? p.Current : null, p.Leg, p.Active ? p.Lead.Position : Vec3.Zero, p.Active ? p.Lead.Speed : 0f);
                cardLegs.Clear();
                cardRings.Clear();
                if (p.Active) RouteView.Task(p.Current, p.Leg, p.Lead.Position, p.Lead.Speed, cardLegs, cardRings);
                int shown = 0;
                for (int i = 0; i < cardLegs.Count && shown < LegRows; i++)
                {
                    if (cardLegs[i].Number <= 0) continue;
                    b.Legs[shown++].text = RouteView.Label(cardLegs[i]);
                }
                for (int i = shown; i < LegRows; i++) b.Legs[i].text = i == 0 && !p.Active ? (e == 0 ? "On you." : "No route.") : "";
                WmcUi.SetRail(b.Rail, c.ScopeElement == e && c.Scope.Kind != ScopeKind.Wing ? "armed" : "info");
                b.Select.SetEnabled(count > 0);
                b.Skip.SetEnabled(c.CanOrder && p.Active && p.Leg >= 0);
                b.Form.SetEnabled(c.CanOrder && (e == 0 || count > 0));
            }
            RefreshCard(c);
        }

        private void RefreshCard(WmcContext c)
        {
            // The deep card follows the one selected wingman; gone, it drops (review focus 5).
            // INSPECT › on the bezel names the aircraft to show; else the one selected.
            WingMember m = c.Client ? null : c.MemberOf(c.Inspected != 0u ? c.Inspected : c.Selection.Single);
            CardMember = m != null && (object)m.Aircraft != null ? m.Aircraft.persistentID.Id : 0u;
            bool show = CardMember != 0u;
            memberHint.gameObject.SetActive(!show);
            if (!show) memberHint.text = c.Client ? "Wingman details come from the host." : "Select one wingman for the deep card.";
            detailLines.Clear();
            if (show)
            {
                WmcDetail.Gather(m, ref detail);
                DetailLines.Build(detail, detailLines);
            }
            for (int i = 0; i < CardLines; i++)
            {
                bool on = show && i < detailLines.Count;
                if (memberLines[i].gameObject.activeSelf != on) memberLines[i].gameObject.SetActive(on);
                if (on) memberLines[i].text = detailLines[i];
            }
            center.SetEnabled(show);
            rtb.SetEnabled(show && c.CanOrder);
            refit.SetEnabled(show && c.CanOrder);
        }

        private void SelectElement(int e)
        {
            WmcContext c = last;
            if (c == null) return;
            elementIds.Clear();
            for (int i = 0; i < c.Count; i++)
                if (c.Rows[i].Element == e) elementIds.Add(c.Rows[i].Id);
            if (elementIds.Count == 0) return;
            c.Selection.SelectElement(e, elementIds);
            c.Rescope();
            viewChanged = true;
        }

        private void SkipElement(int e) =>
            WmcUi.Order(last, () => WingOrders.Run(WingOrder.Of(OrderKind.SkipLeg, WingScope.OfElement(e))));

        private void FormElement(int e) =>
            WmcUi.Order(last, () => WingOrders.Run(WingOrder.Of(OrderKind.FormUp, e == 0 ? WingScope.Wing : WingScope.OfElement(e))));

        private void MemberOrder(OrderKind kind)
        {
            uint id = CardMember;
            if (id == 0u) return;
            WmcUi.Order(last, () => WingOrders.Run(WingOrder.Of(kind, WingScope.OfMembers(id))));
        }

        private void CenterMember()
        {
            Unit u = WmcContext.UnitOf(CardMember);
            if (u == null) return;
            GlobalPosition g = u.GlobalPosition();
            view.Project(g.x, g.z, out float px, out float py);
            view.PanBy(-px, -py);
            viewChanged = true;
        }
    }
}
