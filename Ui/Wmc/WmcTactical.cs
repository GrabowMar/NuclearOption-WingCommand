using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>TACTICAL (spec bezel v2 §5 TACTICAL), the 0.9 main page rebuilt: who orders go to (the scope row), the flight list
    /// with inline RTB · RDR · EJ · INSPECT, then ORDERS — the cue banner, doctrine, the order grid and the situation. The scope
    /// row and the list stay put; ORDERS scrolls on its own and keeps its place across refreshes.</summary>
    internal sealed partial class WmcTactical : IWmcPage
    {
        private readonly Dictionary<string, AvButton> ids;
        private readonly WmcScopeRow scope;
        private RectTransform page, subArea;
        private Rect body;
        private float x, width, listTop, bottom, panelWidth, listHeight = -1f;
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

            subArea = Container(page, "TacticalOrders", new Rect(0f, listTop, panelWidth, 10f));
            BuildOrders(subArea);
            Layout(BezelLayout.RowPitch);
        }

        private static RectTransform Container(RectTransform parent, string name, Rect r)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            AvKit.Place(rt, r);
            return rt;
        }

        /// <summary>The flight list took <paramref name="height"/> px: ORDERS follows it.</summary>
        private void Layout(float height)
        {
            if (Mathf.Abs(height - listHeight) < 0.5f) return;
            listHeight = height;
            float top = listTop - height - BezelLayout.ScopeGap;
            AvKit.Place(subArea, new Rect(0f, top, panelWidth, top - bottom));
            LayoutOrders(top - bottom);
        }

        private static bool InScope(WmcContext c, in SnapshotMember m) => c.InScope(m);

        private void PickElement(int e) => scope.PickElement(e);

        // ---------------------------------------------------------------- refresh

        public void Shown(WmcContext c)
        {
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            scope.Refresh(c);
            RefreshList(c);
            RefreshOrders(c);
        }
    }
}
