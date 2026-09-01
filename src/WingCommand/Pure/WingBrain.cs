namespace WingCommand
{
    /// <summary>
    /// Smart is the full behaviour and CPU/network cost - the default and the development
    /// target. Performance is a lean profile for busy missions and, above all, for
    /// multiplayer: the host simulates every AI wingman, so Performance coarsens the
    /// formation update (fewer synced control-input changes per wingman), drops the
    /// Harmony target-deconfliction pass, stops the all-aircraft opportunity scans,
    /// removes the manoeuvre and jam orders (both spray per-tick networked side effects)
    /// and cuts radio chatter to the essential.
    /// </summary>
    internal enum WingMode
    {
        Smart,
        Performance,
    }

    /// <summary>
    /// The one switch for how much wingman there is. Resolved once per mission from
    /// <c>AI/Mode</c>; every consumer reads this snapshot, never the live config entry, so
    /// a mid-mission change is inert until the next mission.
    /// </summary>
    internal static class WingBrain
    {
        /// <summary>Reactive spacing multiplier while the widen behaviour is active.</summary>
        public const float ThreatWidenScale = 1.45f;

        /// <summary>Saturation penalty per extra committed attacker on one target.</summary>
        public const float TargetSaturationPenalty = 1.5f;

        private static bool performance;

        public static WingMode Mode { get; private set; } = WingMode.Smart;

        /// <summary>Snapshot the mode for the mission about to start.</summary>
        public static void Begin(WingMode mode)
        {
            Mode = mode;
            performance = mode == WingMode.Performance;
        }

        /// <summary>True in Smart mode: the full, expensive behaviour set is available.</summary>
        public static bool Full => !performance;

        /// <summary>Physics ticks a wingman may coast between full formation recomputes.</summary>
        public static int GeometryStride => performance ? 3 : 1;

        /// <summary>Multiplies every periodic-check and UI-refresh interval.</summary>
        public static float IntervalScale => performance ? 2.5f : 1f;

        /// <summary>
        /// A base interval in seconds, stretched for the current mode. For non-critical
        /// periodic work only - missile evasion, the takeover prompt and the radio
        /// anti-spam gaps are deliberately left on their own fixed timers.
        /// </summary>
        public static float Interval(float seconds) => seconds * IntervalScale;

        // Behaviour gates. All follow Full today; named individually so a call site reads
        // for itself and a future third mode can differ per behaviour.

        /// <summary>Terrain floor, turn-side mirror, combat-spread reaction, rejoin-lead, reactive widen.</summary>
        public static bool SmartFormation => Full;

        /// <summary>The <c>CombatAI.ChooseHQTarget</c> deconfliction postfix - the biggest host cost.</summary>
        public static bool Deconfliction => Full;

        /// <summary>Let wingmen search for and fire on opportunity targets from the slot.</summary>
        public static bool OpportunityFire => Full;

        /// <summary>Non-critical radio calls and idle crew banter.</summary>
        public static bool RichChatter => Full;

        /// <summary>The manoeuvres menu and the Manoeuvre order.</summary>
        public static bool Manoeuvres => Full;

        /// <summary>The Jam Target order.</summary>
        public static bool Jamming => Full;

        /// <summary>Metres of terrain clearance a formation slot keeps, 0 when disabled.</summary>
        public static float TerrainClearance => SmartFormation ? 45f : 0f;

        /// <summary>
        /// Master scales on formation position correction and its rate/damping terms. Held
        /// at 1 in both modes for now; kept here so a future per-mode tweak is one line and
        /// <see cref="FixedWingFormation"/> keeps reading a named source rather than a literal.
        /// </summary>
        public static float Aggression => 1f;
        public static float Damping => 1f;

        public static string Summary() =>
            $"mode={Mode} stride={GeometryStride} intervalScale={IntervalScale:0.0}";
    }
}
