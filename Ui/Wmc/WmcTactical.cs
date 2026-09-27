using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace WingCommand
{
    /// <summary>TACTICAL (spec bezel v2 §5 TACTICAL), the 0.9 main page rebuilt dense: who orders go to (the scope row), the flight
    /// list with inline RTB · RDR · EJ · INSPECT, the cue row, DOCTRINE, the order grid and REACT — every one at a fixed place, the
    /// list reserving its tallest so a wingman joining, leaving or splitting moves nothing under the cursor (critic §14.1) — then one
    /// scroll with the alerts, the situation and RECENT.</summary>
    internal sealed partial class WmcTactical : IWmcPage
    {
        private readonly Dictionary<string, AvButton> ids;
        private readonly WmcScopeRow scope;
        private RectTransform page;
        private Rect body;
        private float x, width, listTop, bottom, panelWidth;
        private int reservedFor = -1;
        private bool doctrineOpen, placed;
        private WmcContext last;

        public WmcTactical(Dictionary<string, AvButton> controls)
        {
            ids = controls;
            scope = new WmcScopeRow(controls, "tac.scope.");
        }

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
            scope.Build(page, x, body.y, width, "COMMAND", true);
            listTop = body.y - BezelLayout.ScopeRow - BezelLayout.ScopeGap;
            BuildList();
            BuildCue();
            BuildDoctrine();
            BuildGrid();
            BuildSituation();
            doctrineOpen = BezelLayout.DoctrineOpenByDefault(body.height, WingService.MaxMembers);
            PlaceBlocks();
        }

        private static RectTransform Container(RectTransform parent, string name, Rect r)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            AvKit.Place(rt, r);
            return rt;
        }

        /// <summary>The cue, DOCTRINE, the grid and the scroll under the list's reserve; again only when the reserve (the wing's
        /// size setting) or DOCTRINE's toggle changed.</summary>
        private void PlaceBlocks()
        {
            int max = WingService.MaxMembers;
            if (placed && max == reservedFor && doctrineOpen == doctrineShown) return;
            placed = true;
            reservedFor = max;
            doctrineShown = doctrineOpen;
            float y = listTop - BezelLayout.ListReserve(max) - BezelLayout.ListGap;
            AvKit.Place(cueRoot, new Rect(x, y, width, BezelLayout.Cue));
            y -= BezelLayout.Cue + BezelLayout.CueGap;
            AvKit.Place(doctrineRoot, new Rect(x, y, width, BezelLayout.DoctrineBlock(true)));
            doctrineRows.SetActive(doctrineOpen);
            y -= BezelLayout.DoctrineBlock(doctrineOpen);
            AvKit.Place(gridRoot, new Rect(x, y, width, BezelLayout.GridBlock));
            y -= BezelLayout.GridBlock;
            situationScroll.SetViewport(new Rect(x, y, width + 8f, Mathf.Max(20f, y - bottom)));
            recentKey = int.MinValue;
            situationLayout = int.MinValue;
        }

        private bool doctrineShown;

        private static bool InScope(WmcContext c, in SnapshotMember m) => c.InScope(m);

        private void PickElement(int e) => scope.PickElement(e);

        public void Shown(WmcContext c)
        {
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            PlaceBlocks();
            scope.Refresh(c);
            RefreshList(c);
            RefreshOrders(c);
            RefreshSituation(c);
        }
    }
}
