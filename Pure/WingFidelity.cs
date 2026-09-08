namespace WingCommand
{
    /// <summary>Mission mode selecting full behaviour or reduced optional work and geometry
    /// cadence.</summary>
    internal enum WingMode
    {
        Smart,
        Performance,
    }

    /// <summary>Snapshots AI/Mode at mission start for shared feature gates and timing. Member brains own
    /// decisions; fidelity never edits intent or aircraft controls.</summary>
    internal static class WingFidelity
    {
        private static bool performance;

        public static WingMode Mode { get; private set; } = WingMode.Smart;

        /// <summary>Freeze the selected mode for the next mission.</summary>
        public static void Begin(WingMode mode)
        {
            Mode = mode;
            performance = mode == WingMode.Performance;
        }

        /// <summary>Whether the complete Smart behaviour set is enabled.</summary>
        public static bool Full => !performance;

        /// <summary>Physics-tick interval between full formation geometry updates.</summary>
        public static int GeometryStride => performance ? 3 : 1;

        /// <summary>Multiplier for mode-scaled periodic and UI intervals.</summary>
        public static float IntervalScale => performance ? 2.5f : 1f;

        /// <summary>Scale a base interval by mode. Missile evasion, takeover interaction, and radio
        /// anti-spam use independent fixed timers.</summary>
        public static float Interval(float seconds) => seconds * IntervalScale;

        // Named feature gates share the current full-mode setting.

        /// <summary>Terrain clearance, turn mirroring, threat spacing, and rejoin prediction
        /// features.</summary>
        public static bool SmartFormation => Full;

        /// <summary>Enable native target-search deconfliction, a significant host cost.</summary>
        public static bool Deconfliction => Full;

        /// <summary>Allow station-keeping opportunity target scans and fire.</summary>
        public static bool OpportunityFire => Full;

        /// <summary>Enable noncritical calls and ambient banter.</summary>
        public static bool RichChatter => Full;

        /// <summary>Expose manoeuvre commands and menu.</summary>
        public static bool Manoeuvres => Full;

        /// <summary>Expose targeted pod jamming.</summary>
        public static bool Jamming => Full;

        /// <summary>Formation terrain clearance in metres, or zero when disabled.</summary>
        public static float TerrainClearance => SmartFormation ? 45f : 0f;

        public static string Summary() =>
            $"mode={Mode} stride={GeometryStride} intervalScale={IntervalScale:0.0}";
    }
}
