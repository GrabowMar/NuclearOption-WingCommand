using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace WingCommand
{
    /// <summary>TACTICAL (spec bezel v2 §5 TACTICAL), the 0.9 main page rebuilt dense: who orders go to (the scope row), the flight
    /// list with inline RTB · RDR · EJ · INSPECT, the cue row, the order grid and REACT — every one at a fixed place, the list
    /// reserving its tallest so a wingman joining, leaving or splitting moves nothing under the cursor (critic §14.1). DOCTRINE moved to
    /// BEHAVIOUR › OPTIONS and the situation scroll went on 2026-09-28 (the user's cut).</summary>
    internal sealed partial class WmcTactical : IWmcPage
    {
        private readonly Dictionary<string, AvButton> ids;
        private readonly WmcScopeRow scope;
        private RectTransform page;
        private Rect body;
        private float x, width, listTop;
        private int reservedFor = -1;
        private bool placed;
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
            body = WmcUi.Page(page, shellBody, shellBody.height);
            x = body.x;
            width = body.width;
            scope.Build(page, x, body.y, width, "COMMAND");
            listTop = body.y - BezelLayout.ScopeRow - BezelLayout.ScopeGap;
            BuildList();
            BuildCue();
            BuildGrid();
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

        /// <summary>The cue and the grid under the list's reserve; again only when the reserve (the wing's size setting) changed.</summary>
        private void PlaceBlocks()
        {
            int max = Reserve;
            if (placed && max == reservedFor) return;
            placed = true;
            reservedFor = max;
            float y = listTop - BezelLayout.ListReserve(max) - BezelLayout.ListGap;
            AvKit.Place(cueRoot, new Rect(x, y, width, BezelLayout.Cue));
            y -= BezelLayout.Cue + BezelLayout.CueGap;
            AvKit.Place(gridRoot, new Rect(x, y, width, BezelLayout.GridBlock));
        }

        /// <summary>The seats the list reserves: the wing's size setting, or more when the wing flies more (a client of a bigger wing,
        /// the setting lowered mid-mission).</summary>
        private int Reserve => Mathf.Max(WingService.MaxMembers, last != null ? last.Count : 0);

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
            // The alerts first: the cue row reads this refresh's.
            RefreshAlerts(c);
            RefreshOrders(c);
        }
    }
}
