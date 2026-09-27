using System;

namespace WingCommand
{
    /// <summary>The SQUADRON notch's geometry (research squadron-studio §2.5): three columns — PILOTS list, STUDIO, RECORD — with 12 px
    /// padding and gaps, list rows by the body's height, a native portrait on tall bodies, the studio's APPEARANCE and IDENTITY
    /// sub-columns, and a bio box above the footer. Everything fits without scrolling from 1278×650 up.</summary>
    internal static class SquadronLayout
    {
        public const float Pad = 12f, Gap = 12f, Head = 18f, RowsTop = Pad + Head + 8f, RowH = 40f, RowPitch = 44f, ListFooter = 98f;
        public const float FooterRow = 28f, FooterGap = 8f, Pager = 26f;
        public const int MinRows = 4, MaxRows = 16;
        public const float StudioHead = 20f, SubTop = 44f, SectionHead = 18f, SectionGap = 6f, StepPitch = 32f, StepH = 28f, RandomRow = 28f;
        public const float FieldPitch = 36f, FieldH = 28f, FieldLabel = 72f, RandomW = 84f, FooterBlock = 50f;

        public static float ListW(float w) => Clamp(0.2f * w, 280f, 340f);

        public static float RecordW(float w) => Clamp(0.24f * w, 320f, 440f);

        public static float StudioW(float w) => w - ListW(w) - RecordW(w) - 2f * Pad - 2f * Gap;

        public const float ListX = Pad;

        public static float StudioX(float w) => ListX + ListW(w) + Gap;

        public static float RecordX(float w) => StudioX(w) + StudioW(w) + Gap;

        /// <summary>Rows the list shows: as many 44 px rows as fit above its 98 px footer, 4 to 16.</summary>
        public static int ListRows(float h)
        {
            int n = (int)Math.Floor((h - 146f) / RowPitch);
            return n < MinRows ? MinRows : n > MaxRows ? MaxRows : n;
        }

        public static float PortraitW(float h) => h >= 820f ? 128f : 96f;

        public static float PortraitH(float h) => PortraitW(h) * 1.5f;

        public static float StepperW(float studioW) => studioW >= 800f ? 260f : 200f;

        public static float LookW(float studioW, float h) => PortraitW(h) + 8f + StepperW(studioW);

        public static float IdentityW(float studioW, float h) => studioW - LookW(studioW, h) - Gap;

        /// <summary>The taller of the two sub-columns' contents under their heads.</summary>
        public static float SubH(float h) => SectionHead + SectionGap + Math.Max(Math.Max(PortraitH(h), 5f * StepPitch + RandomRow), 3f * FieldPitch);

        public static float BioTop(float h) => SubTop + SubH(h) + Gap;

        public static float BioH(float h) => Clamp(h - Pad - FooterBlock - Gap - (BioTop(h) + SectionHead + SectionGap), 96f, 180f);

        public static float StudioBottom(float h) => BioTop(h) + SectionHead + SectionGap + BioH(h) + Gap + FooterBlock;

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
