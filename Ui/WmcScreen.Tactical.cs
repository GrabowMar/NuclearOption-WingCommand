using System;
using System.Collections.Generic;
using NuclearOption.SavedMission;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using NOAvionics.Ui;

namespace WingCommand
{
    /// <summary>The WMC panel's TACTICAL tab: rules of engagement, weapon preference, the order grid, and the flight roster.</summary>
    internal static partial class WmcScreen
    {
        /// <summary>
        /// The three standing choices that shape a fight, in one labelled block: what a
        /// wingman may shoot, which of its own weapons it reaches for, and where it sits.
        ///
        /// Grouped under one heading with a left gutter rather than given a heading each.
        /// Three headings and a hint line cost sixty pixels of a panel that now shares its
        /// bezel with four tabs, and they were labelling three rows that all answer the
        /// same question — how does this flight fight. The per-choice explanations moved to
        /// the status line at the foot of the page, where only the one being changed is
        /// shown and it has the width to be a sentence.
        /// </summary>
        private static float AddEngagementSection(RectTransform parent, float y)
        {
            y = Heading(parent, y, "ENGAGEMENT");

            float left = Pad + GutterWidth;
            float w = PanelWidth - Pad - left;

            // Rules of engagement: three rungs, so three buttons. They are an escalation
            // rather than a toggle — each answers "the leader is being shot at" differently,
            // which is the whole reason there are three of them. Wing-wide.
            Gutter(parent, y, "ROE");
            float roeWidth = (w - Gap * 2f) / 3f;
            // Each rung explains itself from the same source the status line already used
            // for the standing hint, so the hovered description and the resting one cannot
            // drift apart.
            holdButton = Button(parent, "HOLD", new Rect(left, y, roeWidth, RowHeight),
                                () => SetRoe(WingRoe.Hold))
                         .WithTooltip("HOLD - " + RoeRules.Hint(WingRoe.Hold));
            tightButton = Button(parent, "TIGHT",
                                  new Rect(left + roeWidth + Gap, y, roeWidth, RowHeight),
                                  () => SetRoe(WingRoe.Tight))
                           .WithTooltip("TIGHT - " + RoeRules.Hint(WingRoe.Tight));
            freeButton = Button(parent, "FREE",
                                new Rect(left + (roeWidth + Gap) * 2f, y, roeWidth, RowHeight),
                                () => SetRoe(WingRoe.Free))
                         .WithTooltip("FREE - " + RoeRules.Hint(WingRoe.Free));
            y -= RowHeight + Gap;

            // Weapon preference. Unlike the two rows around it this one is scoped to the
            // current selection, which is what makes a mixed flight possible: two wingmen
            // holding their missiles for aircraft while the third works the ground.
            Gutter(parent, y, "WEAPON");
            float preferenceWidth = (w - Gap * (preferenceButtons.Length - 1)) / preferenceButtons.Length;
            for (int i = 0; i < preferenceButtons.Length; i++)
            {
                WingWeaponPreference preference = WingWeaponPreferences.All[i];
                preferenceButtons[i] = WingUi.Button(
                    parent, WingWeaponPreferences.Label(preference),
                    new Rect(left + (preferenceWidth + Gap) * i, y, preferenceWidth, RowHeight),
                    FontSmall,
                    () => WingCommandManager.Instance?.SetWeaponPreference(preference))
                    .WithTooltip(WingWeaponPreferences.Label(preference) + " - " +
                                 WingWeaponPreferences.Hint(preference) +
                                 " Applies to the selected wingmen only.");
            }
            y -= RowHeight + Gap;
            return y;
        }


        private static void SetRoe(WingRoe roe)
        {
            WingRegistry wing = Wing();
            if (wing == null) return;

            wing.Roe = roe;
            WingCommandManager.Instance?.Toast("ROE: " + RoeRules.Label(roe));
        }

