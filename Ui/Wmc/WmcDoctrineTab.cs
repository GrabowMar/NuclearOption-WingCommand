using System.Collections.Generic;
using NOAvionics.Ui;
using UnityEngine;

namespace WingCommand
{
    internal sealed class WmcDoctrineTab : IWmcTab
    {
        public WmcDoctrineTab(Dictionary<string, AvButton> controls) { }
        public string Hint => "";
        public void Build(RectTransform page, Rect body) { }
        public void Refresh(WmcContext c) { }
    }
}
