using UnityEngine;

namespace WingCommand
{
    /// <summary>One notch of the Wing Command room (spec WMC program §6). Built once into the room's body; shown, refreshed at
    /// ≤ 6 Hz and ticked every frame only while it is the open page.</summary>
    internal interface IRoomPage
    {
        /// <summary>The footer's help line for the page.</summary>
        string Hint { get; }

        void Build(RectTransform body, Rect area);

        void Show(WmcContext c);

        void Hide();

        void Refresh(WmcContext c);

        void Tick(WmcContext c);
    }
}
