namespace WingCommand
{
    /// <summary>Short feedback through the game's own message feed.</summary>
    internal static class WingToast
    {
        public static void Show(string text)
        {
            MessageUI ui = SceneSingleton<MessageUI>.i;
            if (ui != null) ui.GameMessage(text);
            Plugin.LogVerbose("[Toast] " + text);
        }
    }
}
