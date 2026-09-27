using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>PLAN › ELEMENTS (spec bezel v2 §5; the room's element cards): a card per element in use, top down with no gaps —
    /// its letter in its colour, name, task word and members, the task in words, the next three legs, then SELECT · SKIP · FORM UP ·
    /// FIT. Only SELECT changes who orders go to.</summary>
    internal sealed partial class WmcPlan
    {
        private const int LegRows = 3;
        private const float CardHead = 20f, CardLine = 17f, CardButtons = 24f, CardPad = 6f;
        private const float CardH = CardHead + CardLine + LegRows * CardLine + CardButtons + CardPad * 2f, CardPitch = CardH + 6f;

        private sealed class ElementCardView
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Rail;
            public TMP_Text Header, Members, Task;
            public readonly TMP_Text[] Legs = new TMP_Text[LegRows];
            public AvButton Select, Skip, Form, Fit;
        }

        private readonly ElementCardView[] cards = new ElementCardView[ElementRoster.MaxElements];
        private readonly List<RouteLeg> cardLegs = new List<RouteLeg>();
        private readonly List<RouteRing> cardRings = new List<RouteRing>();
        private readonly List<uint> elementIds = new List<uint>();
        private WmcScroll elementsScroll;
        private TMP_Text elementsEmpty;
        private int elementsKey = int.MinValue, cardsShown;

        /// <summary>Element cards showing now (automation).</summary>
        public int Cards => cardsShown;

        private void BuildElements(RectTransform root, float top, float height)
        {
            elementsScroll = WmcScroll.Build(root, new Rect(x, top, width + 8f, height), "PlanElementsScroll");
            RectTransform s = elementsScroll.Content;
            float w = elementsScroll.Width;
            elementsEmpty = WmcKit.Text(s, new Rect(0f, 0f, w, 20f), "hint");
            for (int e = 0; e < cards.Length; e++)
            {
                int k = e;
                var v = new ElementCardView();
                RectTransform rt = Container(s, "PlanElement" + ElementRoster.Letter(e), new Rect(0f, -e * CardPitch, w, CardH));
                v.Root = rt.gameObject;
                v.Rect = rt;
                AvButton back = WmcUi.Card(rt, new Rect(0f, 0f, w, CardH), null, out _, out v.Rail);
                back.SetEnabled(false);
                float y = -CardPad;
                v.Header = WmcKit.Text(rt, new Rect(10f, y, w - 120f, CardHead - 2f), "row-name");
                v.Members = WmcKit.Text(rt, new Rect(w - 110f, y, 104f, CardHead - 2f), "row-sub", TextAlignmentOptions.MidlineRight);
                y -= CardHead;
                v.Task = WmcKit.Text(rt, new Rect(10f, y, w - 16f, CardLine - 1f), "row-sub");
                y -= CardLine;
                for (int i = 0; i < LegRows; i++) v.Legs[i] = WmcKit.Text(rt, new Rect(18f, y - i * CardLine, w - 24f, CardLine - 1f), "hint");
                y -= LegRows * CardLine;
                float bw = (w - 20f - 3f * WmcUi.Gap) / 4f;
                v.Select = AvStyled.Button(rt, new Rect(10f, y, bw, CardButtons - 2f), "SELECT", "btn", () => SelectElement(k));
                v.Skip = AvStyled.Button(rt, new Rect(10f + (bw + WmcUi.Gap), y, bw, CardButtons - 2f), "SKIP", "btn", () => SkipElement(k));
                v.Form = AvStyled.Button(rt, new Rect(10f + 2f * (bw + WmcUi.Gap), y, bw, CardButtons - 2f), "FORM UP", "btn", () => FormElement(k));
                v.Fit = AvStyled.Button(rt, new Rect(10f + 3f * (bw + WmcUi.Gap), y, bw, CardButtons - 2f), "FIT", "btn", () => FitElement(k));
                v.Select.WithTooltip("Orders go to this element.");
                v.Skip.WithTooltip("The element's task goes on to its next point now.");
                v.Form.WithTooltip(e == 0 ? "The wing forms up on you." : "The element rejoins A.");
                v.Fit.WithTooltip("Frame this element and its legs on the map.");
                ids["plan.el" + e + ".select"] = v.Select;
                ids["plan.el" + e + ".skip"] = v.Skip;
                ids["plan.el" + e + ".form"] = v.Form;
                ids["plan.el" + e + ".fit"] = v.Fit;
                v.Root.SetActive(false);
                cards[e] = v;
            }
        }

        private void RefreshElements(WmcContext c)
        {
            WingService w = c.Client ? null : c.Wing;
            int shown = 0;
            for (int e = 0; e < cards.Length; e++)
            {
                ElementCardView v = cards[e];
                int count = 0;
                for (int i = 0; i < c.Count; i++)
                    if (c.Rows[i].Element == e) count++;
                bool on = w != null && (e == 0 || (w.Roster.InUse(e) && count > 0));
                if (v.Root.activeSelf != on) v.Root.SetActive(on);
                if (!on) continue;
                // In use, top down: a merged element leaves no gap.
                v.Rect.anchoredPosition = new Vector2(0f, -shown * CardPitch);
                shown++;
                WingPlanner p = w.PlannerOf(e);
                string word = p.Active ? p.Current.Kind.ToString().ToUpperInvariant() : e == 0 ? "FORM" : null;
                WmcKit.Set(v.Header, ElementCard.Header(e, w.Roster.Name(e), word, count));
                v.Header.color = WmcMapOverlay.ElementColor(e);
                WmcKit.Set(v.Members, ElementCard.Members(c.Rows, c.Count, e));
                WmcKit.Set(v.Task, TaskCard.Text(p.Active ? p.Current : null, p.Leg, p.Active ? p.Lead.Position : Vec3.Zero, p.Active ? p.Lead.Speed : 0f));
                cardLegs.Clear();
                cardRings.Clear();
                if (p.Active) RouteView.Task(p.Current, p.Leg, p.Lead.Position, p.Lead.Speed, cardLegs, cardRings);
                int legs = 0;
                for (int i = 0; i < cardLegs.Count && legs < LegRows; i++)
                {
                    if (cardLegs[i].Number <= 0) continue;
                    WmcKit.Set(v.Legs[legs++], RouteView.Label(cardLegs[i]));
                }
                for (int i = legs; i < LegRows; i++) WmcKit.Set(v.Legs[i], i == 0 && !p.Active ? (e == 0 ? "On you." : "No route.") : "");
                WmcUi.SetRail(v.Rail, c.ScopeElement == e && c.Scope.Kind != ScopeKind.Wing ? "armed" : "info");
                v.Select.SetEnabled(count > 0);
                v.Skip.SetEnabled(c.CanOrder && p.Active && p.Leg >= 0);
                v.Form.SetEnabled(c.CanOrder && (e == 0 || count > 0));
                v.Fit.SetEnabled(WmcMap.Usable && count > 0);
            }
            cardsShown = shown;
            bool none = shown == 0;
            if (elementsEmpty.gameObject.activeSelf != none) elementsEmpty.gameObject.SetActive(none);
            if (none) WmcKit.Set(elementsEmpty, c.Client ? "Elements are the host's." : "NO WINGMEN · REQUISITION ON SUPPLY");
            if (shown != elementsKey)
            {
                elementsKey = shown;
                elementsScroll.SetContentHeight(Mathf.Max(20f, shown * CardPitch));
            }
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
            WmcPanel.Instance?.Refresh();
        }

        private void SkipElement(int e) =>
            WmcUi.Order(last, () => WingOrders.Run(WingOrder.Of(OrderKind.SkipLeg, WingScope.OfElement(e))));

        private void FormElement(int e) =>
            WmcUi.Order(last, () => WingOrders.Run(WingOrder.Of(OrderKind.FormUp, e == 0 ? WingScope.Wing : WingScope.OfElement(e))));

        /// <summary>The element's members and the legs it still flies, framed on the map.</summary>
        private void FitElement(int e)
        {
            WmcContext c = last;
            if (c?.Wing == null || c.Client) return;
            var box = new MapBox();
            foreach (WingMember m in c.Wing.Members)
                if (!m.Released && m.Alive && (object)m.Aircraft != null && c.Wing.ElementOf(m) == e) box.Add(m.Last.Pos.X, m.Last.Pos.Z);
            WingPlanner p = c.Wing.PlannerOf(e);
            if (p != null && p.Active)
            {
                cardLegs.Clear();
                cardRings.Clear();
                RouteView.Task(p.Current, p.Leg, p.Lead.Position, p.Lead.Speed, cardLegs, cardRings);
                foreach (RouteLeg l in cardLegs) box.Add(l.ToX, l.ToZ);
                foreach (RouteRing r in cardRings)
                {
                    box.Add(r.X - r.Radius, r.Z - r.Radius);
                    box.Add(r.X + r.Radius, r.Z + r.Radius);
                }
            }
            WmcMap.Fit(box);
        }
    }
}
