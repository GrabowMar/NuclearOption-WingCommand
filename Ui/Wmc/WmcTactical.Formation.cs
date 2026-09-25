using System.Collections.Generic;
using System.Globalization;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>TACTICAL › FORMATION (spec WMC rebuild §FORMATION; the 0.9 deck modernized): the plan view of the scope's shape
    /// with the members' live positions, the family and shapes for the scope's element, the maneuver row, then what is
    /// wing-wide — spacing, stack and power — under its own head so scope is never ambiguous.</summary>
    internal sealed partial class WmcTactical
    {
        private const int MaxShapes = 12, MaxFamilies = 4, MaxDots = WcSnapshot.MaxMembers;
        private const float PreviewH = 150f, PlanSize = 140f;

        private WmcScroll formScroll;
        private RectTransform planRect;
        private readonly Image[] slotDots = new Image[MaxDots], liveDots = new Image[MaxDots];
        private readonly TMP_Text[] formLines = new TMP_Text[4];
        private TMP_Text shapeHead;
        private readonly AvButton[] familyButtons = new AvButton[MaxFamilies];
        private readonly AvButton[] shapeButtons = new AvButton[MaxShapes];
        private readonly string[] shapeIds = new string[MaxShapes];
        private readonly List<FormationDefinition> shapes = new List<FormationDefinition>();
        private readonly List<string> families = new List<string>();
        private SegmentRow spacingRow, stackRow, powerRow, maneuverRow;
        private int formKey = int.MinValue, shapesKey = int.MinValue;

        private void BuildFormation(RectTransform root)
        {
            formScroll = WmcScroll.Build(root, new Rect(x, -BannerTop, width + 8f, 100f), "FormScroll");
            RectTransform s = formScroll.Content;
            float w = formScroll.Width, y = 0f;

            AvStyled.Box(s, new Rect(0f, y, w, PreviewH), "card");
            var go = new GameObject("PlanView", typeof(RectTransform));
            planRect = (RectTransform)go.transform;
            planRect.SetParent(s, false);
            AvKit.Place(planRect, new Rect(6f, y - 5f, PlanSize, PlanSize));
            AvKit.Panel(planRect, new Rect(0f, 0f, PlanSize, PlanSize), AvTheme.SurfaceInert).raycastTarget = false;
            AvKit.Rule(planRect, new Rect(PlanSize * 0.5f, 0f, 1f, PlanSize), AvTheme.Hairline);
            AvKit.Rule(planRect, new Rect(0f, -PlanSize / 3f, PlanSize, 1f), AvTheme.Hairline);
            // The leader's mark and label are drawn (the MFD font has no ▲ ○ ●: they showed as boxes at stop 1).
            AvKit.Panel(planRect, new Rect(PlanSize * 0.5f - 3f, -PlanSize / 3f + 3f, 6f, 6f), AvTheme.TextPrimary).raycastTarget = false;
            AvStyled.Label(planRect, new Rect(PlanSize * 0.5f + 5f, -PlanSize / 3f + 14f, 30f, 12f), "LDR", "row-sub");
            for (int i = 0; i < MaxDots; i++)
            {
                slotDots[i] = AvKit.Panel(planRect, new Rect(0f, 0f, 8f, 8f), AvTheme.Hairline);
                slotDots[i].raycastTarget = false;
                liveDots[i] = AvKit.Panel(planRect, new Rect(0f, 0f, 5f, 5f), AvTheme.Friendly);
                liveDots[i].raycastTarget = false;
            }
            float tx = PlanSize + 16f, tw = w - tx - 8f;
            for (int i = 0; i < formLines.Length; i++)
                formLines[i] = WmcKit.Text(s, new Rect(tx, y - 8f - i * 22f, tw, 18f), i == 0 ? "row-name" : "row-sub");
            // The legend: the plan view's own marks, drawn.
            float ly = y - 8f - formLines.Length * 22f;
            AvKit.Panel(s, new Rect(tx, ly - 5f, 8f, 8f), AvTheme.Hairline).raycastTarget = false;
            WmcKit.Text(s, new Rect(tx + 12f, ly, 40f, 18f), "row-sub").text = "SLOT";
            AvKit.Panel(s, new Rect(tx + 58f, ly - 6f, 5f, 5f), AvTheme.Friendly).raycastTarget = false;
            WmcKit.Text(s, new Rect(tx + 68f, ly, 40f, 18f), "row-sub").text = "LIVE";
            AvButton edit = AvStyled.Button(s, new Rect(w - 126f, y - PreviewH + 30f, 120f, 22f), "EDIT SHAPES ›", "btn", null);
            edit.SetEnabled(false);
            edit.WithTooltip("The shape editor lives in the planning room's WORKSHOP. Arrives in a later update.");
            ids["tac.form.edit"] = edit;
            y -= PreviewH + 6f;

            shapeHead = WmcKit.Text(s, new Rect(0f, y, w, 16f), "section-title");
            y -= 20f;
            float fw = (w - KeyWidth - WmcUi.Gap * (MaxFamilies - 1)) / MaxFamilies;
            AvStyled.Label(s, new Rect(0f, y, KeyWidth, ToggleH), "FAMILY", "metric-key");
            for (int i = 0; i < MaxFamilies; i++)
            {
                int k = i;
                familyButtons[i] = AvStyled.Button(s, new Rect(KeyWidth + i * (fw + WmcUi.Gap), y, fw, ToggleH), "", "btn",
                    () => PickFamily(k), AvButtonStyle.Toggle);
                ids["tac.form.family" + i] = familyButtons[i];
            }
            y -= TogglePitch;
            AvStyled.Label(s, new Rect(0f, y, KeyWidth, ToggleH), "SHAPE", "metric-key");
            for (int i = 0; i < MaxShapes; i++)
            {
                int k = i;
                shapeButtons[i] = AvStyled.Button(s, new Rect(KeyWidth + (i % 4) * (fw + WmcUi.Gap), y - (i / 4) * TogglePitch, fw, ToggleH), "",
                    "btn", () => PickShape(k), AvButtonStyle.Toggle);
                ids["tac.form.shape" + i] = shapeButtons[i];
            }
            y -= 3 * TogglePitch;
            maneuverRow = SegmentRow.Build(s, new Rect(0f, y, w, ToggleH), KeyWidth, "MANEUVER",
                new[] { "BRK L", "BRK R", "PULL UP", "SPLIT", "BEAM" },
                new[]
                {
                    "Hard turn 90° left, then back to the slot.", "Hard turn 90° right, then back to the slot.",
                    "Climb 500 m straight on, then back to the slot.", "Turn 60° apart: a pair splits, one aircraft turns away from the lead.",
                    "Turn across the nearest air threat's line of sight (from the wing's tracks), then back to the slot.",
                }, "tac.form.", new[] { "brkl", "brkr", "pullup", "split", "beam" }, ids,
                i => WmcUi.Order(last, () => WingOrders.Run(new WingOrder { Kind = OrderKind.Maneuver, Number = i, Scope = last.Scope })));
            y -= TogglePitch + 6f;

            AvStyled.Label(s, new Rect(0f, y, w, 16f), "WHOLE WING", "section-title");
            y -= 20f;
            spacingRow = SegmentRow.Build(s, new Rect(0f, y, w, ToggleH), KeyWidth, "SPACING", new[] { "CLOSE", "STANDARD", "OPEN", "SPREAD" },
                new[] { "40 m between slots.", "80 m between slots.", "160 m between slots.", "350 m between slots." },
                "tac.form.spacing", new[] { "0", "1", "2", "3" }, ids,
                i => WmcUi.Order(last, () => WingCommands.SetSpacing((SpacingPreset)i)));
            y -= TogglePitch;
            stackRow = SegmentRow.Build(s, new Rect(0f, y, w, ToggleH), KeyWidth, "STACK", new[] { "HIGH", "LEVEL", "LOW" },
                new[] { "The wing flies above you.", "Level with you.", "The wing flies below you." },
                "tac.form.", new[] { "high", "level", "low" }, ids, PickStack);
            y -= TogglePitch;
            powerRow = SegmentRow.Build(s, new Rect(0f, y, w, ToggleH), KeyWidth, "POWER", new[] { "BUSTER", "GATE" },
                new[] { "Full power, no afterburner.", "Afterburner allowed." }, "tac.form.", new[] { "buster", "gate" }, ids,
                i => WmcUi.Order(last, () => WingCommands.Afterburner(i == 1)));
            y -= TogglePitch;
            formScroll.SetContentHeight(-y + 4f);
        }

        private void LayoutFormation(float region)
        {
            formScroll?.SetViewport(new Rect(x, -BannerTop, width + 8f, Mathf.Max(40f, region - BannerTop)));
        }

        private void PickFamily(int i)
        {
            if (i >= families.Count) return;
            WmcUi.Order(last, () =>
            {
                FormationDefinition first = shapes.Find(d => d.Family == families[i]);
                if (first != null) WingOrders.Run(new WingOrder { Kind = OrderKind.SetShape, Text = first.Id, Scope = last.Scope });
            });
        }

        private void PickShape(int i)
        {
            string id = shapeIds[i];
            if (id == null) return;
            WmcUi.Order(last, () => WingOrders.Run(new WingOrder { Kind = OrderKind.SetShape, Text = id, Scope = last.Scope }));
        }

        private void PickStack(int i) => WmcUi.Order(last, () =>
        {
            switch (i)
            {
                case 0: WingCommands.Stack(WingCommands.GoHighMetres, "Going high"); break;
                case 1: WingCommands.Stack(0f, "Level with you"); break;
                default: WingCommands.Stack(WingCommands.GoLowMetres, "Going low"); break;
            }
        });

        private void RefreshFormation(WmcContext c)
        {
            WingService w = c.Wing;
            FormationSelection sel = w?.Selection;
            bool host = w != null && !c.Client && sel != null;
            for (int i = 0; i < familyButtons.Length; i++) familyButtons[i].SetEnabled(host && c.CanOrder);
            if (!host)
            {
                if (formKey != -2)
                {
                    formKey = -2;
                    formLines[0].text = "Formation is the host's";
                    for (int i = 1; i < formLines.Length; i++) formLines[i].text = "";
                    shapeHead.text = "SHAPE";
                }
                return;
            }
            int e = c.ScopeElement;
            FormationDefinition current = w.ShapeOf(e) ?? sel.Current;
            string elementName = w.Roster.Name(e);
            int members = 0;
            for (int i = 0; i < c.Count; i++)
                if (c.Rows[i].Element == e) members++;
            float stack = w.Stack;
            int stackWord = stack > 1f ? 0 : stack < -1f ? 2 : 1;
            int key = (current.Id?.GetHashCode() ?? 0) * 31 + (int)sel.Spacing * 7 + stackWord * 3 + (w.AfterburnerAllowed ? 1 : 0)
                + e * 1009 + members * 101 + (elementName?.GetHashCode() ?? 0);
            if (key != formKey)
            {
                formKey = key;
                formLines[0].text = current.Name.ToUpperInvariant() + " · " + current.Family.ToUpperInvariant();
                formLines[1].text = "SPACING " + sel.Spacing.ToString().ToUpperInvariant() + " · "
                    + Mathf.RoundToInt(sel.SpacingMetres).ToString(CultureInfo.InvariantCulture) + " m";
                formLines[2].text = "STACK " + (stackWord == 0 ? "HIGH" : stackWord == 2 ? "LOW" : "LEVEL") + " · "
                    + (w.AfterburnerAllowed ? "GATE" : "BUSTER");
                formLines[3].text = "ELEMENT " + ElementRoster.Letter(e) + (elementName != ElementRoster.Letter(e) ? " " + elementName : "")
                    + " · " + members.ToString(CultureInfo.InvariantCulture) + " AC";
                shapeHead.text = "SHAPE · ELEMENT " + ElementRoster.Letter(e);
            }
            RefreshShapes(sel, current);
            spacingRow.Set((int)sel.Spacing);
            spacingRow.SetEnabled(c.CanOrder, "Orders are host only for now");
            stackRow.Set(stackWord);
            stackRow.SetEnabled(c.CanOrder, "Orders are host only for now");
            powerRow.Set(w.AfterburnerAllowed ? 1 : 0);
            powerRow.SetEnabled(c.CanOrder, "Orders are host only for now");
            // A maneuver is a one-shot order: nothing stays latched.
            bool flying = false;
            for (int i = 0; i < c.Count && !flying; i++)
                flying = c.InScope(c.Rows[i]) && (MemberDuty)c.Rows[i].Duty == MemberDuty.Formation;
            maneuverRow.Set(-1);
            maneuverRow.SetEnabled(c.CanOrder && flying, !c.CanOrder ? "Orders are host only for now" : "Nobody in scope is flying in formation");
            RefreshPlanView(c, current, sel.SpacingMetres, e, members);
        }

        private void RefreshShapes(FormationSelection sel, FormationDefinition current)
        {
            // The suitable list follows the wing's shape use (jet, rotary, escort): the wing's own shape names it.
            int key = (current.Id?.GetHashCode() ?? 0) * 31 + (sel.Current?.Id?.GetHashCode() ?? 0);
            if (key == shapesKey) return;
            shapesKey = key;
            sel.Suitable(shapes);
            FormationSelection.Families(shapes, families);
            for (int i = 0; i < MaxFamilies; i++)
            {
                bool on = i < families.Count;
                familyButtons[i].gameObject.SetActive(on);
                if (!on) continue;
                familyButtons[i].SetText(families[i].ToUpperInvariant());
                familyButtons[i].SetLatched(families[i] == current.Family);
            }
            int n = 0;
            foreach (FormationDefinition d in shapes)
            {
                if (d.Family != current.Family || n >= MaxShapes) continue;
                shapeIds[n] = d.Id;
                shapeButtons[n].SetText(WmcWords.Shape(d.Id, d.Name));
                shapeButtons[n].SetLatched(d.Id == current.Id);
                shapeButtons[n].WithTooltip(d.Name + ": this element's shape.");
                shapeButtons[n++].gameObject.SetActive(true);
            }
            for (int i = n; i < MaxShapes; i++)
            {
                shapeIds[i] = null;
                shapeButtons[i].gameObject.SetActive(false);
            }
        }

        /// <summary>Slots from the shape at the wing's spacing; live dots are each member's offset from its element's leader —
        /// the task lead while the element has a task, else A's anchor or you — in the leader's heading frame.</summary>
        private void RefreshPlanView(WmcContext c, FormationDefinition shape, float spacing, int e, int members)
        {
            PlanView.Fit(shape.Slots, spacing, PlanSize, out float mpp);
            int slots = Mathf.Min(shape.Slots.Length, Mathf.Min(members, MaxDots));
            for (int i = 0; i < MaxDots; i++)
            {
                bool on = i < slots;
                if (slotDots[i].gameObject.activeSelf != on) slotDots[i].gameObject.SetActive(on);
                if (!on) continue;
                var (px, py) = PlanView.Point(shape.Slots[i].Right * spacing, shape.Slots[i].Aft * spacing, mpp, PlanSize);
                slotDots[i].rectTransform.anchoredPosition = new Vector2(px - 4f, -py + 4f);
            }

            bool haveLeader = LeaderFrame(c.Wing, e, out Vec3 at, out float heading);
            Vec3 fwd = Vec3.FromHeading(heading), right = new Vec3(fwd.Z, 0f, -fwd.X);
            int k = 0;
            if (haveLeader)
                foreach (WingMember m in c.Wing.Members)
                {
                    if (k >= MaxDots) break;
                    if (m.Released || !m.Alive || (object)m.Aircraft == null || c.Wing.ElementOf(m) != e) continue;
                    Vec3 d = m.Last.Pos - at;
                    var (px, py) = PlanView.Point(Vec3.Dot(d, right), -Vec3.Dot(d, fwd), mpp, PlanSize);
                    Image dot = liveDots[k++];
                    if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
                    dot.rectTransform.anchoredPosition = new Vector2(px - 2.5f, -py + 2.5f);
                    dot.color = c.Selection.Contains(m.Aircraft.persistentID.Id) ? AvTheme.Accent : AvTheme.Friendly;
                }
            for (; k < MaxDots; k++)
                if (liveDots[k].gameObject.activeSelf) liveDots[k].gameObject.SetActive(false);
        }

        private static bool LeaderFrame(WingService w, int e, out Vec3 at, out float heading)
        {
            at = Vec3.Zero;
            heading = 0f;
            // Review R2 I2: A forms on its task lead while it has a task, else on its anchor (FORM ON / ESCORT) or you.
            if (e == 0 && !w.Planner.Active)
            {
                Unit u = w.LeaderUnit != null ? w.LeaderUnit : w.Player;
                if (u == null) return false;
                at = u.GlobalPosition().ToVec3();
                Vector3 f = u.transform.forward;
                heading = Vec3.HeadingDeg(new Vec3(f.x, 0f, f.z));
                return true;
            }
            WingPlanner planner = e == 0 ? w.Planner : w.Roster.InUse(e) ? w.PlannerOf(e) : null;
            if (planner?.Lead == null) return false;
            at = planner.Lead.Position;
            heading = planner.Lead.HeadingDeg;
            return true;
        }
    }
}
