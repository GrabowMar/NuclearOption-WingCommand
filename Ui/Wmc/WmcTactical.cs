using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>TACTICAL (spec WMC rebuild §TACTICAL), the 0.9 main page rebuilt: who orders go to (the scope row), the flight
    /// list with inline RTB · RDR · EJ · INSPECT, then ORDERS · FORMATION · ROUTE. The scope row, the list and the sub-tab
    /// strip stay put; each sub-page scrolls on its own and keeps its place across refreshes.</summary>
    internal sealed partial class WmcTactical : IWmcPage
    {
        public const int SubOrders = 0, SubForm = 1, SubRoute = 2;
        private static readonly string[] SubLabels = { "ORDERS", "FORMATION", "ROUTE" };
        private const float ChipGap = 3f, AllWidth = 38f, PresetWidth = 30f;

        private readonly Dictionary<string, AvButton> ids;
        private RectTransform page, subArea;
        private Rect body;
        private float x, width, listTop, bottom, panelWidth, listHeight = -1f;
        private WmcContext last;
        private int sub = -1;
        private AvButton[] subTabs;
        private readonly GameObject[] subRoots = new GameObject[SubLabels.Length];

        private TMP_Text scopeText;
        private AvButton allChip, presetChip;
        private readonly AvButton[] elementChips = new AvButton[ElementRoster.MaxElements];
        private readonly bool[] chipInUse = new bool[ElementRoster.MaxElements];
        private readonly string[] chipNames = new string[ElementRoster.MaxElements];
        private readonly List<uint> members = new List<uint>();
        private int scopeKey = int.MinValue;

        public WmcTactical(Dictionary<string, AvButton> controls) => ids = controls;

        public int Sub => sub;

        public string Hint => last != null && last.Count == 0 && !last.Client
            ? "No wingmen yet: requisition them on SUPPLY, or call them from the radial menu."
            : "Click wingmen to choose who orders go to; an element's header takes it whole.";

        public string Alert => alertText;

        public void Build(RectTransform pageRoot, Rect shellBody)
        {
            page = pageRoot;
            panelWidth = shellBody.width;
            body = WmcUi.Page(page, shellBody, shellBody.height);
            x = body.x;
            width = body.width;
            bottom = body.y - body.height;
            BuildScopeRow(body.y);
            listTop = body.y - BezelLayout.ScopeRow - BezelLayout.ScopeGap;
            BuildList();

            subArea = Container(page, "TacticalSub", new Rect(0f, listTop, panelWidth, 10f));
            subTabs = WmcKit.SubTabs(subArea, new Rect(x, 0f, width, BezelLayout.SubTabs), SubLabels, "tac.sub.", ids, ShowSub);
            subTabs[SubOrders].WithTooltip("Doctrine, the order grid and the situation.");
            subTabs[SubForm].WithTooltip("The plan view, shapes, maneuvers; spacing, stack and power for the wing.");
            subTabs[SubRoute].WithTooltip("The scope's quick route, and your own autopilot with NAV.");
            BuildOrders(SubRoot(SubOrders, "TacticalOrders"));
            BuildFormation(SubRoot(SubForm, "TacticalFormation"));
            BuildRoute(SubRoot(SubRoute, "TacticalRoute"));
            Layout(BezelLayout.RowPitch);
            ShowSub(SubOrders);
        }

        private RectTransform SubRoot(int k, string name)
        {
            RectTransform rt = Container(subArea, name, new Rect(0f, 0f, panelWidth, 10f));
            AvKit.Stretch(rt);
            subRoots[k] = rt.gameObject;
            return rt;
        }

        private static RectTransform Container(RectTransform parent, string name, Rect r)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            AvKit.Place(rt, r);
            return rt;
        }

        /// <summary>The flight list took <paramref name="height"/> px: the sub-tabs and the sub-page follow it.</summary>
        private void Layout(float height)
        {
            if (Mathf.Abs(height - listHeight) < 0.5f) return;
            listHeight = height;
            float top = listTop - height - BezelLayout.ScopeGap;
            AvKit.Place(subArea, new Rect(0f, top, panelWidth, top - bottom));
            LayoutOrders(top - bottom);
            LayoutFormation(top - bottom);
            LayoutRoute(top - bottom);
        }

        private void ShowSub(int k)
        {
            if (k < 0 || k >= subRoots.Length || subRoots[k] == null) return;
            sub = k;
            for (int i = 0; i < subRoots.Length; i++)
            {
                if (subRoots[i] != null) subRoots[i].SetActive(i == k);
                subTabs[i].SetLatched(i == k);
            }
            AvKit.Popup.CloseAny();
        }

        /// <summary>Automation: a control of a sub-page shows that sub-page first.</summary>
        public void ShowSubFor(string id)
        {
            if (id.StartsWith("tac.orders.", StringComparison.Ordinal)) ShowSub(SubOrders);
            else if (id.StartsWith("tac.form.", StringComparison.Ordinal)) ShowSub(SubForm);
            else if (id.StartsWith("tac.route.", StringComparison.Ordinal)) ShowSub(SubRoute);
        }

        // ---------------------------------------------------------------- scope row

        private void BuildScopeRow(float y)
        {
            AvStyled.Box(page, new Rect(x, y, width, BezelLayout.ScopeRow), "row");
            AvStyled.Label(page, new Rect(x + 8f, y - 2f, 60f, BezelLayout.ScopeRow - 4f), "COMMAND", "metric-key");
            scopeText = WmcKit.Text(page, new Rect(x + 68f, y - 2f, 126f, BezelLayout.ScopeRow - 4f), "row-name");
            float right = x + width - 2f;
            presetChip = AvStyled.Button(page, new Rect(right - PresetWidth, y - 2f, PresetWidth, BezelLayout.ScopeRow - 4f), "+", "btn", null);
            presetChip.SetEnabled(false);
            presetChip.WithTooltip("Element presets: saved splits of the wing, made in PLAN. Arrives in a later update.");
            ids["tac.scope.preset"] = presetChip;
            allChip = AvStyled.Button(page, new Rect(x + 198f, y - 2f, AllWidth, BezelLayout.ScopeRow - 4f), "ALL", "btn", PickAll,
                AvButtonStyle.Toggle);
            allChip.WithTooltip("Orders go to the whole wing.");
            ids["tac.scope.all"] = allChip;
            for (int e = 0; e < elementChips.Length; e++)
            {
                int k = e;
                elementChips[e] = AvStyled.Button(page, new Rect(x + 240f, y - 2f, 40f, BezelLayout.ScopeRow - 4f), ElementRoster.Letter(e),
                    "btn", () => PickElement(k), AvButtonStyle.Toggle);
                elementChips[e].gameObject.SetActive(false);
                ids["tac.scope.el" + e] = elementChips[e];
            }
        }

        private void PickAll()
        {
            if (last == null) return;
            last.Selection.Clear();
            last.Rescope();
        }

        private void PickElement(int e)
        {
            if (last == null) return;
            members.Clear();
            for (int i = 0; i < last.Count; i++)
                if (last.Rows[i].Element == e) members.Add(last.Rows[i].Id);
            if (members.Count > 0) last.Selection.SelectElement(e, members);
            last.Rescope();
        }

        private void RefreshScope(WmcContext c)
        {
            int inScope = ScopeCount(c);
            int key = (int)c.Scope.Kind * 100000 + c.ScopeElement * 10000 + inScope * 100 + c.Selection.Count;
            if (key != scopeKey || !ReferenceEquals(scopeLabelShown, c.ScopeLabel))
            {
                scopeKey = key;
                scopeLabelShown = c.ScopeLabel;
                WmcKit.Set(scopeText, WmcWords.ScopeText(c.ScopeLabel, inScope));
            }
            allChip.SetLatched(c.Scope.Kind == ScopeKind.Wing);

            bool changed = false;
            int used = 0;
            for (int e = 0; e < elementChips.Length; e++)
            {
                bool present = false;
                for (int i = 0; i < c.Count && !present; i++) present = c.Rows[i].Element == e;
                string name = present && c.Wing != null && !c.Client ? c.Wing.Roster.Name(e) : ElementRoster.Letter(e);
                if (present != chipInUse[e] || !ReferenceEquals(name, chipNames[e])) changed = true;
                chipInUse[e] = present;
                chipNames[e] = name;
                if (present) used++;
            }
            if (changed) PlaceChips(used);
            for (int e = 0; e < elementChips.Length; e++)
                if (chipInUse[e]) elementChips[e].SetLatched(c.Scope.Kind == ScopeKind.Element && c.Scope.Element == e);
        }

        private string scopeLabelShown;

        /// <summary>Element chips share what ALL and [+] leave; a chip too narrow for "B · VIPER" shows its letter (the name is
        /// in the tooltip).</summary>
        private void PlaceChips(int used)
        {
            float y = body.y - 2f, h = BezelLayout.ScopeRow - 4f;
            float start = x + 198f + AllWidth + ChipGap, end = x + width - 2f - PresetWidth - ChipGap;
            float w = used > 0 ? (end - start - ChipGap * (used - 1)) / used : 0f;
            int k = 0;
            for (int e = 0; e < elementChips.Length; e++)
            {
                AvButton chip = elementChips[e];
                chip.gameObject.SetActive(chipInUse[e]);
                if (!chipInUse[e]) continue;
                AvKit.Place((RectTransform)chip.transform, new Rect(start + k * (w + ChipGap), y, w, h));
                string letter = ElementRoster.Letter(e), name = chipNames[e];
                bool named = !string.IsNullOrEmpty(name) && name != letter;
                string full = named ? letter + " · " + name : letter;
                // ~6.5 px a character at 10 px bold; the button keeps a 12 px margin.
                chip.SetText(full.Length * 6.5f <= w - 12f ? full : letter);
                chip.WithTooltip("Orders go to element " + full + ".");
                k++;
            }
        }

        private static int ScopeCount(WmcContext c)
        {
            switch (c.Scope.Kind)
            {
                case ScopeKind.Members: return c.Scope.Members?.Length ?? 0;
                case ScopeKind.Element:
                    int n = 0;
                    for (int i = 0; i < c.Count; i++)
                        if (c.Rows[i].Element == c.Scope.Element) n++;
                    return n;
                default: return c.Count;
            }
        }

        private static bool InScope(WmcContext c, in SnapshotMember m) => c.InScope(m);

        // ---------------------------------------------------------------- refresh

        public void Refresh(WmcContext c)
        {
            last = c;
            RefreshScope(c);
            RefreshList(c);
            if (sub == SubOrders) RefreshOrders(c);
            else if (sub == SubForm) RefreshFormation(c);
            else if (sub == SubRoute) RefreshRoute(c);
        }

        public void Metrics(WmcContext c, WmcMetricRow m)
        {
            WingSummary s = WingRows.Summary(c.Rows, c.Count);
            float bingo = float.NaN;
            int winchester = 0, defending = 0, fighting = 0;
            for (int i = 0; i < c.Count; i++)
            {
                if ((c.Rows[i].Flags & (byte)SnapshotFlags.Winchester) != 0) winchester++;
                if ((MemberDuty)c.Rows[i].Duty == MemberDuty.Defending) defending++;
                if ((MemberDuty)c.Rows[i].Duty == MemberDuty.Engaged) fighting++;
            }
            if (c.Wing != null && !c.Client)
                foreach (WingMember w in c.Wing.Members)
                    if (!w.Released && w.Alive && !float.IsNaN(w.Bingo.SecondsToBingo) && (float.IsNaN(bingo) || w.Bingo.SecondsToBingo < bingo))
                        bingo = w.Bingo.SecondsToBingo;
            int hostiles = c.Wing != null && !c.Client ? c.Wing.HostilesNear() : -1;
            int key = Pack(s.MinFuel) * 7 + Pack(s.MinAmmo) * 131 + (float.IsNaN(bingo) ? -1 : (int)bingo) * 1009 + winchester * 17
                + defending * 29 + fighting * 37 + hostiles * 41 + (s.Bingo ? 3 : 0);
            if (key == metricKey && m.Generation == metricGeneration) return;
            metricKey = key;
            metricGeneration = m.Generation;
            m.Set(0, WmcText.Percent(s.MinFuel).TrimEnd('%'),
                s.Bingo ? "BINGO" : float.IsNaN(bingo) ? "" : "BINGO IN " + WmcText.Clock(bingo),
                WingRows.Bar(s.MinFuel), WmcUi.LevelColor(WmcStyle.Level(s.MinFuel)));
            m.Set(1, WmcText.Percent(s.MinAmmo).TrimEnd('%'), winchester > 0 ? winchester + " WINCHESTER" : "",
                WingRows.Bar(s.MinAmmo), WmcUi.LevelColor(WmcStyle.Level(s.MinAmmo)));
            string threat = hostiles < 0 ? WmcText.Unknown : hostiles == 0 ? "CLEAR" : hostiles.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string caption = defending > 0 ? defending + " DEFENDING" : fighting > 0 ? fighting + " FIGHTING" : "";
            m.Set(2, threat, caption, hostiles > 0 ? 1f : 0f, hostiles > 0 ? AvTheme.Alert : AvTheme.Friendly);
        }

        private int metricKey = int.MinValue, metricGeneration = -1;

        private static int Pack(float fraction) => float.IsNaN(fraction) ? -1 : (int)(fraction * 1000f);
    }
}
