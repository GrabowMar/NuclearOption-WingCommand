namespace WingCommand
{
    /// <summary>The room's footer line (spec WMC program §6, review P5 I3): the room covers the bezel's status strip and the
    /// game's messages, so it says what the pointer is on, else a toast from the last few seconds, else the page's help.</summary>
    internal static class RoomFooter
    {
        public const float ToastSeconds = 6f;

        public static string Text(string tooltip, string toast, float toastAge, string hint)
        {
            if (!string.IsNullOrEmpty(tooltip)) return tooltip;
            if (!string.IsNullOrEmpty(toast) && toastAge <= ToastSeconds) return toast;
            return hint ?? "";
        }
    }
}
