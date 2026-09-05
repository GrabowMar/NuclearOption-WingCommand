namespace WingCommand
{
    /// <summary>
    /// The formation the wing is currently flying, and the fixed geometry it flies by.
    ///
    /// <see cref="Shape"/> used to be a config entry. It is a live choice a player makes from
    /// the WMC FORM stepper mid-mission, not a value anyone can pick blind before one, so it
    /// is runtime state here rather than a setting. <see cref="SlotSpacing"/> and
    /// <see cref="MaxWingSize"/> were settings for the same reason the numbers in
    /// <see cref="WingTuning"/> were - no player has a way to know what to set them to - and
    /// are constants here beside the state they pair with.
    /// </summary>
    internal static class WingFormation
    {
        /// <summary>Geometry wingmen hold station in. Set from the WMC FORM stepper.</summary>
        public static FormationShape Shape { get; set; } = FormationShape.EchelonRight;

        /// <summary>
        /// Lateral and longitudinal spacing between slots, in metres. Helicopters fly a
        /// fraction of this (<see cref="WingTuning.RotarySpacingScale"/>) and the wing
        /// widens it by itself under threat.
        /// </summary>
        public static float SlotSpacing { get; set; } = 120f;

        /// <summary>
        /// Most wingmen the roster holds. Formation geometry, HUD layout and the shop
        /// economy are all sized to this; the F1 debug bypass is the only way past it.
        /// </summary>
        public const int MaxWingSize = 3;
    }
}