        private static float AddSummary(RectTransform parent, float y)
        {
            float w = PanelWidth - Pad * 2f;
            WingUi.TacticalCard(parent, new Rect(Pad, y, w, RowHeight), WingUi.RailEmerald);

            const float actionWidth = WingUi.ButtonAction;
            summaryLabel = Label(parent, "",
                                 new Rect(Pad + Space3, y, w - actionWidth - Space4,
                                          RowHeight),
                                 Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            WingUi.Button(parent, "SELECT ALL",
                          new Rect(PanelWidth - Pad - actionWidth - Space1, y - 2f, actionWidth, RowHeight - 4f),
                          FontMicro, UiButtonStyle.Primary,
                          () => WingCommandManager.Instance?.SelectAllMembers())
                .WithTooltip(OrderHint.SelectAll);
            return y - RowHeight - Space2;
        }

        private static float AddRosterArea(RectTransform parent, float y)
        {
            y = Heading(parent, y, "FLIGHT");

            // Column headers, so the numbers in each row are readable without guessing.
            float w = PanelWidth - Pad * 2f;
            y = ColumnHeaders(parent, y, RosterColumns);

            float h = RowPitch * RosterRowsPerPage;

            var area = new GameObject("Roster", typeof(RectTransform));
            rosterArea = area.GetComponent<RectTransform>();
            rosterArea.SetParent(parent, worldPositionStays: false);
            Place(rosterArea, new Rect(Pad, y, w, h));

            rosterEmptyLabel = EmptyNote(rosterArea,
                "No wingmen. Requisition aircraft on the SUPPLY tab, or ASSIGN a friendly " +
                "AI aircraft selected on the map.");

            y -= h + Gap;

            rosterPrevButton = Pager(parent, y, "<", () => TurnRosterPage(-1));
            rosterPageLabel = PagerLabel(parent, y);
            rosterNextButton = Pager(parent, y, ">", () => TurnRosterPage(1));
            return y - RowHeight - Gap;
        }


        /// <summary>
        /// The scoped orders, grouped by what the player is trying to accomplish.
        ///
        /// Target work comes first, autonomous combat follows, and point orders stay
        /// together. RTB and Refit share a final row for recovery or immediate turnaround.
        /// </summary>
        private static float AddActions(RectTransform parent, float y)
        {
            y = Heading(parent, y, "ORDERS - SELECTED SCOPE");
            float w = (PanelWidth - Pad * 2f - Gap * 2f) / 3f;

            // Short labels that read as a set — Attack / Splash / Engage / Disengage —
            // instead of the old jokey "Splash 'Em" sitting next to plain "Attack". The
            // full sentence for each still lands on the status strip on hover.
            y = Triple(parent, y, w,
                "Form Up", OrderHint.Rejoin, () => Order(WingAction.Rejoin),
                "Attack", OrderHint.Attack, () => Order(WingAction.AttackMyTarget),
                "Splash", OrderHint.FireForEffect, () => Order(WingAction.FireForEffect));

            GridButton(parent, "Engage", Pad, y, w,
                       () => Order(WingAction.Engage)).WithTooltip(OrderHint.Engage);
            GridButton(parent, "Disengage", Pad + w + Gap, y, w,
                       () => Order(WingAction.FallBack)).WithTooltip(OrderHint.Disengage);
            jamButton = GridButton(parent, "Jam", Pad + (w + Gap) * 2f, y, w,
                                   () => Order(WingAction.JamMyTarget))
                        .WithTooltip(OrderHint.Jam);
            y -= RowHeight + Gap;

            GridButton(parent, "Hold", Pad, y, w,
                       () => WingCommandManager.Instance?.ArmPointOrder(WingOrder.OrbitHere))
                .WithTooltip(OrderHint.HoldHere);

            // Deliver Cargo arms a drop point, and says on the status line that pressing it
            // again falls back to the stock supply route.
            cargoButton = GridButton(parent, "Cargo", Pad + w + Gap, y, w,
                                     () => WingCommandManager.Instance?.RequestCargoRun())
                          .WithTooltip(OrderHint.DeliverCargo);
            landButton = GridButton(parent, "Land", Pad + (w + Gap) * 2f, y, w,
                                    () => WingCommandManager.Instance?.ArmPointOrder(WingOrder.LandHere))
                         .WithTooltip(OrderHint.LandHere);
            y -= RowHeight + Gap;

            GridButton(parent, "RTB", Pad, y, w,
                       () => Order(WingAction.ReturnToBase))
                .WithTooltip(OrderHint.ReturnToBase);
            GridButton(parent, "Refit", Pad + w + Gap, y, w,
                       () => Order(WingAction.Refit))
                .WithTooltip("REFIT - land at base, refill fuel and ammunition, then relaunch and rejoin.");
            y -= RowHeight + Gap;

            y = AddFormationAndDoctrine(parent, y);

            return y;
        }

        private static TMP_Text doctrineTitleLabel;
        private static TMP_Text doctrineProfileLabel;
        private static TMP_Text doctrineRulesLabel;
        private static TMP_Text doctrineWeaponsLabel;
        private static WingButton[] formationButtons;
        private static WingButton[] maneuverButtons;
        private static float formationRadarCenterY;
        private static readonly List<RectTransform> formationWingmenDots = new List<RectTransform>();
        private static readonly List<Image> formationVectorLines = new List<Image>();

        private static string ShortFormationName(FormationShape shape)
        {
            switch (shape)
            {
                case FormationShape.EchelonRight: return "ECH R";
                case FormationShape.EchelonLeft:  return "ECH L";
                case FormationShape.LineAbreast:  return "ABREAST";
                case FormationShape.Trail:        return "TRAIL";
                case FormationShape.CombatSpread: return "SPREAD";
                case FormationShape.FingerFour:   return "FINGER 4";
                case FormationShape.Vic:          return "VIC";
                case FormationShape.Diamond:      return "DIAMOND";
                case FormationShape.Ladder:       return "LADDER";
                case FormationShape.Wall:         return "WALL";
                default: return shape.ToString().ToUpperInvariant();
            }
        }

        private static float AddFormationAndDoctrine(RectTransform parent, float y)
        {
            y = Heading(parent, y, "FORMATION & COMBAT DOCTRINE");

            float w = PanelWidth - Pad * 2f;
            const float radarW = 108f;
            const float boxH = 104f;

            // --- Left: Formation Radar Visualizer ---
            WingUi.TacticalCard(parent, new Rect(Pad, y, radarW, boxH), WingUi.RailEmerald);

            // Crosshairs with subtle emerald glow
            Color crosshairCol = new Color(WingUi.RailEmerald.r, WingUi.RailEmerald.g, WingUi.RailEmerald.b, 0.25f);
            Rule(parent, new Rect(Pad + radarW * 0.5f, y - 6f, 1f, boxH - 28f), crosshairCol);
            Rule(parent, new Rect(Pad + 6f, y - (boxH - 20f) * 0.5f, radarW - 12f, 1f), crosshairCol);

            float radarCenterX = Pad + radarW * 0.5f;
            formationRadarCenterY = y - 22f;

            // Leader indicator at center
            Label(parent, "▲", new Rect(radarCenterX - 10f, formationRadarCenterY - 6f, 20f, 16f),
                  Green(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
            Label(parent, "LDR", new Rect(radarCenterX - 15f, formationRadarCenterY + 10f, 30f, 10f),
                  Green(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);

            // 3 Wingmen indicators and connecting lines
            formationWingmenDots.Clear();
            formationVectorLines.Clear();

            for (int i = 0; i < 3; i++)
            {
                var line = Rule(parent, new Rect(radarCenterX, formationRadarCenterY, 1f, 1f),
                                new Color(WingUi.RailEmerald.r, WingUi.RailEmerald.g, WingUi.RailEmerald.b, 0.35f));
                formationVectorLines.Add(line);

                var dotGo = new GameObject("WingmanDot_" + i, typeof(RectTransform));
                var rt = dotGo.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Label(rt, "▲", new Rect(0f, 0f, 16f, 16f), Friendly(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);
                Place(rt, new Rect(radarCenterX, formationRadarCenterY, 16f, 16f));
                formationWingmenDots.Add(rt);
            }

            // --- Right: AI Combat Doctrine Telemetry ---
            float docX = Pad + radarW + Gap;
            float docW = w - radarW - Gap;

            WingUi.TacticalCard(parent, new Rect(docX, y, docW, boxH), WingUi.RailCyan);

            float lineY = y - 4f;
            doctrineTitleLabel = Label(parent, "COMBAT DOCTRINE",
                new Rect(docX + Space2, lineY, docW - Space3, 16f),
                Green(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
            lineY -= 18f;

            doctrineProfileLabel = Label(parent, "",
                new Rect(docX + Space2, lineY, docW - Space3, 24f),
                Friendly(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            doctrineProfileLabel.enableWordWrapping = true;
            lineY -= 26f;

            doctrineRulesLabel = Label(parent, "",
                new Rect(docX + Space2, lineY, docW - Space3, 24f),
                Green(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            doctrineRulesLabel.enableWordWrapping = true;
            lineY -= 26f;

            doctrineWeaponsLabel = Label(parent, "",
                new Rect(docX + Space2, lineY, docW - Space3, 24f),
                Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            doctrineWeaponsLabel.enableWordWrapping = true;

            y -= boxH + Space2;

            // --- Grid of all available formations (small buttons under formation preview) ---
            const int cols = 5;
            const float btnH = 22f;
            float btnW = (w - (cols - 1) * Gap) / cols;
            formationButtons = new WingButton[FormationShapes.All.Length];

            for (int i = 0; i < FormationShapes.All.Length; i++)
            {
                FormationShape shape = FormationShapes.All[i];
                int col = i % cols;
                int row = i / cols;
                float bx = Pad + col * (btnW + Gap);
                float by = y - row * (btnH + Gap);

                formationButtons[i] = WingUi.Button(
                    parent, ShortFormationName(shape),
                    new Rect(bx, by, btnW, btnH),
                    FontMicro, UiButtonStyle.Toggle,
                    () => SetFormationShape(shape))
                    .WithTooltip(FormationShapes.Pretty(shape) + " formation geometry");
            }

            int rows = Mathf.CeilToInt(FormationShapes.All.Length / (float)cols);
            y -= rows * btnH + (rows - 1) * Gap + Space2;

            // --- Combat manoeuvres: transient moves flown once, then the wing rejoins ---
            y -= Space1;
            Label(parent, "COMBAT MANOEUVRES",
                  new Rect(Pad, y, w, Space4),
                  Dim(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);
            y -= Space4 + Space1;

            const int maneuverCols = 5;
            float maneuverBtnW = (w - (maneuverCols - 1) * Gap) / maneuverCols;
            maneuverButtons = new WingButton[ManeuverCatalog.All.Length];

            for (int i = 0; i < ManeuverCatalog.All.Length; i++)
            {
                ManeuverKind kind = ManeuverCatalog.All[i];
                int col = i % maneuverCols;
                int row = i / maneuverCols;
                float bx = Pad + col * (maneuverBtnW + Gap);
                float by = y - row * (btnH + Gap);

                maneuverButtons[i] = WingUi.Button(
                    parent, ManeuverCatalog.ShortLabel(kind),
                    new Rect(bx, by, maneuverBtnW, btnH),
                    FontMicro,
                    () => WingCommandManager.Instance?.ExecuteManeuver(kind, wholeWing: false))
                    .WithTooltip(ManeuverCatalog.Label(kind) + " - the selected wingmen fly this, then rejoin.");
            }

            int maneuverRows = Mathf.CeilToInt(ManeuverCatalog.All.Length / (float)maneuverCols);
            return y - (maneuverRows * btnH + (maneuverRows - 1) * Gap + Space2);
        }

        private static void SetFormationShape(FormationShape shape)
        {
            WingFormation.Shape = shape;
            WingCommandManager manager = WingCommandManager.Instance;
            if (manager != null)
            {
                WingRegistry wing = manager.Wing;
                if (wing != null) RefreshTactical(wing);
            }
        }

        /// <summary>
        /// What each control does, in the two lines the status strip has room for.
        ///
        /// Kept together rather than written at each call site: these are the panel's
        /// documentation, they have to stay consistent in voice and length, and several of
        /// them are the only place a distinction is ever explained — Attack versus Fire For
        /// Effect, or Disengage versus Return To Base, are not differences a four-word
        /// button label can carry.
        /// </summary>
        private static class OrderHint
        {
            public const string Rejoin =
                "FORM UP - break off and return to formation on the leader. Cancels any " +
                "attack, hold or route the selected wingmen are flying.";

            public const string Attack =
                "ATTACK - send the selection after the target you have locked. Targets are " +
                "shared out across the scope so several wingmen do not chase one contact.";

            public const string FireForEffect =
                "SPLASH - empty everything that will bear on your locked target. " +
                "Expends ordnance freely; use it to finish something, not to open on it.";

            public const string Engage =
                "ENGAGE - hunt independently within the rules of engagement. The wingman " +
                "picks its own targets and does not come back until told to.";

            public const string Disengage =
                "DISENGAGE - break contact and run for the nearest friendly base or ship, " +
                "defending itself on the way. Not a landing order.";

            public const string HoldHere =
                "HOLD HERE - then click the map. The selection orbits that point and " +
                "defends itself, but starts nothing.";

            public const string ReturnToBase =
                "RETURN TO BASE - fly home and land. The airframe and its fit go back into " +
                "the wing reserve, ready to be requisitioned again.";

            public const string DeliverCargo =
                "DELIVER CARGO - then click a drop point, or press again to use the stock " +
                "supply route. Only wingmen actually carrying a load can take this.";

            public const string LandHere =
                "LAND HERE - then click the map. Puts a rotary wingman on the ground at " +
                "that spot rather than routing it to an airbase.";

            public const string SelectAll =
                "Put every wingman in the command scope, so the next order goes to the " +
                "whole flight.";

            public const string Release =
                "Discharge this wingman from the wing for good. It flies home, gives its " +
                "airframe back and stops using a squadron slot. Press once to arm, again " +
                "to confirm.";

            public const string Roe =
                "Rules of engagement, wing-wide: how far a wingman may go on its own " +
                "initiative before it needs telling.";

            public const string Weapon =
                "Which weapons the selected wingmen reach for first. Scoped, so a mixed " +
                "flight can split between the air and the ground.";

            public const string Form =
                "The formation shape wingmen fly when they are formed up on the leader.";

            public const string Requisition =
                "Buy the selected airframe. It launches from a friendly base with the fit " +
                "chosen on LOADOUT and flies out to join the wing.";

            public const string Fit =
                "What the next one of these launches with: its standard fit, or one of the " +
                "templates you have built for it on LOADOUT.";

            public const string OverLimit =
                "Permission to requisition past the mission's AI aircraft cap, at a " +
                "surcharge. Changes nothing while the squadron still has room.";

            public const string FullFuel =
                "Requisitions launch with full tanks. Switch it off to launch them at " +
                "half fuel instead - lighter and more agile, but they call bingo sooner.";

            public const string AssignSelected =
                "Conscript the friendly AI aircraft selected on the map into your wing. " +
                "Press twice to confirm the fee.";

            public const string ReserveHold =
                "Take this airframe out of the faction pool and keep it for the wing, so " +
                "the AI cannot spend it.";

            public const string ReserveRelease =
                "Give this airframe back to the faction pool. Press once to arm, again to " +
                "confirm.";

            public const string Jam =
                "JAM - the selected wingmen hold their formation slot and run their jammer " +
                "pod against the target you have locked, until it dies or you order them " +
                "off. Only wingmen carrying a jammer pod can take it.";

            public const string Pager = "Show the rest of the list.";
        }

        private static void Order(WingAction action) =>
            WingCommandManager.Instance?.Execute(action, wholeWing: false);

        private static void TurnRosterPage(int direction)
        {
            rosterPage = Mathf.Max(0, rosterPage + direction);
        }


        private static void RefreshTactical(WingRegistry wing)
        {
            WingCommandManager manager = WingCommandManager.Instance;

            if (summaryLabel != null)
                summaryLabel.text = "COMMAND: " + (manager?.Selection.Summary(wing) ?? "ALL") +
                                    "   ·   WING " + (wing.Count + WingShopDelivery.PendingCount) + "/" + WingRegistry.WingLimitLabel +
                                    "  (YOUR FLIGHT)";

            holdButton?.SetLatched(wing.Roe == WingRoe.Hold);
            tightButton?.SetLatched(wing.Roe == WingRoe.Tight);
            freeButton?.SetLatched(wing.Roe == WingRoe.Free);

            // A scope whose members disagree lights nothing, rather than lighting the first
            // one's choice and inviting the player to believe the whole scope shares it.
            WingWeaponPreference? shared = manager?.ScopeWeaponPreference();
            for (int i = 0; i < preferenceButtons.Length; i++)
                preferenceButtons[i]?.SetLatched(shared == WingWeaponPreferences.All[i]);

            if (manager != null)
            {
                List<WingMember> scope = manager.Commands.Scope(wholeWing: false);
                bool canCargo = false;
                bool canLand = false;
                bool canJam = false;
                foreach (WingMember member in scope)
                {
                    canCargo |= WingOrderCatalog.CanApply(member, WingOrder.DeliverCargo);
                    canLand |= WingOrderCatalog.CanApply(member, WingOrder.LandHere);
                    canJam |= WingOrderCatalog.CanApply(member, WingOrder.JamTarget);
                }
                cargoButton?.SetEnabled(canCargo);
                landButton?.SetEnabled(canLand && WingRegistry.IsRotary(wing.Leader));

                // Jam needs a jam-capable wingman in scope. The manoeuvre controls live on
                // the radial wheel, keeping this page focused on persistent orders.
                jamButton?.SetEnabled(canJam);
            }

            // The map has first claim on this line: an armed point order or a pending
            // assignment fee is a live instruction, and the engagement hints are not. A
            // hovered control outranks both, because it is the one the player is asking
            // about right now — RefreshStatusStrip resolves that.
            RefreshStatusStrip(Page.Tactical,
                manager != null && manager.MapStatusIsNotice
                    ? manager.MapStatus
                    : EngagementHint(wing, shared));

            RefreshRoster(wing);
            UpdateFormationAndDoctrine(wing, shared);
        }

        private static void UpdateFormationAndDoctrine(WingRegistry wing, WingWeaponPreference? shared)
        {
            FormationShape shape = WingFormation.Shape;

            if (formationButtons != null)
            {
                for (int i = 0; i < formationButtons.Length; i++)
                {
                    if (formationButtons[i] != null && i < FormationShapes.All.Length)
                    {
                        formationButtons[i].SetLatched(FormationShapes.All[i] == shape);
                    }
                }
            }

            // Manoeuvres are gated on the host profile: in Performance mode the whole set
            // is unavailable, the same gate the radial wheel uses to grey its slices.
            if (maneuverButtons != null)
            {
                for (int i = 0; i < maneuverButtons.Length; i++)
                {
                    if (maneuverButtons[i] != null)
                        maneuverButtons[i].SetEnabled(WingBrain.Manoeuvres);
                }
            }

            const float radarW = 108f;
            float radarCenterX = Pad + radarW * 0.5f;

            int totalInWing = (wing != null ? wing.Count : 0) + WingShopDelivery.PendingCount;
            for (int i = 0; i < formationWingmenDots.Count; i++)
            {
                Vector3 coord = FormationSolver.SlotCoordinates(i + 1, shape, 1f, 1f);
                float px = radarCenterX + Mathf.Clamp(coord.x * 16f, -44f, 44f);
                float py = formationRadarCenterY + Mathf.Clamp(coord.z * 16f, -48f, 12f);

                RectTransform dot = formationWingmenDots[i];
                if (dot != null)
                {
                    Place(dot, new Rect(px - 8f, py + 8f, 16f, 16f));
                    bool inWing = i < totalInWing;
                    dot.gameObject.SetActive(true);
                    var lbl = dot.GetComponentInChildren<TMP_Text>();
                    if (lbl != null) lbl.color = inWing ? Green() : new Color(0.4f, 0.6f, 0.55f, 0.45f);
                }

                if (i < formationVectorLines.Count && formationVectorLines[i] != null)
                {
                    Image line = formationVectorLines[i];
                    float dx = px - radarCenterX;
                    float dy = py - formationRadarCenterY;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

                    RectTransform lineRt = line.rectTransform;
                    lineRt.sizeDelta = new Vector2(dist, 1f);
                    lineRt.anchoredPosition = new Vector2(radarCenterX, formationRadarCenterY);
                    lineRt.localRotation = Quaternion.Euler(0f, 0f, angle);
                    bool inWing = i < totalInWing;
                    line.color = inWing ? new Color(0.2f, 0.65f, 0.45f, 0.45f) : new Color(0.2f, 0.35f, 0.3f, 0.2f);
                }
            }

            string roeName = wing != null ? RoeRules.Label(wing.Roe) : "HOLD";
            string wepName = shared.HasValue ? shared.Value.ToString().ToUpperInvariant() : "AUTO";
            string shapeName = FormationShapes.Pretty(shape).ToUpperInvariant();

            if (doctrineTitleLabel != null)
                doctrineTitleLabel.text = $"DOCTRINE: {roeName} · {wepName} · {shapeName}";

            if (doctrineProfileLabel != null)
            {
                switch (shape)
                {
                    case FormationShape.EchelonRight:
                    case FormationShape.EchelonLeft:
                        doctrineProfileLabel.text = "FLIGHT: Scimitar echelon, stepped down. One photograph from abeam.";
                        break;
                    case FormationShape.Trail:
                    case FormationShape.Ladder:
                        doctrineProfileLabel.text = "FLIGHT: Tight column astern. Trail weaves the wake; ladder climbs it.";
                        break;
                    case FormationShape.Vic:
                        doctrineProfileLabel.text = "FLIGHT: Display V, lead at the point. Reads as one aircraft head-on.";
                        break;
                    case FormationShape.LineAbreast:
                    case FormationShape.Wall:
                        doctrineProfileLabel.text = "FLIGHT: Shallow crescent abreast. Wall stacks the same line vertically.";
                        break;
                    case FormationShape.Diamond:
                    case FormationShape.FingerFour:
                    default:
                        doctrineProfileLabel.text = "FLIGHT: Close element. Diamond rolls as a rhombus; finger-four as a hand.";
                        break;
                }
            }

            if (doctrineRulesLabel != null)
            {
                WingRoe roe = wing != null ? wing.Roe : WingRoe.Hold;
                doctrineRulesLabel.text = "RULES: " + RoeRules.Hint(roe);
            }

            if (doctrineWeaponsLabel != null)
            {
                doctrineWeaponsLabel.text = shared.HasValue
                    ? "WEAPONS: " + WingWeaponPreferences.Hint(shared.Value)
                    : "WEAPONS: Mixed preference across selected flight members.";
            }
        }

        /// <summary>
        /// What the two engagement rows currently mean, in one sentence.
        ///
        /// The rules of engagement line is the important half and comes first; the weapon
        /// preference is only mentioned when it is doing something, so an ordinary AUTO
        /// flight reads exactly as it did before this control existed.
        /// </summary>
        private static string EngagementHint(WingRegistry wing, WingWeaponPreference? shared)
        {
            string hint = RoeRules.Hint(wing.Roe);

            if (shared == null) return hint + "  ·  Weapon preference varies across the selection.";
            if (shared.Value == WingWeaponPreference.Auto) return hint;

            return hint + "  ·  " + WingWeaponPreferences.Hint(shared.Value);
        }

        private static void RefreshRoster(WingRegistry wing)
        {
            int pendingCount = WingShopDelivery.PendingCount;
            int totalCount = wing.Count + pendingCount;
            bool empty = totalCount == 0;
            if (rosterEmptyLabel != null && rosterEmptyLabel.gameObject.activeSelf != empty)
                rosterEmptyLabel.gameObject.SetActive(empty);

            int pages = Mathf.Max(1, Mathf.CeilToInt(totalCount / (float)RosterRowsPerPage));
            rosterPage = Mathf.Clamp(rosterPage, 0, pages - 1);
            if (rosterPageLabel != null)
                rosterPageLabel.text = empty
                    ? ""
                    : pages == 1
                        ? totalCount + (totalCount == 1 ? " wingman" : " wingmen")
                        : "flight page " + (rosterPage + 1) + " of " + pages;

            rosterPrevButton?.SetEnabled(rosterPage > 0);
            rosterNextButton?.SetEnabled(rosterPage < pages - 1);

            SyncRosterRows(RosterRowsPerPage);
            int first = rosterPage * RosterRowsPerPage;

            for (int i = 0; i < rosterRows.Count; i++)
            {
                int index = first + i;
                if (index < wing.Count)
                {
                    rosterRows[i].Bind(wing.Members[index]);
                }
                else if (index < totalCount)
                {
                    rosterRows[i].BindPending(WingShopDelivery.GetPending(index - wing.Count), index + 1);
                }
                else
                {
                    rosterRows[i].Hide();
                }
            }
        }

        /// <summary>Keep the inspection focus on a pilot who is still on the roster.</summary>
        private static void PruneFocus(WingRegistry wing)
        {
            _ = wing;

            if (WingPilotRoster.Contains(inspectPilot)) return;

            // When the roster changes out from under the selection, fall back to the pilot
            // the player picked for the next flight, then to the most senior available.
            inspectPilot = WingPilotRoster.Selected;
            if (inspectPilot != null && WingPilotRoster.Contains(inspectPilot)) return;

            List<WingPilot> roster = WingPilotRoster.DisplayRoster();
            inspectPilot = roster.Count > 0 ? roster[0] : null;
        }


        private static void SyncRosterRows(int needed)
        {
            while (rosterRows.Count < needed && rosterArea != null)
            {
                int index = rosterRows.Count;
                rosterRows.Add(new RosterRow(rosterArea, index));
            }
        }


        /// <summary>One line of the roster: slot, name, state, fuel, ammo, release button.</summary>
        private sealed class RosterRow
        {
            private readonly GameObject go;
            private readonly TMP_Text slot, plane, name, order, fuel, ammo;
            private readonly Image selectionRule;
            private readonly Image fill;
            private readonly WingButton hit;
            private readonly WingButton lead;
            private readonly WingButton release;
            private WingMember bound;
            private WingShopDelivery.PendingDelivery boundPending;

            /// <summary>
            /// Which wingman, if any, has had its REL pressed once and is waiting to have it
            /// pressed again.
            ///
            /// Static, so arming one row disarms every other: two rows both offering to
            /// discharge a wingman on the next click is worse than none.
            /// </summary>
            private static readonly Confirmation memberRelease = new Confirmation();
            private static readonly Confirmation pendingRelease = new Confirmation();

            /// <summary>Drop the armed wingman when the mission ends, with everything else.</summary>
            public static void Disarm()
            {
                memberRelease.Clear();
                pendingRelease.Clear();
            }

            public RosterRow(RectTransform parent, int index)
            {
                float width = parent.rect.width;
                float y = -index * RowPitch;

                go = new GameObject("Row" + index, typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, new Rect(0f, y, width, RowHeight));

                fill = Panel(rt, new Rect(0f, 0f, width, RowHeight), MemberFrameColor());
                selectionRule = Rule(rt, new Rect(0f, 0f, 3f, RowHeight), WingColor());

                const float releaseWidth = WingUi.ButtonCompact;
                const float leadWidth = 26f;
                float releaseX = width - releaseWidth - 6f;
                float leadX = releaseX - leadWidth - Space1;

                hit = HitButton(rt, new Rect(0f, 0f, leadX - 2f, RowHeight), () =>
                {
                    if (bound != null)
                    {
                        bool toggle = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                        WingCommandManager.Instance?.SelectMember(bound, toggle);
                    }
                    else if (boundPending != null)
                    {
                        WingCommandManager.Instance?.Toast(
                            boundPending.AirframeName + " is preparing for departure - cannot be ordered until airborne");
                    }
                });

                // Cells sit under the columns in RosterColumns — PLANE, CALLSIGN, STATE,
                // FUEL, AMMO — so a header and the value beneath it cannot drift apart.
                slot  = Label(rt, "", new Rect(4f, 0f, 16f, RowHeight), Dim(), FontBody,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                plane = Label(rt, "", new Rect(22f, 0f, 56f, RowHeight), WingColor(), FontBody,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                name  = Label(rt, "", new Rect(80f, 0f, 66f, RowHeight), WingColor(), FontBody,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                order = Label(rt, "", new Rect(148f, 0f, 54f, RowHeight), Dim(), FontBody,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                fuel  = Label(rt, "", new Rect(204f, 0f, 36f, RowHeight), Dim(), FontSmall,
                              FontStyles.Normal, TextAlignmentOptions.Right);
                ammo  = Label(rt, "", new Rect(242f, 0f, 34f, RowHeight), Dim(), FontSmall,
                              FontStyles.Normal, TextAlignmentOptions.Right);

                // LD grants this wingman temporary flight lead: the rest of the wing then
                // formates on them instead of on the player.
                lead = WingUi.Button(rt, "LD",
                                     new Rect(leadX, -1f, leadWidth, RowHeight - 2f),
                                     FontMicro, UiButtonStyle.Default, ToggleLead)
                             .WithTooltip("Flight lead - the rest of the wing formates on this " +
                                          "wingman while it takes your orders. Press again to release.");

                // REL discharges a wingman for good.
                release = WingUi.Button(rt, "REL",
                                        new Rect(releaseX, -1f, releaseWidth, RowHeight - 2f),
                                        FontSmall, UiButtonStyle.Danger, ConfirmRelease)
                                .WithTooltip(OrderHint.Release);
            }

            private void ToggleLead()
            {
                if (bound != null) WingCommandManager.Instance?.ToggleFlightLead(bound);
            }

            /// <summary>Arm on the first press, discharge on the second.</summary>
            private void ConfirmRelease()
            {
                if (bound != null)
                {
                    if (memberRelease.IsArmedFor(bound))
                    {
                        WingMember going = bound;
                        memberRelease.Clear();
                        WingCommandManager.Instance?.RemoveMember(going);
                        return;
                    }

                    memberRelease.Arm(bound);
                    WingCommandManager.Instance?.Toast(
                        "Press REL again to release " + bound.Name + " from the wing");
                    return;
                }

                if (boundPending != null)
                {
                    if (pendingRelease.IsArmedFor(boundPending))
                    {
                        WingShopDelivery.PendingDelivery going = boundPending;
                        pendingRelease.Clear();
                        WingShopDelivery.CancelPending(going);
                        return;
                    }

                    pendingRelease.Arm(boundPending);
                    WingCommandManager.Instance?.Toast(
                        "Press REL again to cancel requisition of " + boundPending.AirframeName);
                }
            }

            public void Bind(WingMember m)
            {
                bool memberChanged = bound != m || boundPending != null;
                bound = m;
                boundPending = null;
                if (!go.activeSelf) go.SetActive(true);

                bool selected = WingCommandManager.Instance?.Selection.Contains(m) ?? true;

                bool armed = memberRelease.IsArmedFor(m);
                release?.SetLatched(armed);
                release?.SetText(armed ? "SURE?" : "REL");

                lead?.SetLatched(m.IsFlightLead);

                // Just the slot number. The filled/hollow circles this used to draw are not
                // in the MFD font, so every row rendered the same tofu box and the marker
                // said nothing — while the lit edge and the green callsign beside it were
                // already showing selection perfectly well.
                if (memberChanged)
                {
                    slot.text = m.Slot.ToString();
                    string planeStr = !string.IsNullOrEmpty(m.Aircraft?.definition?.code)
                        ? m.Aircraft.definition.code
                        : m.Name;
                    plane.text = AvTheme.Truncate(planeStr, 7);
                    string callsignStr = m.Crew != null && !string.IsNullOrEmpty(m.Crew.Callsign)
                        ? m.Crew.Callsign
                        : "AI";
                    name.text = AvTheme.Truncate(callsignStr, 8);
                }
                slot.color = selected ? Green() : Dim();
                plane.color = selected ? Green() : WingColor();
                name.color = selected ? Green() : WingColor();
                selectionRule.color = selected ? Green() : MemberFrameColor();

                // Selection is the row's resting state; the pointer only adds to it. Until
                // now nothing at all happened when the mouse crossed a row, so a roster that
                // is the panel's main control surface looked exactly like a readout.
                hit?.SetRowHighlight(fill,
                                     selected ? WingUi.CardFillSelected : WingUi.CardFill,
                                     WingUi.CardFillHover);
                order.text = ShortOrder(m);

                // Fuel and stores are aggregate queries over every tank/station. Sample
                // each once so binding one row does not walk both collections twice. Each
                // reads under its own header and turns amber on its own threshold, rather
                // than the old shared "45%  12" cell that went amber for either.
                float fuelFraction = m.Fuel;
                int ammoCount = m.Ammo;
                Color low = new Color(1f, 0.55f, 0.2f);
                fuel.text = Mathf.RoundToInt(fuelFraction * 100f) + "%";
                fuel.color = fuelFraction <= WingTuning.BingoFuel ? low : Dim();
                ammo.text = ammoCount.ToString();
                ammo.color = ammoCount <= 0 ? low : Dim();
            }

            public void BindPending(WingShopDelivery.PendingDelivery p, int slotNumber)
            {
                bool pendingChanged = boundPending != p || bound != null;
                bound = null;
                boundPending = p;
                if (!go.activeSelf) go.SetActive(true);

                bool armed = pendingRelease.IsArmedFor(p);
                release?.SetLatched(armed);
                release?.SetText(armed ? "SURE?" : "REL");

                lead?.SetLatched(false);

                if (pendingChanged)
                {
                    slot.text = slotNumber.ToString();
                    string planeStr = !string.IsNullOrEmpty(p.Definition?.code)
                        ? p.Definition.code
                        : p.AirframeName;
                    plane.text = AvTheme.Truncate(planeStr, 7);
                    name.text = "EN ROUTE";
                }
                slot.color = Dim();
                plane.color = WingColor();
                name.color = WingColor();
                selectionRule.color = MemberFrameColor();

                hit?.SetRowHighlight(fill, WingUi.CardFill, WingUi.CardFillHover);
                order.text = p.StatusCode;

                float fuelFraction = p.Fuel;
                fuel.text = Mathf.RoundToInt(fuelFraction * 100f) + "%";
                fuel.color = Dim();
                ammo.text = "-";
                ammo.color = Dim();
            }

            public void Hide()
            {
                bound = null;
                boundPending = null;
                if (go.activeSelf) go.SetActive(false);
            }
        }

    }
}
