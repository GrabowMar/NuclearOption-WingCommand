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
        private const float TacticalGap = 3f;
        private const float TacticalRosterHeight = 28f;
        private const float DeckTabHeight = 26f;
        private const float TacticalRosterPitch = TacticalRosterHeight + TacticalGap;
        internal static bool TacticalFlightExpanded => TacticalCommandModeActive && rosterExpanded;
        private const int ExpandedRosterRows = 6;
        private static bool rosterExpanded;
        private static WingButton rosterExpandButton;
        private static RectTransform tacticalViewport, tacticalContent, tacticalScrollTrack;
        private static ScrollRect tacticalScroll;
        private static float tacticalFlightBottom, tacticalDeckHeight;
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
        private static TMP_Text altValueLabel, spdValueLabel;
        private static float tacticalDeckAvail;

        private static WingButton TacticalButton(RectTransform parent, string text, float x, float y,
            float width, Action action, UiButtonStyle style = UiButtonStyle.Default) =>
            WingUi.Button(parent, text, new Rect(x, y, width, TacticalButtonHeight), FontSmall, style, action);

        private static float BuildTacticalPage(RectTransform parent, float top)
        {
            float bodyH = Mathf.Max(RowHeight, panelHeight + top - Pad - StatusStripHeight - Space2);

            // The flight stays put. Only the deck below it scrolls.
            float y = AddScopeBar(parent, top);
            y = AddRosterArea(parent, y);
            tacticalFlightBottom = y;
            BuildFlightGroups(parent);

            float bodyBottom = top - bodyH;
            // Take exactly the space the fixed flight leaves above the body foot. A floor
            // here used to push the deck viewport over the pinned strip on short panels.
            tacticalDeckHeight = Mathf.Max(0f, y - bodyBottom);
            MountDeck(parent, y, tacticalDeckHeight);
            ReflowTactical();
            return bodyBottom;
        }

        private static void MountDeck(RectTransform parent, float top, float height)
        {
            float tabW = (ContentWidth - Gap * 2f) / 3f;
            float tabH = DeckTabHeight;
            tacticalDeckTabs[0] = WingUi.Button(parent, "ORDERS",
                new Rect(Pad, top, tabW, tabH), FontMicro, UiButtonStyle.Tab, () => SetTacticalDeck(0));
            tacticalDeckTabs[1] = WingUi.Button(parent, "FORMATION",
                new Rect(Pad + tabW + Gap, top, tabW, tabH), FontMicro, UiButtonStyle.Tab, () => SetTacticalDeck(1));
            tacticalDeckTabs[2] = WingUi.Button(parent, "ROUTE",
                new Rect(Pad + (tabW + Gap) * 2f, top, tabW, tabH), FontMicro, UiButtonStyle.Tab, () => SetTacticalDeck(2));

            float viewTop = top - tabH - TacticalGap;
            float viewH = Mathf.Max(1f, height - tabH - TacticalGap);
            // Pages size themselves to this viewport so the resting flight does not scroll.
            tacticalDeckAvail = viewH;
            tacticalViewport = BuildViewport(parent, new Rect(0f, viewTop, PageWidth, viewH),
                "TacticalViewport", out tacticalContent, out tacticalScroll);
            tacticalScrollTrack = parent.Find("TacticalViewportScrollTrack") as RectTransform;
            pageScrolls[(int)Page.Tactical] = tacticalScroll;

            tacticalDeckPages[0] = PageRoot(tacticalContent, "DeckOrders");
            tacticalDeckPages[1] = PageRoot(tacticalContent, "DeckFormation");
            tacticalDeckPages[2] = PageRoot(tacticalContent, "DeckRoute");
            tacticalDeckBottoms[0] = AddDirectivesDeck(tacticalDeckPages[0], 0f);
            tacticalDeckBottoms[1] = AddGeometryDeck(tacticalDeckPages[1], 0f);
            tacticalDeckBottoms[2] = AddRouteNodesDeck(tacticalDeckPages[2], 0f);
            SetTacticalDeck(tacticalDeck);
        }

        private static void FitTacticalViewport() => ReflowTactical();

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
            float extra = rosterExpanded ? (ExpandedRosterRows - RosterRowsPerPage) * TacticalRosterPitch : 0f;
            float groupHeight = rosterExpanded ? FlightGroupsHeight : 0f;
            float rosterH = TacticalRosterPitch * RosterRowsPerPage + extra;
            if (rosterArea != null)
                rosterArea.sizeDelta = new Vector2(rosterArea.sizeDelta.x, rosterH);
            if (flightGroupsRoot != null)
            {
                flightGroupsRoot.gameObject.SetActive(rosterExpanded);
                Place(flightGroupsRoot, new Rect(0f, tacticalFlightBottom - extra, PageWidth, groupHeight));
            }

            float shift = extra + groupHeight;
            float tabW = (ContentWidth - Gap * 2f) / 3f;
            float tabTop = tacticalFlightBottom - shift;
            for (int i = 0; i < tacticalDeckTabs.Length; i++)
            {
                if (tacticalDeckTabs[i] == null) continue;
                Place((RectTransform)tacticalDeckTabs[i].transform,
                    new Rect(Pad + i * (tabW + Gap), tabTop, tabW, DeckTabHeight));
            }

            float viewTop = tabTop - DeckTabHeight - TacticalGap;
            float viewH = Mathf.Max(1f, tacticalDeckHeight - shift - DeckTabHeight - TacticalGap);
            // The deck viewport owns the body foot. On a body too short for tabs plus a
            // usable viewport it gives up its clearance rather than cover the status strip.
            if (viewTop - viewH < BodyBottom)
            {
                viewH = Mathf.Max(1f, viewTop - BodyBottom);
                viewTop = BodyBottom + viewH;
            }
            tacticalDeckAvail = viewH;
            if (tacticalViewport != null)
                Place(tacticalViewport, new Rect(0f, viewTop, PageWidth, viewH));
            if (tacticalScrollTrack != null)
                Place(tacticalScrollTrack, new Rect(PanelWidth - 5f, viewTop, 4f, viewH));

            float contentH = viewH;
            if (tacticalContent != null && tacticalDeck >= 0 && tacticalDeck < tacticalDeckBottoms.Length)
            {
                contentH = Mathf.Max(viewH, -tacticalDeckBottoms[tacticalDeck]);
                tacticalContent.sizeDelta = new Vector2(PageWidth, contentH);
            }
            bool overflow = contentH > viewH + 1f;
            if (tacticalScroll != null) tacticalScroll.vertical = overflow;
            if (tacticalScrollTrack != null) tacticalScrollTrack.gameObject.SetActive(overflow);
            ClampScrollOffset(tacticalScroll);
            SyncScrollbar(tacticalScroll);
        }

        private static void SetTacticalDeck(int index)
        {
            if (tacticalDeckPages[0] == null) return;
            index = Mathf.Clamp(index, 0, tacticalDeckPages.Length - 1);
            tacticalDeck = index;
            for (int i = 0; i < tacticalDeckPages.Length; i++)
            {
                tacticalDeckPages[i]?.gameObject.SetActive(i == index);
                tacticalDeckTabs[i]?.SetLatched(i == index);
            }
            ReflowTactical();
            // Preserve the visible command position across deck switches; the reflow above
            // already clamped it to the shorter deck's content.
            ClampScrollOffset(tacticalScroll);
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
            float alt = 0f, spd = 0f;
            bool altMixed = false, spdMixed = false;
            for (int i = 0; i < scope.Count; i++)
            {
                WingMember member = scope[i];
                allPatrol &= member.PatrolRoute;
                allRefit &= member.AutoRefit;
                anyPatrol |= member.PatrolRoute;
                anyRefit |= member.AutoRefit;
                canPatrol |= member.CanPatrolRoute || member.PatrolRoute;
                canRefit |= !member.IsSurface;
                if (i == 0)
                {
                    alt = member.ResolvedMoveAltitude;
                    spd = member.ResolvedMoveSpeed;
                }
                else
                {
                    altMixed |= !Mathf.Approximately(member.ResolvedMoveAltitude, alt);
                    spdMixed |= !Mathf.Approximately(member.ResolvedMoveSpeed, spd);
                }
            }
            patrolButton?.SetText("PATROL " + (allPatrol ? "ON" : anyPatrol ? "MIXED" : "OFF"));
            patrolButton?.SetLatched(allPatrol);
            patrolButton?.SetEnabled(canPatrol);
            bool autoReturn = Plugin.Settings == null || Plugin.Settings.AutoReturnOnEmpty.Value;
            autoRefitButton?.SetText("AUTO REFIT " + (allRefit ? "ON" : anyRefit ? "MIXED" : "OFF"));
            autoRefitButton?.SetLatched(allRefit);
            autoRefitButton?.SetEnabled(canRefit && autoReturn);
            autoRefitButton?.WithTooltip(autoReturn
                ? "Selected aircraft refuel and rearm at bingo or empty stores, then resume their task."
                : "AUTO REFIT is unavailable because Auto Return On Empty is off in settings.");
            if (altValueLabel != null)
                altValueLabel.text = scope.Count == 0 ? "ALT —"
                    : altMixed ? "ALT MIXED"
                    : "ALT " + alt.ToString("0") + " m";
            if (spdValueLabel != null)
                spdValueLabel.text = scope.Count == 0 ? "SPD —"
                    : spdMixed ? "SPD MIXED"
                    : "SPD " + (spd * 100f).ToString("0") + "%";
            if (routeLabel != null)
                routeLabel.text = scope.Count == 1 && scope[0].RefitPending
                    ? "REFITTING - TASK SAVED FOR RELAUNCH"
                    : scope.Count == 1
                    ? scope[0].Route.Count + " POINTS | ALT " + scope[0].ResolvedMoveAltitude.ToString("0") +
                      " m | SPD " + (scope[0].ResolvedMoveSpeed * 100f).ToString("0") + "%"
                    : "Left-click: Move | Shift-left-click: add point";
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
            tacticalViewport = tacticalContent = tacticalScrollTrack = null;
            tacticalScroll = null;
            rosterExpandButton = patrolButton = autoRefitButton = null;
            altValueLabel = spdValueLabel = null;
            rosterHeaderLabel = null;
            ordersCueRail = null;
            ordersCueLabel = null;
            tacticalDeckAvail = 0f;
            tacticalFlightBottom = tacticalDeckHeight = 0f;
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
