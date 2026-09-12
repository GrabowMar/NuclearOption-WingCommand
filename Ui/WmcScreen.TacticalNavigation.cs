using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NOAvionics.Ui;

namespace WingCommand
{
    internal static partial class WmcScreen
    {
        private const float TacticalButtonHeight = 28f;
        private const float TacticalGap = 4f;
        private const float TacticalCellWidth = (PanelWidth - Pad * 2f - Gap * 3f) / 4f;
        private static float TacticalColumn(int column) => Pad + column * (TacticalCellWidth + Gap);
        internal static bool TacticalFlightExpanded => TacticalCommandModeActive && rosterExpanded;
        private const int ExpandedRosterRows = 6;
        private static bool rosterExpanded;
        private static WingButton rosterExpandButton;
        private static RectTransform tacticalViewport, tacticalContent, tacticalCommands, tacticalScrollTrack;
        private static ScrollRect tacticalScroll;
        private static float tacticalTop, tacticalCommandsTop, tacticalCollapsedHeight;
        private static readonly RectTransform[] tacticalDeckPages = new RectTransform[3];
        private static readonly WingButton[] tacticalDeckTabs = new WingButton[3];
        private static readonly float[] tacticalDeckBottoms = new float[3];
        private static int tacticalDeck;
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
            AvInput.StripNavigation(scrollbar);
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
            float end = AddTacticalDecks(tacticalCommands, 0f);
            Place(tacticalCommands, new Rect(0f, y, PanelWidth, -end));
            tacticalCollapsedHeight = -y - end;
            ReflowTactical();
            // Reserve the tallest compact deck so switching tabs does not introduce overflow.
            return top + y + Mathf.Min(tacticalDeckBottoms);
        }

        private static void FitTacticalViewport()
        {
            if (tacticalViewport == null) return;
            float height = panelHeight + tacticalTop - Pad - StatusStripHeight - Space2;
            Place(tacticalViewport, new Rect(0f, tacticalTop, PanelWidth, Mathf.Max(RowHeight, height)));
            Place(tacticalScrollTrack, new Rect(PanelWidth - 12f, tacticalTop, 8f, Mathf.Max(RowHeight, height)));
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

        private static float AddTacticalDecks(RectTransform parent, float y)
        {
            float deckTabWidth = (PanelWidth - Pad * 2f - Gap * 2f) / 3f;
            tacticalDeckTabs[0] = WingUi.Button(parent, "DIRECTIVES",
                new Rect(Pad, y, deckTabWidth, RowHeight), FontSmall, UiButtonStyle.Tab,
                () => SetTacticalDeck(0));
            tacticalDeckTabs[1] = WingUi.Button(parent, "GEOMETRY",
                new Rect(Pad + deckTabWidth + Gap, y, deckTabWidth, RowHeight), FontSmall, UiButtonStyle.Tab,
                () => SetTacticalDeck(1));
            tacticalDeckTabs[2] = WingUi.Button(parent, "ROUTE / NODES",
                new Rect(Pad + (deckTabWidth + Gap) * 2f, y, deckTabWidth, RowHeight), FontSmall, UiButtonStyle.Tab,
                () => SetTacticalDeck(2));
            string[] icons = { "tasking", "formation", "move" };
            for (int i = 0; i < tacticalDeckTabs.Length; i++)
            {
                AddSprite((RectTransform)tacticalDeckTabs[i].transform, "DeckIcon", IconFactory.Get(icons[i]),
                    new Rect(6f, -7f, 16f, 16f), WingUi.RailCyan);
                tacticalDeckTabs[i].GetComponentInChildren<TMP_Text>().margin = new Vector4(24f, 0f, 4f, 0f);
            }
            y -= RowHeight + Gap;

            tacticalDeckPages[0] = PageRoot(parent, "DeckDirectives");
            tacticalDeckPages[1] = PageRoot(parent, "DeckGeometryRoute");
            tacticalDeckPages[2] = PageRoot(parent, "DeckRouteNodes");

            tacticalDeckBottoms[0] = AddDirectivesDeck(tacticalDeckPages[0], y);
            tacticalDeckBottoms[1] = AddGeometryDeck(tacticalDeckPages[1], y);
            tacticalDeckBottoms[2] = AddRouteNodesDeck(tacticalDeckPages[2], y);

            SetTacticalDeck(tacticalDeck);
            return tacticalDeckBottoms[tacticalDeck];
        }

        private static void SetTacticalDeck(int index)
        {
            index = Mathf.Clamp(index, 0, tacticalDeckPages.Length - 1);
            tacticalDeck = index;
            for (int i = 0; i < tacticalDeckPages.Length; i++)
            {
                tacticalDeckPages[i]?.gameObject.SetActive(i == index);
                tacticalDeckTabs[i]?.SetLatched(i == index);
            }
            float bottom = tacticalDeckBottoms[index];
            if (tacticalCommands != null && bottom < 0f)
            {
                tacticalCommands.sizeDelta = new Vector2(PanelWidth, -bottom);
                tacticalCollapsedHeight = -tacticalCommandsTop - bottom;
                ReflowTactical();
            }
            // A hidden armed button must not leave a surprising map-click action behind.
            WingCommandManager.Instance?.CancelMapOrder(notify: false);
            nextRefresh = 0f;
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
            RefreshRouteNodes(scope);
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
            tacticalDeck = 0;
            tacticalViewport = tacticalContent = tacticalCommands = tacticalScrollTrack = null;
            tacticalScroll = null;
            rosterExpandButton = patrolButton = autoRefitButton = null;
            ResetFlightGroups();
            ResetTacticalPreview();
            ResetTacticalBento();
            routeLabel = null;
            formationButtons = null;
            ResetRouteNodes();
            Array.Clear(tacticalDeckPages, 0, tacticalDeckPages.Length);
            Array.Clear(tacticalDeckTabs, 0, tacticalDeckTabs.Length);
            Array.Clear(tacticalDeckBottoms, 0, tacticalDeckBottoms.Length);
            Array.Clear(maneuverButtons, 0, maneuverButtons.Length);
        }
    }
}
