namespace WingCommand
{
    /// <summary>Mission fidelity: full behavior, or reduced geometry and optional work.</summary>
    internal enum WingMode
    {
        Smart,
        Performance,
    }

    /// <summary>
    /// Mission-wide feature gates and update cadence, frozen from <c>AI/Mode</c> at
    /// mission start. Per-aircraft decision state belongs to WingMemberBrain; this
    /// class never selects a behavior, changes an order or writes flight controls.
    /// </summary>
    internal static class WingFidelity
    {
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
        /// A base interval in seconds, stretched for the current mode. Missile evasion, the
        /// takeover prompt and the radio anti-spam gaps are deliberately left on their own
        /// fixed timers.
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

        public static string Summary() =>
            $"mode={Mode} stride={GeometryStride} intervalScale={IntervalScale:0.0}";
    }
}
