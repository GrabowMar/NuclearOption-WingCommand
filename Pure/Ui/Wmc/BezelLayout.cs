using System;

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

        // LOADOUT (spec WMC rebuild §LOADOUT; research loadout-ui §2): a scroll viewport over the build card, airframe tiles, the
        // template bar and the HARDPOINTS table, and a LIVERY row pinned on the body's floor.
        public const float LiveryRow = 30f, LiveryPin = 6f + LiveryRow, CardH = 84f, BlockGap = 10f, SectionHead = 22f, LoadoutTileH = 44f;
        public const float LoadoutTiles = 2f * LoadoutTileH + TileGap, TemplateBar = 26f, TemplatePick = 242f, TemplateBtn = 64f, DeleteGap = 16f;
        public const float HardpointsTop = CardH + BlockGap + SectionHead + HeadGap + LoadoutTiles + BlockGap + TemplateBar + BlockGap;
        public const float ColumnHead = 16f, HardpointHead = SectionHead + HeadGap + ColumnHead, HpRowH = 42f, HpPitch = 44f, HpPager = 26f;
        public const int HpRowsMin = 4, HpRowsMax = 6;
        public const float ColStation = 8f, ColStationW = 164f, ColStore = 176f, ColStoreW = 172f, ColMass = 352f, ColMassW = 48f;
        public const float ColVerb = 404f, ColVerbW = 54f;
        public const float PopupRowPitch = 32f, PopupPad = 8f;
        public const int PopupMaxRows = 7;

        public static float Body(float panelHeight) => panelHeight - Chrome;

        /// <summary>LOADOUT's scroll viewport: the body less the pinned LIVERY row.</summary>
        public static float LoadoutView(float body) => body - LiveryPin;

        /// <summary>Hardpoint rows per page: as many as the tall dock shows without scrolling (6), at least 4.</summary>
        public static int HardpointRows(float body)
        {
            int n = (int)Math.Floor((LoadoutView(body) - HardpointsTop - HardpointHead - (HpPager + HeadGap)) / HpPitch);
            return n < HpRowsMin ? HpRowsMin : n > HpRowsMax ? HpRowsMax : n;
        }

        public static float LoadoutContent(int rows, bool paged) => HardpointsTop + HardpointHead + rows * HpPitch + (paged ? HpPager + HeadGap : 0f);

        /// <summary>The toolkit popup's height for <paramref name="entries"/> rows (seven at most: it pages beyond).</summary>
        public static float PopupHeight(int entries) =>
            PopupRowPitch * (entries <= 0 ? 1 : entries > PopupMaxRows ? PopupMaxRows : entries) + PopupPad;

        /// <summary>Where a popup for a row goes, as a depth from the body's top: below the row when it fits, else above, else after
        /// scrolling the row up (<paramref name="scroll"/>, at most <paramref name="maxScroll"/>) so it fits below — never over its row
        /// and never past the body (the page layer sits under the chrome). A last resort, when nothing fits, keeps it in the body.</summary>
        public static float PopupPlace(float rowDepth, float rowH, float popupH, float bodyH, float maxScroll, out float scroll)
        {
            scroll = 0f;
            if (rowDepth + rowH + popupH <= bodyH) return rowDepth + rowH;
            if (rowDepth - popupH >= 0f) return rowDepth - popupH;
            float need = rowDepth + rowH + popupH - bodyH;
            if (need <= maxScroll && rowH + popupH <= bodyH)
            {
                scroll = need;
                return rowDepth - need + rowH;
            }
            float room = bodyH - (rowDepth + rowH) >= rowDepth ? rowDepth + rowH : rowDepth - popupH;
            return Math.Max(0f, Math.Min(room, bodyH - popupH));
        }

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
