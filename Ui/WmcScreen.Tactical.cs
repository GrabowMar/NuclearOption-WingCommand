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

            float w = TacticalCellWidth;
            Gutter(parent, y, "ROE / ALL");
            holdButton = TacticalButton(parent, "HOLD", TacticalColumn(1), y, w,
                () => SetRoe(WingRoe.Hold), UiButtonStyle.Toggle)
                .WithTooltip("HOLD - " + CombatFacade.Roe.Hint(WingRoe.Hold));
            tightButton = TacticalButton(parent, "TIGHT", TacticalColumn(2), y, w,
                () => SetRoe(WingRoe.Tight), UiButtonStyle.Toggle)
                .WithTooltip("TIGHT - " + CombatFacade.Roe.Hint(WingRoe.Tight));
            freeButton = TacticalButton(parent, "FREE", TacticalColumn(3), y, w,
                () => SetRoe(WingRoe.Free), UiButtonStyle.Toggle)
                .WithTooltip("FREE - " + CombatFacade.Roe.Hint(WingRoe.Free));
            y -= TacticalButtonHeight + Gap;
            for (int i = 0; i < preferenceButtons.Length; i++)
            {
                WingWeaponPreference preference = WingWeaponPreferences.All[i];
                preferenceButtons[i] = TacticalButton(parent, WingWeaponPreferences.Label(preference),
                    TacticalColumn(i), y, w,
                    () => WingCommandManager.Instance?.SetWeaponPreference(preference), UiButtonStyle.Toggle)
                    .WithTooltip("WEAPON " + WingWeaponPreferences.Label(preference) + " - " +
                        WingWeaponPreferences.Hint(preference) + " Selected aircraft only.");
            }
            y -= TacticalButtonHeight + Gap;
            return y;
        }


        private static void SetRoe(WingRoe roe)
        {
            WingRegistry wing = Wing();
            if (wing == null) return;

            wing.Roe = roe;
            WingCommandManager.Instance?.Toast("ROE: " + CombatFacade.Roe.Label(roe));
        }

        private static float AddSummary(RectTransform parent, float y)
        {
            float w = PanelWidth - Pad * 2f;
            WingUi.TacticalCard(parent, new Rect(Pad, y, w, RowHeight), WingUi.RailEmerald);

            const float actionWidth = TacticalCellWidth;
            summaryLabel = Label(parent, "",
                                 new Rect(Pad + Space3, y, w - actionWidth - Space4,
                                          RowHeight),
                                 Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            WingUi.Button(parent, "SELECT ALL",
                          new Rect(TacticalColumn(3), y, actionWidth, TacticalButtonHeight),
                          FontSmall, UiButtonStyle.Primary,
                          () => WingCommandManager.Instance?.SelectAllMembers())
                .WithTooltip(OrderHint.SelectAll);
            y -= RowHeight + Space1;
            return y - Space1;
        }

        private static float AddRosterArea(RectTransform parent, float y)
        {
            rosterExpandButton = WingUi.Button(parent, "FLIGHT [+]", new Rect(Pad, y, TacticalCellWidth, TacticalButtonHeight),
                FontSmall, UiButtonStyle.Quiet, ToggleRosterExpanded)
                .WithTooltip("Expand to six aircraft per page, or collapse to three. Selection is preserved.");
            rosterPrevButton = WingUi.Button(parent, "PREV", new Rect(TacticalColumn(2), y, TacticalCellWidth, TacticalButtonHeight),
                FontSmall, () => TurnRosterPage(-1)).WithTooltip("Previous flight page");
            rosterPageLabel = Label(parent, "", new Rect(TacticalColumn(1), y, TacticalCellWidth, TacticalButtonHeight),
                Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Center);
            rosterNextButton = WingUi.Button(parent, "NEXT", new Rect(TacticalColumn(3), y, TacticalCellWidth, TacticalButtonHeight),
                FontSmall, () => TurnRosterPage(1)).WithTooltip("Next flight page");
            y -= RowHeight + Gap;
            float w = PanelWidth - Pad * 2f;
            y = ColumnHeaders(parent, y, RosterColumns);
            float h = RowPitch * RosterRowsPerPage;
            var area = new GameObject("Roster", typeof(RectTransform));
            rosterArea = area.GetComponent<RectTransform>();
            rosterArea.SetParent(parent, worldPositionStays: false);
            Place(rosterArea, new Rect(Pad, y, w, h));
            rosterEmptyLabel = EmptyNote(rosterArea,
                "No wingmen. Requisition on SUPPLY, or assign friendly AI from the map.");
            return y - h - Gap;
        }


        /// <summary>Group scoped target, autonomous-combat, and point orders. Individual RTB dismissal
        /// belongs on the member row.</summary>
        private static float AddActions(RectTransform parent, float y)
        {
            y = Heading(parent, y, "ORDERS - SELECTED SCOPE");
            y = AddTacticalTabs(parent, y, new[] { "1 COMBAT", "2 TASKING", "3 ROUTE" },
                orderPages, orderTabs, index => SetOrderPage(index));
            RectTransform combat = orderPages[0];
            RectTransform tasking = orderPages[1];
            RectTransform route = orderPages[2];
            float top = y;
            parent = combat;
            float w = TacticalCellWidth;

            // Use compact order labels and put detailed distinctions in status help.
            TacticalButton(parent, "Form Up", Pad, y, w,
                       () => Order(WingAction.Rejoin)).WithTooltip(OrderHint.Rejoin);
            attackButton = TacticalButton(parent, "Attack Target", Pad + w + Gap, y, w,
                                      () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.Attack),
                                      UiButtonStyle.Toggle)
                           .WithTooltip(OrderHint.Attack);
            TacticalButton(parent, "Splash", Pad + (w + Gap) * 2f, y, w,
                       () => Order(WingAction.FireForEffect)).WithTooltip(OrderHint.FireForEffect);
            TacticalButton(parent, "Engage", TacticalColumn(3), y, w,
                       () => Order(WingAction.Engage)).WithTooltip(OrderHint.Engage);
            y -= TacticalButtonHeight + Gap;
            seekAndDestroyButton = TacticalButton(parent, "Seek & Destroy", Pad, y, w,
                                              () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.SeekAndDestroy),
                                              UiButtonStyle.Toggle)
                                  .WithTooltip(OrderHint.SeekAndDestroy);
            TacticalButton(parent, "Disengage", TacticalColumn(1), y, w,
                       () => Order(WingAction.FallBack)).WithTooltip(OrderHint.Disengage);
            y -= TacticalButtonHeight + Gap;

            parent = tasking;
            y = top;
            holdHereButton = TacticalButton(parent, "Hold Here", Pad, y, w,
                                        () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.OrbitHere),
                                        UiButtonStyle.Toggle)
                             .WithTooltip(OrderHint.HoldHere);

            jamButton = TacticalButton(parent, "Jam", Pad + w + Gap, y, w,
                                   () => Order(WingAction.JamMyTarget))
                        .WithTooltip(OrderHint.Jam);

            // Cargo arms a point; explain the second-press native-route fallback in status.
            cargoButton = TacticalButton(parent, "Cargo", Pad + (w + Gap) * 2f, y, w,
                                     () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.DeliverCargo),
                                     UiButtonStyle.Toggle)
                          .WithTooltip(OrderHint.DeliverCargo);
            landButton = TacticalButton(parent, "Land", TacticalColumn(3), y, w,
                                    () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.LandHere),
                                    UiButtonStyle.Toggle)
                         .WithTooltip(OrderHint.LandHere);
            y -= TacticalButtonHeight + Gap;
            TacticalButton(parent, "Refit", Pad, y, w,
                       () => Order(WingAction.Refit))
                .WithTooltip("REFIT - land at base, refill fuel and ammunition, then resume this task and route. A new order cancels the saved task.");
            TacticalButton(parent, "Stand Down", TacticalColumn(1), y, w,
                       () => Order(WingAction.StandDown))
                .WithTooltip(OrderHint.StandDown);
            y -= TacticalButtonHeight + Gap;

            parent = route;
            y = top;
            y = AddRouteControls(parent, y);
            float tuneW = TacticalCellWidth;
            TacticalButton(parent, "ALT +", Pad, y, tuneW,
                       () => WingCommandManager.Instance?.StepMoveHeight(1))
                .WithTooltip(OrderHint.HeightUp);
            TacticalButton(parent, "ALT -", Pad + tuneW + Gap, y, tuneW,
                       () => WingCommandManager.Instance?.StepMoveHeight(-1))
                .WithTooltip(OrderHint.HeightDown);
            TacticalButton(parent, "SPD +", Pad + (tuneW + Gap) * 2f, y, tuneW,
                       () => WingCommandManager.Instance?.StepMoveSpeed(1))
                .WithTooltip(OrderHint.SpeedUp);
            TacticalButton(parent, "SPD -", Pad + (tuneW + Gap) * 3f, y, tuneW,
                       () => WingCommandManager.Instance?.StepMoveSpeed(-1))
                .WithTooltip(OrderHint.SpeedDown);
            y -= TacticalButtonHeight + Gap;

            routeLabel = Label(route, "", new Rect(Pad, y, PanelWidth - Pad * 2f, LineHeight),
                Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            Hint(combat, y, "Combat orders apply to the selected aircraft.");
            Hint(tasking, y, "Hold, logistics and recovery for the selection.");
            SetOrderPage(orderPage);
            return y - LineHeight - Gap;

        }

        private static WingButton[] formationButtons;

        private static float AddFlightGeometry(RectTransform parent, float y)
        {
            y = Heading(parent, y, "FLIGHT GEOMETRY / MANOEUVRES");
            y = AddTacticalTabs(parent, y, new[] { "1 FORMATION", "2 MANOEUVRES" },
                geometryPages, geometryTabs, index => SetGeometryPage(index));
            y = AddTacticalPreview(parent, y);
            const int columns = 4;
            float w = (PanelWidth - Pad * 2f - Gap * (columns - 1)) / columns;
            formationButtons = new WingButton[FormationShapes.All.Length];
            for (int i = 0; i < FormationShapes.All.Length; i++)
            {
                FormationShape shape = FormationShapes.All[i];
                formationButtons[i] = TacticalButton(geometryPages[0], FormationShapes.Pretty(shape),
                    Pad + i % columns * (w + Gap), y - i / columns * (TacticalButtonHeight + Gap), w,
                    () => SetFormationShape(shape), UiButtonStyle.Toggle)
                    .WithTooltip(FormationShapes.Pretty(shape) + " - applies to the whole flight.");
            }
            for (int i = 0; i < TacticalManeuvers.Length; i++)
            {
                ManeuverKind kind = TacticalManeuvers[i];
                maneuverButtons[i] = TacticalButton(geometryPages[1], ManeuverCatalog.Label(kind),
                    Pad + i % columns * (w + Gap), y - i / columns * (TacticalButtonHeight + Gap), w,
                    () => WingCommandManager.Instance?.ExecuteManeuver(kind, wholeWing: false))
                    .WithTooltip(ManeuverCatalog.Label(kind) + " - selected wingmen; needs " +
                        ManeuverCatalog.MinEntryAltitudeAgl(kind) + " m AGL and safe entry speed.");
            }
            SetGeometryPage(geometryPage);
            int rows = Mathf.CeilToInt(Mathf.Max(FormationShapes.All.Length, TacticalManeuvers.Length) / (float)columns);
            return y - rows * (TacticalButtonHeight + Gap);
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

                // Require a jam-capable member in scope.
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

            RefreshTacticalNavigation(wing);
            RefreshRoster(wing);
            RefreshFlightGeometry();
        }

        private static void RefreshFlightGeometry()
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

            RefreshTacticalPreview();
        }

        /// <summary>Summarise ROE first, adding weapon preference only when non-Auto.</summary>
        private static string EngagementHint(WingRegistry wing, WingWeaponPreference? shared)
        {
            string hint = CombatFacade.Roe.Hint(wing.Roe);

            if (shared == null) return hint + "  ·  Weapon preference varies across the selection.";
            if (shared.Value == WingWeaponPreference.Auto) return hint;

            return hint + "  ·  " + WingWeaponPreferences.Hint(shared.Value);
        }

        private static void RefreshRoster(WingRegistry wing)
        {
            int pendingCount = EconomyFacade.ShopDelivery.PendingCount;
            int totalCount = wing.Count + pendingCount;
            bool empty = totalCount == 0;
            if (rosterEmptyLabel != null && rosterEmptyLabel.gameObject.activeSelf != empty)
                rosterEmptyLabel.gameObject.SetActive(empty);

            int visibleRows = rosterExpanded ? ExpandedRosterRows : RosterRowsPerPage;
            int pages = Mathf.Max(1, Mathf.CeilToInt(totalCount / (float)visibleRows));
            rosterPage = Mathf.Clamp(rosterPage, 0, pages - 1);
            if (rosterPageLabel != null)
                rosterPageLabel.text = (rosterPage + 1) + "/" + pages;
            rosterExpandButton?.SetText("FLIGHT " + totalCount + (rosterExpanded ? " [-]" : " [+]"));

            rosterPrevButton?.SetEnabled(rosterPage > 0);
            rosterNextButton?.SetEnabled(rosterPage < pages - 1);

            SyncRosterRows(visibleRows);
            int first = rosterPage * visibleRows;

            for (int i = 0; i < rosterRows.Count; i++)
            {
                int index = first + i;
                if (i >= visibleRows) { rosterRows[i].Hide(); continue; }
                if (index < wing.Count)
                {
                    rosterRows[i].Bind(wing.Members[index]);
                }
                else if (index < totalCount)
                {
                    rosterRows[i].BindPending(EconomyFacade.ShopDelivery.GetPending(index - wing.Count), index + 1);
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

            if (PersonnelFacade.Roster.Contains(inspectPilot)) return;

            // After removal, prefer the next-flight pilot, then the most senior available.
            inspectPilot = PersonnelFacade.Roster.Selected;
            if (inspectPilot != null && PersonnelFacade.Roster.Contains(inspectPilot)) return;

            List<WingPilot> roster = PersonnelFacade.Roster.DisplayRoster();
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
                                     new Rect(leadX, 0f, leadWidth, RowHeight),
                                     FontMicro, UiButtonStyle.Default, ToggleLead)
                             .WithTooltip("Flight lead - the rest of the wing formates on this " +
                                          "wingman while it takes your orders. Press again to release.");

                // RTB removes the member from active command and sends it home.
                release = WingUi.Button(rt, "RTB",
                                        new Rect(releaseX, 0f, releaseWidth, RowHeight),
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
                        EconomyFacade.ShopDelivery.CancelPending(going);
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
