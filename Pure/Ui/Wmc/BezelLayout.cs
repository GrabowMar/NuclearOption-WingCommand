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

        // SUPPLY (spec WMC rebuild §SUPPLY; supply-ui §3 less its OVER-LIMIT row — the mode is a setting, user 2026-09-25):
        // a scroll viewport over the steps and a DISPATCH pin on the body's floor.
        public const float Content = 458f;
        public const float PinGap = 6f, PinCard = 48f, PinGap2 = 4f, RequisitionH = 30f, SupplyPin = PinGap + PinCard + PinGap2 + RequisitionH;
        public const float StepHead = 18f, HeadGap = 4f, StepGap = 9f, PilotCard = 48f;
        public const float TileH = 56f, TileGap = 6f, TileFooter = 26f;
        public const int TileCols = 3, TileRows = 2;
        public const float FitRow = 26f, FitDetail = 28f, BaseMode = 24f, BaseRowH = 28f, BasePitch = 30f;
        public const int BaseRows = 3;
        public const float InboundHead = 18f, InboundRow = 22f, AdoptBand = 26f;
        public const int InboundMax = 4;
        public const float PilotStep = StepHead + HeadGap + PilotCard + StepGap;
        public const float AirframeStep = StepHead + HeadGap + TileRows * TileH + (TileRows - 1) * TileGap + HeadGap + TileFooter + StepGap;
        public const float FitStep = StepHead + HeadGap + FitRow + HeadGap + FitDetail + StepGap;
        public const float BaseStep = StepHead + HeadGap + BaseMode + HeadGap + BaseRows * BasePitch + HeadGap;
        public const float SupplySteps = PilotStep + AirframeStep + FitStep + BaseStep;

        public static float Body(float panelHeight) => panelHeight - Chrome;

        /// <summary>SUPPLY's scroll viewport: the body less the DISPATCH pin.</summary>
        public static float SupplyView(float body) => body - SupplyPin;

        /// <summary>INBOUND rows drawn (the last says "+n MORE" beyond <see cref="InboundMax"/>).</summary>
        public static int InboundRows(int inbound) => inbound <= 0 ? 0 : inbound > InboundMax ? InboundMax : inbound;

        public static float InboundBlock(int inbound)
        {
            int rows = InboundRows(inbound);
            return rows == 0 ? 0f : InboundHead + HeadGap + rows * InboundRow + StepGap;
        }

        public static float AdoptBlock(bool adopt) => adopt ? AdoptBand + StepGap : 0f;

        /// <summary>SUPPLY's scroll content: INBOUND and ADOPT when they show, then the four steps.</summary>
        public static float SupplyContent(int inbound, bool adopt) => InboundBlock(inbound) + AdoptBlock(adopt) + SupplySteps;

        public static float TileWidth(float content) => (content - (TileCols - 1) * TileGap) / TileCols;

        /// <summary>Pixels the flight list may take.</summary>
        public static float ListCap(float body) => body - SubPageReserve > ShortListCap ? body - SubPageReserve : ShortListCap;
    }
}
