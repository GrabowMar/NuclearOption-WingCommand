namespace WingCommand
{
    /// <summary>Strict precedence tiers: lower-numbered bands always beat higher bands. Compare scores
    /// only within a band so utility tuning cannot override survival with a task preference.</summary>
    public enum WingReflexBand
    {
        /// <summary>Highest-priority survival, including missile and terrain escape.</summary>
        Survival = 0,

        /// <summary>Safety conditions that make continuing the task hazardous.</summary>
        Safety = 1,

        /// <summary>Wing cohesion, including leash recall.</summary>
        Cohesion = 2,

        /// <summary>Always-available standing task, ensuring a resolution.</summary>
        Task = 3,
    }

    /// <summary>Public stateless reflex contract shared by built-ins and extensions. One instance serves
    /// all members; read WingSituation and return a score without changing aircraft, pilot state, or
    /// standing intent.</summary>
    public interface IWingReflex
    {
        /// <summary>Stable unique namespaced ID, used for deterministic ties and cached at registration.
        /// Unregister and register again to change identity.</summary>
        string Id { get; }

        /// <summary>Precedence band for this reflex.</summary>
        WingReflexBand Band { get; }

        /// <summary>Built-in or registered behaviour ID to fly on winning. Metadata is sampled once per
        /// decision; changes take effect on the next decision.</summary>
        string BehaviourId { get; }

        /// <summary>Minimum control duration in seconds despite falling score. Lower bands still preempt
        /// immediately; zero releases as soon as scoring stops.</summary>
        float MinimumSeconds { get; }

        /// <summary>Whether Performance mode omits this optional reflex. Never require Smart mode for
        /// survival-critical behaviour.</summary>
        bool RequiresSmartMode { get; }

        /// <summary>Relative priority when breaking score ties within the same band. Higher values win;
        /// identical priorities fall back to deterministic ID string comparison.</summary>
        int Priority => 0;

        /// <summary>Score from 0 (inactive) to 1 (maximum urgency), compared within this band. Use
        /// incumbent for stateless entry/release hysteresis. Do not throw; score, metadata, and lifecycle
        /// faults are reported once and disable the extension for the mission.</summary>
        float Score(in WingSituation situation, bool incumbent);
    }

    /// <summary>Optional lifecycle limits: invalidate holds when order/aircraft conditions end, and permit
    /// immediate emergencies to interrupt holds within the same band.</summary>
    public interface IWingReflexLifecycle
    {
        bool CanHold(in WingSituation situation);
        bool InterruptsMinimumHold { get; }
    }

    /// <summary>Built-in behaviour IDs; extensions may register additional IDs.</summary>
    public static class WingBehaviours
    {
        /// <summary>Release mod flight control, preserving native taxi/launch ownership.</summary>
        public const string Held = "wingcommand.held";

        /// <summary>Missile-evasion behaviour.</summary>
        public const string MissileBreak = "wingcommand.missile-break";

        /// <summary>Leader-tracking overhead hold during landing or deck operations.</summary>
        public const string DeckHold = "wingcommand.deck-hold";

        /// <summary>Airborne terrain recovery followed by standing-task resumption.</summary>
        public const string TerrainAbort = "wingcommand.terrain-abort";

        /// <summary>Rejoin the slot after exceeding the pursuit leash.</summary>
        public const string Rejoin = "wingcommand.rejoin";

        /// <summary>Execute standing intent as the default behaviour.</summary>
        public const string Task = "wingcommand.task";

        /// <summary>Required registered control for members without autopilots. WingSurface supplies
        /// destination and task; companion plugins implement vehicle-specific steering. Without
        /// registration, leave surface members without a mod flight state.</summary>
        public const string Surface = "wingcommand.surface";
    }
}
