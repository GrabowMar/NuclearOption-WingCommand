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
    /// <summary>TACTICAL page for ROE, scoped weapon preference, orders, and flight roster.</summary>
    internal static partial class WmcScreen
    {
        /// <summary>Group ROE, weapon preference, and formation controls in one compact block; put
        /// per-control explanations in the shared status strip.</summary>
        private static float AddEngagementSection(RectTransform parent, float y)
        {
            y = Heading(parent, y, "ENGAGEMENT - ROE APPLIES TO ALL");

            float left = Pad + GutterWidth;
            float w = PanelWidth - Pad - left;

            // Three wing-wide ROE choices form an escalation, not a toggle.
            Gutter(parent, y, "ROE");
            float roeWidth = (w - Gap * 2f) / 3f;
            // Reuse ROE hints for hover and resting status text.
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

            // Scope weapon preference to selected members so mixed flights can favour different roles.
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
            y -= RowHeight + Space1;
            Hint(parent, y, "Click a row to select. Shift-click to add or remove.");
            return y - LineHeight - Space2;
        }

        private static float AddRosterArea(RectTransform parent, float y)
        {
            y = Heading(parent, y, "FLIGHT");

            // Align headers with roster values.
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


        /// <summary>Group scoped target, autonomous-combat, and point orders. Individual RTB dismissal
        /// belongs on the member row.</summary>
        private static float AddActions(RectTransform parent, float y)
        {
            y = Heading(parent, y, "ORDERS - SELECTED SCOPE");
            float w = (PanelWidth - Pad * 2f - Gap * 2f) / 3f;

            // Use compact order labels and put detailed distinctions in status help.
            GridButton(parent, "Form Up", Pad, y, w,
                       () => Order(WingAction.Rejoin)).WithTooltip(OrderHint.Rejoin);
            attackButton = GridButton(parent, "Attack Target", Pad + w + Gap, y, w,
                                      () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.Attack),
                                      UiButtonStyle.Toggle)
                           .WithTooltip(OrderHint.Attack);
            GridButton(parent, "Splash", Pad + (w + Gap) * 2f, y, w,
                       () => Order(WingAction.FireForEffect)).WithTooltip(OrderHint.FireForEffect);
            y -= RowHeight + Gap;

            GridButton(parent, "Engage", Pad, y, w,
                       () => Order(WingAction.Engage)).WithTooltip(OrderHint.Engage);
            seekAndDestroyButton = GridButton(parent, "Seek & Destroy", Pad + w + Gap, y, w,
                                              () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.SeekAndDestroy),
                                              UiButtonStyle.Toggle)
                                  .WithTooltip(OrderHint.SeekAndDestroy);
            GridButton(parent, "Disengage", Pad + (w + Gap) * 2f, y, w,
                       () => Order(WingAction.FallBack)).WithTooltip(OrderHint.Disengage);
            y -= RowHeight + Gap;

            holdHereButton = GridButton(parent, "Hold Here", Pad, y, w,
                                        () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.OrbitHere),
                                        UiButtonStyle.Toggle)
                             .WithTooltip(OrderHint.HoldHere);

            jamButton = GridButton(parent, "Jam", Pad + w + Gap, y, w,
                                   () => Order(WingAction.JamMyTarget))
                        .WithTooltip(OrderHint.Jam);

            // Cargo arms a point; explain the second-press native-route fallback in status.
            cargoButton = GridButton(parent, "Cargo", Pad + (w + Gap) * 2f, y, w,
                                     () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.DeliverCargo),
                                     UiButtonStyle.Toggle)
                          .WithTooltip(OrderHint.DeliverCargo);
            y -= RowHeight + Gap;

            landButton = GridButton(parent, "Land", Pad, y, w,
                                    () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.LandHere),
                                    UiButtonStyle.Toggle)
                         .WithTooltip(OrderHint.LandHere);
            GridButton(parent, "Refit", Pad + w + Gap, y, w,
                       () => Order(WingAction.Refit))
                .WithTooltip("REFIT - land at base, refill fuel and ammunition, then relaunch and rejoin.");
            GridButton(parent, "Stand Down", Pad + (w + Gap) * 2f, y, w,
                       () => Order(WingAction.StandDown))
                .WithTooltip(OrderHint.StandDown);
            y -= RowHeight + Gap;

            float tuneW = (PanelWidth - Pad * 2f - Gap * 3f) / 4f;
            GridButton(parent, "ALT +", Pad, y, tuneW,
                       () => WingCommandManager.Instance?.StepMoveHeight(1))
                .WithTooltip(OrderHint.HeightUp);
            GridButton(parent, "ALT -", Pad + tuneW + Gap, y, tuneW,
                       () => WingCommandManager.Instance?.StepMoveHeight(-1))
                .WithTooltip(OrderHint.HeightDown);
            GridButton(parent, "SPD +", Pad + (tuneW + Gap) * 2f, y, tuneW,
                       () => WingCommandManager.Instance?.StepMoveSpeed(1))
                .WithTooltip(OrderHint.SpeedUp);
            GridButton(parent, "SPD -", Pad + (tuneW + Gap) * 3f, y, tuneW,
                       () => WingCommandManager.Instance?.StepMoveSpeed(-1))
                .WithTooltip(OrderHint.SpeedDown);
            y -= RowHeight + Gap;

            y = AddFormationAndDoctrine(parent, y);

            return y;
        }

        private static TMP_Text doctrineTitleLabel;
        private static TMP_Text doctrineProfileLabel;
        private static TMP_Text doctrineRulesLabel;
        private static TMP_Text doctrineWeaponsLabel;
        private static WingButton[] formationButtons;
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
            y = Heading(parent, y, "FORMATION - WHOLE FLIGHT");

            float w = PanelWidth - Pad * 2f;
            const float radarW = 108f;
            const float boxH = 88f;

            // Formation preview.
            WingUi.TacticalCard(parent, new Rect(Pad, y, radarW, boxH), WingUi.RailEmerald);

            // Low-opacity preview crosshairs.
            Color crosshairCol = new Color(WingUi.RailEmerald.r, WingUi.RailEmerald.g, WingUi.RailEmerald.b, 0.25f);
            Rule(parent, new Rect(Pad + radarW * 0.5f, y - 6f, 1f, boxH - 28f), crosshairCol);
            Rule(parent, new Rect(Pad + 6f, y - (boxH - 20f) * 0.5f, radarW - 12f, 1f), crosshairCol);

            float radarCenterX = Pad + radarW * 0.5f;
            formationRadarCenterY = y - 22f;

            // Centred leader symbol.
            Label(parent, "^", new Rect(radarCenterX - 10f, formationRadarCenterY - 6f, 20f, 16f),
                  Green(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
            Label(parent, "LDR", new Rect(radarCenterX - 15f, formationRadarCenterY + 10f, 30f, 10f),
                  Green(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);

            // Three follower markers with slot connections.
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
                Label(rt, (i + 1).ToString(), new Rect(0f, 0f, 16f, 16f), Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
                Place(rt, new Rect(radarCenterX, formationRadarCenterY, 16f, 16f));
                formationWingmenDots.Add(rt);
            }

            // Combat-doctrine readouts.
            float docX = Pad + radarW + Gap;
            float docW = w - radarW - Gap;

            WingUi.TacticalCard(parent, new Rect(docX, y, docW, boxH), WingUi.RailCyan);

            float lineY = y - 4f;
            doctrineTitleLabel = Label(parent, "",
                new Rect(docX + Space2, lineY, docW - Space3, 16f),
                Green(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
            lineY -= 18f;

            doctrineProfileLabel = Label(parent, "",
                new Rect(docX + Space2, lineY, docW - Space3, LineHeight),
                Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            lineY -= 20f;

            doctrineRulesLabel = Label(parent, "",
                new Rect(docX + Space2, lineY, docW - Space3, LineHeight),
                Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            lineY -= 20f;

            doctrineWeaponsLabel = Label(parent, "",
                new Rect(docX + Space2, lineY, docW - Space3, LineHeight),
                WingUi.TextPrimary, FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);

            y -= boxH + Space2;

            // Formation choices below the preview.
            const int cols = 5;
            const float btnH = RowHeight;
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
                    FontSmall, UiButtonStyle.Toggle,
                    () => SetFormationShape(shape))
                    .WithTooltip(FormationShapes.Pretty(shape) + " formation geometry");
            }

            int rows = Mathf.CeilToInt(FormationShapes.All.Length / (float)cols);
            y -= rows * btnH + (rows - 1) * Gap + Space2;
            return y;
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

        /// <summary>Shared two-line order help explaining scope and distinctions that compact button
        /// labels cannot carry.</summary>
        private static class OrderHint
        {
            public const string Rejoin =
                "FORM UP - break off and return to formation on the leader. Cancels any " +
                "attack, hold or route the selected wingmen are flying.";

            public const string Attack =
                "ATTACK TARGET - if you have contacts designated, sends the selection after " +
                "them immediately. The button stays armed: right-click a hostile on the map " +
                "to focus that target instead. Shift-right-click queues another.";

            public const string FireForEffect =
                "SPLASH - empty everything that will bear on your locked target. " +
                "Expends ordnance freely; use it to finish something, not to open on it.";

            public const string Engage =
                "ENGAGE - hunt independently. Sets rules of engagement to FREE. The wingman " +
                "picks its own targets and does not come back until told to.";

            public const string SeekAndDestroy =
                "SEEK & DESTROY - then right-click the map. The selection flies to that " +
                "point, then begins hunting independently under the current rules of engagement. " +
                "Shift-right-click queues another.";

            public const string Disengage =
                "DISENGAGE - break contact and run for the nearest friendly base or ship, " +
                "defending itself on the way. Not a landing order.";

            public const string HoldHere =
                "HOLD HERE - then right-click the map. The selection orbits that point and " +
                "defends itself, but starts nothing. Shift-right-click queues the next order.";

            public const string DeliverCargo =
                "DELIVER CARGO - then right-click a drop point, or press again to use the " +
                "stock supply route. Shift-right-click queues another drop. Only wingmen " +
                "actually carrying a load can take this.";

            public const string LandHere =
                "LAND HERE - then right-click the map. Puts a rotary wingman on the ground " +
                "at that spot rather than routing it to an airbase. Shift-right-click queues.";

            public const string StandDown =
                "STAND DOWN - cancel the current task and loiter near the nearest friendly " +
                "airbase or ship. Does not land and does not rejoin until ordered.";

            public const string HeightUp =
                "HEIGHT + - raise Move altitude. Applies to the next map Move and to " +
                "selected wingmen already moving.";

            public const string HeightDown =
                "HEIGHT - - lower Move altitude. Applies to the next map Move and to " +
                "selected wingmen already moving.";

            public const string SpeedUp =
                "SPEED + - raise Move speed. Applies to the next map Move and to selected " +
                "wingmen already moving.";

            public const string SpeedDown =
                "SPEED - - lower Move speed. Applies to the next map Move and to selected " +
                "wingmen already moving.";

            public const string SelectAll =
                "Put every wingman in the command scope, so the next order goes to the " +
                "whole flight.";

            public const string ReturnToBase =
                "RTB - dismiss this wingman from the active flight. It flies home; after " +
                "recovery, both its airframe and pilot return to their pools. Press twice " +
                "to confirm.";

            public const string Roe =
                "Rules of engagement, wing-wide: how far a wingman may go on its own " +
                "initiative before it needs telling.";

            public const string Weapon =
                "Which weapons the selected wingmen reach for first. Scoped, so a mixed " +
                "flight can split between the air and the ground.";

            public const string Requisition =
                "Buy the selected airframe. It launches from a friendly base with the fit " +
                "chosen on LOADOUT and flies out to join the wing.";

            public const string Fit =
                "What the next one of these launches with: your current player default for " +
                "this airframe, or one of the templates you have built for it on LOADOUT.";

            public const string OverLimit =
                "Permission to requisition past the mission's AI aircraft cap, at a " +
                "surcharge. Changes nothing while the squadron still has room.";

            public const string FullFuel =
                "Fuel each requisition launches with, as a share of full tanks - steps " +
                "25 / 50 / 75 / 100%. Less fuel is lighter and more agile but calls bingo " +
                "sooner; 100% is completely full even for an airframe that ships short.";

            public const string AssignSelected =
                "Conscript the friendly AI aircraft selected on the map into your wing. " +
                "Press twice to confirm the fee.";

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
            WingRegistry wing = Wing();
            if (wing != null) RefreshTactical(wing);
        }


        private static void RefreshTactical(WingRegistry wing)
        {
            WingCommandManager manager = WingCommandManager.Instance;

            if (summaryLabel != null)
                summaryLabel.text = "COMMAND: " + (manager?.Selection.Summary(wing) ?? "ALL");

            holdButton?.SetLatched(wing.Roe == WingRoe.Hold);
            tightButton?.SetLatched(wing.Roe == WingRoe.Tight);
            freeButton?.SetLatched(wing.Roe == WingRoe.Free);

            // Leave all preference buttons unlit for mixed scope values.
            WingWeaponPreference? shared = manager?.ScopeWeaponPreference();
            for (int i = 0; i < preferenceButtons.Length; i++)
                preferenceButtons[i]?.SetLatched(shared == WingWeaponPreferences.All[i]);

            if (manager != null)
            {
                List<WingMember> scope = manager.Commands.Scope(wholeWing: false);
                bool canCargo = false;
                bool canLand = false;
                bool canJam = false;
                bool canSeekAndDestroy = false;
                foreach (WingMember member in scope)
                {
                    canCargo |= WingOrderCatalog.CanApply(member, WingOrder.DeliverCargo);
                    canLand |= WingOrderCatalog.CanApply(member, WingOrder.LandHere);
                    canJam |= WingOrderCatalog.CanApply(member, WingOrder.JamTarget);
                    canSeekAndDestroy |= WingOrderCatalog.CanApply(member, WingOrder.SeekAndDestroy);
                }
                cargoButton?.SetEnabled(canCargo);
                landButton?.SetEnabled(canLand && WingRegistry.IsRotary(wing.Leader));
                seekAndDestroyButton?.SetEnabled(canSeekAndDestroy);

                // Require a jam-capable member in scope; manoeuvres are offered on the radial.
                jamButton?.SetEnabled(canJam);

                bool armed = manager.MapOrderArmed;
                WingOrder armedOrder = manager.ArmedMapOrder;
                attackButton?.SetLatched(armed && armedOrder == WingOrder.Attack);
                holdHereButton?.SetLatched(armed && armedOrder == WingOrder.OrbitHere);
                seekAndDestroyButton?.SetLatched(armed && armedOrder == WingOrder.SeekAndDestroy);
                cargoButton?.SetLatched(armed && armedOrder == WingOrder.DeliverCargo);
                landButton?.SetLatched(armed && armedOrder == WingOrder.LandHere);
            }

            // Status priority is hover help, live map instruction, then standing engagement hints.
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
            string wepName = shared.HasValue ? WingWeaponPreferences.Label(shared.Value) : "MIXED";
            if (wing == null || wing.Count == 0 || WingCommandManager.Instance?.Selection.IsNone == true)
                wepName = "NONE";
            string shapeName = FormationShapes.Pretty(shape).ToUpperInvariant();

            if (doctrineTitleLabel != null)
                doctrineTitleLabel.text = shapeName;

            if (doctrineProfileLabel != null)
                doctrineProfileLabel.text = "ROE: " + roeName + "  /  WHOLE FLIGHT";

            if (doctrineRulesLabel != null)
            {
                doctrineRulesLabel.text = "WEAPONS: " + wepName + "  /  SELECTED";
            }

            if (doctrineWeaponsLabel != null)
            {
                doctrineWeaponsLabel.text = "FLIGHT " + (wing?.Count ?? 0) +
                    "  /  INBOUND " + WingShopDelivery.PendingCount;
            }
        }

        /// <summary>Summarise ROE first, adding weapon preference only when non-Auto.</summary>
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
                rosterPageLabel.text = PageSummary(totalCount, rosterPage, pages,
                                                    "WINGMAN", "WINGMEN");

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

        /// <summary>Retain inspection focus only while its pilot remains on the roster.</summary>
        private static void PruneFocus(WingRegistry wing)
        {
            _ = wing;

            if (WingPilotRoster.Contains(inspectPilot)) return;

            // After removal, prefer the next-flight pilot, then the most senior available.
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


        /// <summary>Active-aircraft row with identity, state, fuel, ammunition, and release
        /// control.</summary>
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

            /// <summary>Shared RTB confirmation across rows; arming one member disarms the previous
            /// member.</summary>
            private static readonly Confirmation memberRelease = new Confirmation();
            private static readonly Confirmation pendingRelease = new Confirmation();

            /// <summary>Clear pending member-release confirmation at mission end.</summary>
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

                // Use RosterColumns positions so labels remain aligned with headers.
                slot  = Label(rt, "", new Rect(4f, 0f, 24f, RowHeight), Dim(), FontBody,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                plane = Cell(0);
                name = Cell(1);
                order = Cell(2);
                fuel = Cell(3);
                ammo = Cell(4);

                TMP_Text Cell(int index)
                {
                    Column column = RosterColumns[index];
                    return Label(rt, "", new Rect(column.X, 0f, column.Width, RowHeight),
                                 WingUi.TextPrimary, FontBody, FontStyles.Normal,
                                 column.RightAligned ? TextAlignmentOptions.Right : TextAlignmentOptions.Left);
                }

                // LD toggles temporary flight lead for the other members.
                lead = WingUi.Button(rt, "LD",
                                     new Rect(leadX, -1f, leadWidth, RowHeight - 2f),
                                     FontMicro, UiButtonStyle.Default, ToggleLead)
                             .WithTooltip("Flight lead - the rest of the wing formates on this " +
                                          "wingman while it takes your orders. Press again to release.");

                // RTB removes the member from active command and sends it home.
                release = WingUi.Button(rt, "RTB",
                                        new Rect(releaseX, -1f, releaseWidth, RowHeight - 2f),
                                        FontSmall, UiButtonStyle.Danger, ConfirmRelease)
                                .WithTooltip(OrderHint.ReturnToBase);
            }

            private void ToggleLead()
            {
                if (bound != null) WingCommandManager.Instance?.ToggleFlightLead(bound);
            }

            /// <summary>Arm release first, then confirm the same member's RTB.</summary>
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
                        "Press RTB again to send " + bound.Name + " home from the wing");
                    return;
                }

                if (boundPending != null)
                {
                    if (!boundPending.CanCancel)
                    {
                        pendingRelease.Clear();
                        WingCommandManager.Instance?.Toast(
                            "Launch already accepted; wait for delivery before releasing");
                        return;
                    }
                    if (pendingRelease.IsArmedFor(boundPending))
                    {
                        WingShopDelivery.PendingDelivery going = boundPending;
                        pendingRelease.Clear();
                        WingShopDelivery.CancelPending(going);
                        return;
                    }

                    pendingRelease.Arm(boundPending);
                    WingCommandManager.Instance?.Toast(
                        "Press CXL again to cancel requisition of " + boundPending.AirframeName);
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
                release?.SetEnabled(true);
                release?.WithTooltip(OrderHint.ReturnToBase);
                release?.SetLatched(armed);
                release?.SetText(armed ? "SURE?" : "RTB");

                lead?.SetLatched(m.IsFlightLead);
                lead?.SetEnabled(true);
                slot.text = (selected ? ">" : "") + m.Slot;

                // Use a numeric slot; unsupported circle glyphs render as missing-character boxes.
                if (memberChanged)
                {
                    string planeStr = !string.IsNullOrEmpty(m.Aircraft?.definition?.code)
                        ? m.Aircraft.definition.code
                        : m.Name;
                    plane.text = AvTheme.Truncate(planeStr, 7);
                    string callsignStr = m.Crew != null && !string.IsNullOrEmpty(m.Crew.Callsign)
                        ? m.Crew.Callsign
                        : "AI";
                    name.text = callsignStr;
                    hit.WithTooltip(m.Name + " / " + planeStr + " / " + callsignStr +
                        ". Click to select; Shift-click to add or remove.");
                }
                slot.color = selected ? Green() : Dim();
                plane.color = selected ? Green() : WingColor();
                name.color = selected ? Green() : WingColor();
                selectionRule.color = selected ? Green() : MemberFrameColor();

                // Layer hover feedback over persistent row selection.
                hit?.SetRowHighlight(fill,
                                     selected ? WingUi.CardFillSelected : WingUi.CardFill,
                                     WingUi.CardFillHover);
                order.text = ShortOrder(m);

                // Sample tank and station aggregates once per row; apply separate fuel and ammunition
                // warning thresholds.
                float fuelFraction = m.Fuel;
                int ammoCount = m.Ammo;
                Color low = Warning();
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

                bool canCancel = p.CanCancel;
                if (!canCancel && pendingRelease.IsArmedFor(p)) pendingRelease.Clear();
                bool armed = canCancel && pendingRelease.IsArmedFor(p);
                release?.SetEnabled(canCancel);
                release?.WithTooltip(canCancel ? "CANCEL - cancel this pending requisition. " +
                    "Press once to arm, again to confirm." :
                    "Launch already accepted; wait for delivery before cancelling");
                release?.SetLatched(armed);
                release?.SetText(canCancel ? (armed ? "SURE?" : "CXL") : "DEPT");

                lead?.SetLatched(false);
                lead?.SetEnabled(false);

                if (pendingChanged)
                {
                    hit.WithTooltip(p.AirframeName + " is preparing for departure. Orders unlock when airborne.");
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
