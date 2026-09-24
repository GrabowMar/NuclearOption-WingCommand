using System.Collections.Generic;
using NOAvionics.Ui;
using UnityEngine;

namespace WingCommand
{
    internal sealed class WmcOrdersTab : IWmcTab
    {
        public WmcOrdersTab(Dictionary<string, AvButton> controls) { }
        public string Hint => "";
        public void Build(RectTransform page, Rect body) { }
        public void Refresh(WmcContext c) { }
    }
}
