using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    internal static partial class WmcScreen
    {
        private const float TacticalButtonHeight = RowHeight;
        private const float TacticalCellWidth = (PanelWidth - Pad * 2f - Gap * 3f) / 4f;
        private static float TacticalColumn(int column) => Pad + column * (TacticalCellWidth + Gap);
        internal static bool TacticalFlightExpanded => TacticalCommandModeActive && rosterExpanded;
        private const int ExpandedRosterRows = 6;
        private static bool rosterExpanded;
        private static WingButton rosterExpandButton;
        private static RectTransform tacticalViewport, tacticalContent, tacticalCommands, tacticalScrollTrack;
        private static ScrollRect tacticalScroll;
        private static float tacticalTop, tacticalCommandsTop, tacticalCollapsedHeight;
        private static readonly RectTransform[] orderPages = new RectTransform[3];
        private static readonly WingButton[] orderTabs = new WingButton[3];
        private static readonly RectTransform[] geometryPages = new RectTransform[2];
        private static readonly WingButton[] geometryTabs = new WingButton[2];
        private static int orderPage, geometryPage;
        private static readonly ManeuverKind[] TacticalManeuvers = {
            ManeuverKind.BarrelRoll, ManeuverKind.AileronRoll, ManeuverKind.Loop,
            ManeuverKind.WingWaggle, ManeuverKind.Immelmann, ManeuverKind.SplitS,
            ManeuverKind.BreakLeft, ManeuverKind.BreakRight, ManeuverKind.NotchThreat,
            ManeuverKind.MaskTerrain
        };
        private static readonly WingButton[] maneuverButtons = new WingButton[TacticalManeuvers.Length];
        private static WingButton patrolButton, autoRefitButton;
        private static TMP_Text routeLabel;

        private static WingButton TacticalButton(RectTransform parent, string text, float x, float y,
            float width, Action action, UiButtonStyle style = UiButtonStyle.Default) =>
            WingUi.Button(parent, text, new Rect(x, y, width, TacticalButtonHeight), FontSmall, style, action);

        private static float BuildTacticalPage(RectTransform parent, float top)
        {
            tacticalTop = top;
            var go = new GameObject("TacticalViewport", typeof(RectTransform), typeof(Image),
                typeof(RectMask2D), typeof(ScrollRect));
            tacticalViewport = go.GetComponent<RectTransform>();
            tacticalViewport.SetParent(parent, false);
            go.GetComponent<Image>().color = Color.clear;
            tacticalContent = PageRoot(tacticalViewport, "TacticalContent");
            Place(tacticalContent, new Rect(0f, 0f, PanelWidth, 1f));
            tacticalScroll = go.GetComponent<ScrollRect>();
            tacticalScroll.viewport = tacticalViewport;
            tacticalScroll.content = tacticalContent;
            tacticalScroll.horizontal = false;
            tacticalScroll.vertical = true;
            tacticalScroll.movementType = ScrollRect.MovementType.Clamped;
            tacticalScroll.scrollSensitivity = RowPitch;
            tacticalScroll.inertia = false;

            var track = new GameObject("TacticalScrollTrack", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            tacticalScrollTrack = track.GetComponent<RectTransform>();
            tacticalScrollTrack.SetParent(parent, false);
            track.GetComponent<Image>().color = WingUi.BorderSubtle;
            var thumb = new GameObject("Thumb", typeof(RectTransform), typeof(Image));
            var thumbRect = thumb.GetComponent<RectTransform>();
            thumbRect.SetParent(tacticalScrollTrack, false);
            Stretch(thumbRect);
            thumb.GetComponent<Image>().color = WingUi.RailEmerald;
            var scrollbar = track.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = thumbRect;
            scrollbar.targetGraphic = thumb.GetComponent<Image>();
            tacticalScroll.verticalScrollbar = scrollbar;
            tacticalScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            float y = AddSummary(tacticalContent, 0f);
            y = AddRosterArea(tacticalContent, y);
            tacticalCommandsTop = y;
            BuildFlightGroups(tacticalContent);
            tacticalCommands = PageRoot(tacticalContent, "TacticalCommands");
            float end = AddEngagementSection(tacticalCommands, 0f);
            end = AddActions(tacticalCommands, end);
            end = AddFlightGeometry(tacticalCommands, end);
            Place(tacticalCommands, new Rect(0f, y, PanelWidth, -end));
            tacticalCollapsedHeight = -y - end;
            ReflowTactical();
            return top - tacticalCollapsedHeight;
        }

        private static void FitTacticalViewport()
        {
            if (tacticalViewport == null) return;
            float height = panelHeight + tacticalTop - Pad - StatusStripHeight - Space2;
            Place(tacticalViewport, new Rect(0f, tacticalTop, PanelWidth, Mathf.Max(RowHeight, height)));
            Place(tacticalScrollTrack, new Rect(PanelWidth - 9f, tacticalTop, 5f, Mathf.Max(RowHeight, height)));
        }

        private static void ToggleRosterExpanded()
        {
            int first = rosterPage * (rosterExpanded ? ExpandedRosterRows : RosterRowsPerPage);
            rosterExpanded = !rosterExpanded;
            rosterPage = first / (rosterExpanded ? ExpandedRosterRows : RosterRowsPerPage);
            if (!rosterExpanded) CloseFlightGroupEditor();
            ReflowTactical();
            if (Wing() != null) RefreshTactical(Wing());
        }

        private static void ReflowTactical()
        {
            float extra = rosterExpanded ? (ExpandedRosterRows - RosterRowsPerPage) * RowPitch : 0f;
            float groupHeight = rosterExpanded ? FlightGroupsHeight : 0f;
            if (flightGroupsRoot != null)
            {
                flightGroupsRoot.gameObject.SetActive(rosterExpanded);
                Place(flightGroupsRoot, new Rect(0f, tacticalCommandsTop - extra, PanelWidth, groupHeight));
            }
            if (rosterArea != null)
                rosterArea.sizeDelta = new Vector2(rosterArea.sizeDelta.x, RowPitch * RosterRowsPerPage + extra);
            if (tacticalCommands != null)
                tacticalCommands.anchoredPosition = new Vector2(0f, tacticalCommandsTop - extra - groupHeight);
            if (tacticalContent != null)
                tacticalContent.sizeDelta = new Vector2(PanelWidth, tacticalCollapsedHeight + extra + groupHeight);
            if (tacticalScroll != null)
            {
                tacticalScroll.StopMovement();
                tacticalScroll.verticalNormalizedPosition = 1f;
            }
        }

        private static float AddTacticalTabs(RectTransform parent, float y, string[] labels,
            RectTransform[] roots, WingButton[] tabs, Action<int> select)
        {
            float w = TacticalCellWidth;
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                tabs[i] = WingUi.Button(parent, labels[i], new Rect(Pad + i * (w + Gap), y, w, RowHeight),
                    FontSmall, UiButtonStyle.Tab, () => select(index));
                roots[i] = PageRoot(parent, labels[i]);
            }
            return y - RowHeight - Gap;
        }

        private static void SetOrderPage(int index)
        {
            orderPage = index;
            for (int i = 0; i < orderPages.Length; i++)
            {
                orderPages[i]?.gameObject.SetActive(i == index);
                orderTabs[i]?.SetLatched(i == index);
            }
            // A hidden armed button must not leave a surprising map-click action behind.
            WingCommandManager.Instance?.CancelMapOrder(notify: false);
            nextRefresh = 0f;
        }

        private static void SetGeometryPage(int index)
        {
            geometryPage = index;
            for (int i = 0; i < geometryPages.Length; i++)
            {
                geometryPages[i]?.gameObject.SetActive(i == index);
                geometryTabs[i]?.SetLatched(i == index);
            }
            nextRefresh = 0f;
        }

        private static float AddRouteControls(RectTransform parent, float y)
        {
            float w = TacticalCellWidth;
            patrolButton = TacticalButton(parent, "PATROL OFF", Pad, y, w,
                () => WingCommandManager.Instance?.SetPatrolRoute(!ScopeAllPatrolling()), UiButtonStyle.Toggle)
                .WithTooltip("Queue at least two Move points with Shift-right-click, then enable PATROL to loop them. Turning it off finishes the remaining route once.");
            autoRefitButton = TacticalButton(parent, "AUTO REFIT OFF", Pad + w + Gap, y, w,
                () => WingCommandManager.Instance?.SetAutoRefit(!ScopeAllAutoRefit()), UiButtonStyle.Toggle)
                .WithTooltip("Selected aircraft refuel/rearm at bingo or empty combat stores, then resume their task. Skips deliberate land, cargo and retreat tasks. Requires AutoReturnOnEmpty in settings.");
            y -= TacticalButtonHeight + Gap;
            return y;
        }

        private static bool ScopeAllPatrolling() => ScopeAll(member => member.PatrolRoute);
        private static bool ScopeAllAutoRefit() => ScopeAll(member => member.AutoRefit);
        private static bool ScopeAll(Func<WingMember, bool> predicate)
        {
            var scope = WingCommandManager.Instance?.Commands.Scope(wholeWing: false);
            if (scope == null || scope.Count == 0) return false;
            foreach (WingMember member in scope) if (!predicate(member)) return false;
            return true;
        }

        private static void RefreshTacticalNavigation(WingRegistry wing)
        {
            var manager = WingCommandManager.Instance;
            if (manager == null) return;
            var scope = manager.Commands.Scope(wholeWing: false);
            RefreshFlightGroups(wing, scope);
            bool allPatrol = scope.Count > 0, allRefit = scope.Count > 0;
            bool anyPatrol = false, anyRefit = false, canPatrol = false, canRefit = false;
            foreach (WingMember member in scope)
            {
                allPatrol &= member.PatrolRoute;
                allRefit &= member.AutoRefit;
                anyPatrol |= member.PatrolRoute;
                anyRefit |= member.AutoRefit;
                canPatrol |= member.CanPatrolRoute || member.PatrolRoute;
                canRefit |= !member.IsSurface;
            }
            patrolButton?.SetText("PATROL " + (allPatrol ? "ON" : anyPatrol ? "MIXED" : "OFF"));
            patrolButton?.SetLatched(allPatrol);
            patrolButton?.SetEnabled(canPatrol);
            autoRefitButton?.SetText("AUTO REFIT " + (allRefit ? "ON" : anyRefit ? "MIX" : "OFF"));
            autoRefitButton?.SetLatched(allRefit);
            autoRefitButton?.SetEnabled(canRefit && Plugin.Settings.AutoReturnOnEmpty.Value);
            if (routeLabel != null)
                routeLabel.text = scope.Count == 1 && scope[0].RefitPending
                    ? "REFITTING - TASK SAVED FOR RELAUNCH"
                    : scope.Count == 1
                    ? scope[0].Route.Count + " POINTS | ALT " + scope[0].ResolvedMoveAltitude.ToString("0") +
                      " m | SPD " + (scope[0].ResolvedMoveSpeed * 100f).ToString("0") + "%"
                    : "Right-click: Move | Shift-right-click: add point";
            for (int i = 0; i < maneuverButtons.Length; i++)
            {
                bool enabled = false;
                foreach (WingMember member in scope)
                    enabled |= WingOrderCatalog.CanApply(member, WingOrder.Maneuver) &&
                        (!WingRegistry.IsRotary(member.Aircraft) || ManeuverCatalog.RotaryCapable(TacticalManeuvers[i]));
                maneuverButtons[i]?.SetEnabled(enabled);
            }
        }

        private static void ResetTacticalNavigation()
        {
            rosterExpanded = false;
            orderPage = geometryPage = 0;
            tacticalViewport = tacticalContent = tacticalCommands = tacticalScrollTrack = null;
            tacticalScroll = null;
            rosterExpandButton = patrolButton = autoRefitButton = null;
            ResetFlightGroups();
            ResetTacticalPreview();
            routeLabel = null;
            formationButtons = null;
            Array.Clear(orderPages, 0, orderPages.Length);
            Array.Clear(orderTabs, 0, orderTabs.Length);
            Array.Clear(geometryPages, 0, geometryPages.Length);
            Array.Clear(geometryTabs, 0, geometryTabs.Length);
            Array.Clear(maneuverButtons, 0, maneuverButtons.Length);
        }
    }
}
