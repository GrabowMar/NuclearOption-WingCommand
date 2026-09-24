using UnityEngine;

namespace WingCommand
{
    /// <summary>Short feedback through the game's own message feed; the last line is kept for WMC's status strip.</summary>
    internal static class WingToast
    {
        public static string Last { get; private set; }
        public static float LastAt { get; private set; } = float.NegativeInfinity;

        public static void Show(string text)
        {
            Last = text;
            LastAt = Time.unscaledTime;
            MessageUI ui = SceneSingleton<MessageUI>.i;
            if (ui != null) ui.GameMessage(text);
            Plugin.LogVerbose("[Toast] " + text);
        }
    }
}
