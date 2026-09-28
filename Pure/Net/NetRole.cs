namespace WingCommand
{
    /// <summary>Which end of a session this game is (review M7b-1 I3): a client runs no server; single player and a host
    /// run both.</summary>
    internal static class NetRole
    {
        public static bool ClientOnly(bool serverActive, bool clientActive) => clientActive && !serverActive;
    }
}
