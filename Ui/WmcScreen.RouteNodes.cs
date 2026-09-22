using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NOAvionics.Ui;

namespace WingCommand
{
    internal static partial class WmcScreen
    {
        private const int NodeRows = 4;
        private const float RouteControlHeight = 22f;
        private static readonly TMP_Text[] nodeLabels = new TMP_Text[NodeRows];
        private static readonly RectTransform[] nodeRoots = new RectTransform[NodeRows];
        private static readonly Image[] nodeRails = new Image[NodeRows];
        private static TMP_Text nodePageLabel;
        private static WingButton nodePrevious, nodeNext, skipNode, clearNodes;
        private static WingMember routeInspected;
        private static int nodePage;
        private static float routeNodeRowHeight;

        private static float AddRouteNodesDeck(RectTransform parent, float y)
        {
            float avail = Mathf.Max(220f, tacticalDeckAvail);
            const float h = RouteControlHeight;
            routeLabel = Label(parent, "", new Rect(Pad, y, ContentWidth, 14f),
                Friendly(), FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            y -= 14f + TacticalGap;

            float sw = (ContentWidth - TacticalGap) * 0.5f;
            Stepper(parent, Pad, y, sw, out altValueLabel,
                () => WingCommandManager.Instance?.StepMoveHeight(-1),
                () => WingCommandManager.Instance?.StepMoveHeight(1),
                OrderHint.HeightDown + "  " + OrderHint.HeightUp, h);
            Stepper(parent, Pad + sw + TacticalGap, y, sw, out spdValueLabel,
                () => WingCommandManager.Instance?.StepMoveSpeed(-1),
                () => WingCommandManager.Instance?.StepMoveSpeed(1),
                OrderHint.SpeedDown + "  " + OrderHint.SpeedUp, h);
            y -= h + TacticalGap;

            patrolButton = TacticalButton(parent, "PATROL OFF", Pad, y, sw,
                () => WingCommandManager.Instance?.SetPatrolRoute(!ScopeAllPatrolling()))
                .WithTooltip("Queue at least two Move points with Shift-left-click, then enable PATROL to loop them. Turning it off finishes the remaining route once.");
            autoRefitButton = TacticalButton(parent, "AUTO REFIT OFF", Pad + sw + TacticalGap, y, sw,
                () => WingCommandManager.Instance?.SetAutoRefit(!ScopeAllAutoRefit()))
                .WithTooltip("Selected aircraft refuel and rearm at bingo or empty stores, then resume their task. Skips deliberate land, cargo and retreat tasks.");
            y -= h + TacticalGap;

            // Pin the pager and actions to the viewport foot so the node list fills what remains.
            // The action buttons are taller than the pager row; their foot must land on the
            // content bottom or the bottom scroll position masks them.
            float foot = -avail;
            float buttonsY = foot + TacticalButtonHeight;
            float pagerY = buttonsY + TacticalGap + h;
            float listSpace = y - (pagerY + TacticalGap);
            float pitch = Mathf.Max(h + 2f, listSpace / NodeRows);
            routeNodeRowHeight = pitch - TacticalGap;

            for (int i = 0; i < NodeRows; i++)
            {
                float rowY = y - i * pitch;
                nodeRoots[i] = PageRoot(parent, "RouteNode" + i);
                Place(nodeRoots[i], new Rect(Pad, rowY, ContentWidth, routeNodeRowHeight));
                var (_, rail) = WingUi.TacticalCard(nodeRoots[i],
                    new Rect(0f, 0f, ContentWidth, routeNodeRowHeight), WingUi.RailInert);
                nodeRails[i] = rail;
                nodeLabels[i] = Label(nodeRoots[i], "",
                    new Rect(Space2, 0f, ContentWidth - Space2 * 2f, routeNodeRowHeight),
                    WingUi.TextPrimary, FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            }

            RectTransform pagerRoot = PageRoot(parent, "RoutePager");
            Place(pagerRoot, new Rect(Pad, pagerY, ContentWidth, h));
            (nodePrevious, nodePageLabel, nodeNext) = PagerRow(pagerRoot, 0f, ContentWidth,
                () => { nodePage = Mathf.Max(0, nodePage - 1); nextRefresh = 0f; },
                () => { nodePage++; nextRefresh = 0f; },
                OrderHint.Pager, h);

            float half = (ContentWidth - TacticalGap) * 0.5f;
            skipNode = TacticalButton(parent, "SKIP CURRENT", Pad, buttonsY, half, () => EditInspectedRoute(false))
                .WithTooltip("Complete the current node immediately. Continue to the next node, or form up when the route ends.");
            clearNodes = TacticalButton(parent, "CLEAR / FORM UP", Pad + half + TacticalGap, buttonsY, half,
                () => EditInspectedRoute(true))
                .WithTooltip("Cancel this aircraft's route and return to formation. Select exactly one aircraft first.");
            return foot;
        }

        private static void RefreshRouteNodes(List<WingMember> scope)
        {
            if (nodePageLabel == null) return;
            WingMember member = scope.Count == 1 ? scope[0] : null;
            if (member != routeInspected) { routeInspected = member; nodePage = 0; }
            int count = member?.Route.Count ?? 0;
            int pages = Mathf.Max(1, Mathf.CeilToInt(count / (float)NodeRows));
            nodePage = Mathf.Clamp(nodePage, 0, pages - 1);
            nodePageLabel.text = member == null ? "SELECT ONE AIRCRAFT TO INSPECT" :
                member.Name + " / " + count + " NODES / " + (nodePage + 1) + "/" + pages;
            nodePrevious.SetEnabled(nodePage > 0);
            nodeNext.SetEnabled(nodePage + 1 < pages);
            bool editable = (WingCommandManager.Instance?.CanControlAircraft(member) ?? false) &&
                !member.RefitPending && count > 0;
            skipNode.SetEnabled(editable);
            clearNodes.SetEnabled(editable);
            for (int i = 0; i < NodeRows; i++)
            {
                int index = nodePage * NodeRows + i;
                nodeRoots[i].gameObject.SetActive(index < count || i == 0);
                nodeRails[i].color = index == 0 && count > 0 ? Green() : WingUi.RailInert;
                if (index >= count)
                {
                    nodeLabels[i].text = i == 0 ? (member == null ? "Choose a flight row above" :
                        member.RefitPending ? "REFITTING / ROUTE SAVED" : "No route. Left-click the map to begin.") : "";
                    continue;
                }
                WingDirective node = member.Route[index];
                string text = (index == 0 ? "CURRENT · " : "QUEUED · ") + (index + 1) + "  " +
                    WingOrderCatalog.Label(node.Order) +
                    (node.Target != null ? " / " + node.Target.unitName : node.HasPoint ? " / MAP POINT" : "");
                if (routeNodeRowHeight >= 44f)
                {
                    string detail;
                    if (node.HasPoint)
                    {
                        Vector3 point = node.Point.AsVector3();
                        float range = member.Aircraft != null
                            ? Vector3.Distance(member.Aircraft.transform.position, point) : 0f;
                        detail = "RNG " + TacticalBentoRules.FormatDistance(range) +
                            " · ALT " + TacticalBentoRules.FormatAltitude(point.y);
                    }
                    else
                    {
                        detail = "ALT " + TacticalBentoRules.FormatAltitude(member.ResolvedMoveAltitude);
                    }
                    text += "\n<size=10>" + detail + " · SPD " + (member.ResolvedMoveSpeed * 100f).ToString("0") + "%</size>";
                }
                nodeLabels[i].text = text;
            }
        }

        private static void EditInspectedRoute(bool clear)
        {
            var manager = WingCommandManager.Instance;
            if (manager == null || !manager.CanControlAircraft(routeInspected) || routeInspected.RefitPending) return;
            var scope = manager.Commands.Scope(wholeWing: false);
            if (scope.Count != 1 || scope[0] != routeInspected || routeInspected.Route.Count == 0) return;
            manager.CancelMapOrder(notify: false);
            if (clear || !routeInspected.TryAdvanceQueue(routeInspected.OrderRevision))
                routeInspected.Apply(WingOrder.Formation);
            manager.Toast(routeInspected.Name + (clear ? ": route cleared; forming up" : ": current node skipped"));
            nextRefresh = 0f;
        }

        private static void ResetRouteNodes()
        {
            routeInspected = null;
            nodePage = 0;
            routeNodeRowHeight = 0f;
            nodePageLabel = null;
            nodePrevious = nodeNext = skipNode = clearNodes = null;
            System.Array.Clear(nodeLabels, 0, nodeLabels.Length);
            System.Array.Clear(nodeRoots, 0, nodeRoots.Length);
            System.Array.Clear(nodeRails, 0, nodeRails.Length);
        }
    }
}
