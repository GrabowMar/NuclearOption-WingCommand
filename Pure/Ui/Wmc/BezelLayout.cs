namespace WingCommand
{
    /// <summary>The bezel's fixed geometry (spec WMC rebuild §bezel shell): the synced <c>AvScreen</c> chrome (data bar 62,
    /// metrics 76, tabs 34, status 56 and four 8 px gaps) leaves H − 260 for a page; TACTICAL stacks a scope row, the flight
    /// list, a sub-tab strip and the sub-page.</summary>
    internal static class BezelLayout
    {
        public const float Chrome = 260f;
        public const float ScopeRow = 26f, ScopeGap = 4f, HeaderPitch = 22f, RowPitch = 30f, Pager = 20f, SubTabs = 24f, SubGap = 6f;
        /// <summary>The body kept for the sub-page on a tall dock (at 896 the list gets 188 px: three wingmen in two elements
        /// with room to spare); short docks keep a header and three rows (paged beyond).</summary>
        public const float SubPageReserve = 448f, ShortListCap = 116f;

        public static float Body(float panelHeight) => panelHeight - Chrome;

        /// <summary>Pixels the flight list may take.</summary>
        public static float ListCap(float body) => body - SubPageReserve > ShortListCap ? body - SubPageReserve : ShortListCap;
    }
}
