using System.Collections.Generic;
using TMPro;
using UnityEngine;
using NOAvionics.Ui;

namespace WingCommand
{
    internal static partial class WmcScreen
    {
        private const int NodeRows = 4;
        private static readonly TMP_Text[] nodeLabels = new TMP_Text[NodeRows];
        private static TMP_Text nodePageLabel;
        private static WingButton nodePrevious, nodeNext, skipNode, clearNodes;
        private static WingMember routeInspected;
        private static int nodePage;

        private static float AddRouteNodesDeck(RectTransform parent, float y)
        {
            y = Heading(parent, y, "ROUTE / SELECTED AIRCRAFT");
            y = AddRouteControls(parent, y);
            Hint(parent, y, "Right-click: replace route. Shift-right-click: append a node.");
            y -= LineHeight + Gap;
            y = Heading(parent, y, "QUEUED NODES / CURRENT LEG FIRST");
            for (int i = 0; i < NodeRows; i++)
            {
                float rowY = y - i * RowPitch;
                WingUi.TacticalCard(parent, new Rect(Pad, rowY, PanelWidth - Pad * 2f, RowHeight), WingUi.RailCyan);
                AddSprite(parent, "NodeIcon" + i, IconFactory.Get("move"), new Rect(Pad + 6f, rowY - 7f, 16f, 16f), Dim());
                nodeLabels[i] = Label(parent, "", new Rect(Pad + 28f, rowY, PanelWidth - Pad * 2f - 36f, RowHeight),
                    Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            }
            y -= NodeRows * RowPitch + Gap;
            nodePrevious = Pager(parent, y, "<", () => { nodePage = Mathf.Max(0, nodePage - 1); nextRefresh = 0f; });
            nodePageLabel = PagerLabel(parent, y);
            nodeNext = Pager(parent, y, ">", () => { nodePage++; nextRefresh = 0f; });
            y -= RowHeight + Gap;
            float half = (PanelWidth - Pad * 2f - Gap) * 0.5f;
            skipNode = TacticalButton(parent, "SKIP CURRENT", Pad, y, half, () => EditInspectedRoute(false))
                .WithTooltip("Complete the current node immediately. Continue to the next node, or form up when the route ends.");
            clearNodes = TacticalButton(parent, "CLEAR / FORM UP", Pad + half + Gap, y, half, () => EditInspectedRoute(true))
                .WithTooltip("Cancel this aircraft's route and return to formation. Select exactly one aircraft first.");
            return y - TacticalButtonHeight - Gap;
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
                if (index >= count)
                {
                    nodeLabels[i].text = i == 0 ? (member == null ? "Choose a flight row above" :
                        member.RefitPending ? "REFITTING / ROUTE SAVED" : "No route. Right-click the map to begin.") : "";
                    continue;
                }
                WingDirective node = member.Route[index];
                nodeLabels[i].text = (index == 0 ? "> " : "  ") + (index + 1) + "  " + WingOrderCatalog.Label(node.Order) +
                    (node.Target != null ? " / " + node.Target.unitName : node.HasPoint ? " / MAP POINT" : "");
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
            nodePageLabel = null;
            nodePrevious = nodeNext = skipNode = clearNodes = null;
            System.Array.Clear(nodeLabels, 0, nodeLabels.Length);
        }
    }
}
