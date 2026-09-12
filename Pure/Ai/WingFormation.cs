namespace WingCommand
{
    /// <summary>Current formation choice and shared geometry settings used by the WMC controls and flight
    /// solver.</summary>
    internal static class WingFormation
    {
        /// <summary>Active formation shape selected through the WMC stepper.</summary>
        public static FormationShape Shape { get; set; } = FormationShape.EchelonRight;

        /// <summary>Base planar slot spacing in metres, reduced for rotary aircraft and widened under
        /// threat.</summary>
        public static float SlotSpacing { get; set; } = 120f;

        /// <summary>Normal roster limit used by geometry, HUD, and economy; debug bypass may exceed
        /// it.</summary>
        public const int MaxWingSize = 4;
    }
}
