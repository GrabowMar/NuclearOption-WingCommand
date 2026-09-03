namespace WingCommand
{
    /// <summary>
    /// Precedence tiers, strictly ordered. A reflex in a lower-numbered band beats every
    /// reflex in a higher-numbered one <b>regardless of score</b>.
    ///
    /// This is the guarantee that scoring alone cannot give. A pure utility system decides
    /// everything by comparing numbers, so one mistuned curve — in this mod or in somebody
    /// else's — can outrank a missile break. Bands make that structurally impossible:
    /// scores are only ever compared against other scores in the same band.
    /// </summary>
    public enum WingReflexBand
    {
        /// <summary>Staying alive. Missile break, terrain abort. Nothing outranks this.</summary>
        Survival = 0,

        /// <summary>Conditions where holding the task would fly the aircraft into something.</summary>
        Safety = 1,

        /// <summary>Keeping the wing a wing. Leash recall.</summary>
        Cohesion = 2,

        /// <summary>The standing order. Always available, so resolution is total.</summary>
        Task = 3,
    }

    /// <summary>
    /// One reason a wingman might do something other than its standing order.
    ///
    /// <b>This is the modding surface.</b> The mod's own six reflexes are registered
    /// through the same public call a third-party plugin uses — if the core did not eat its
    /// own cooking here, the public path would rot the first time an internal shortcut was
    /// more convenient.
    ///
    /// One instance serves every wingman, so an implementation must be <b>stateless</b>:
    /// everything it is allowed to know arrives in the <see cref="WingSituation"/>. It
    /// returns a number and nothing else — it cannot switch a pilot state, edit the
    /// standing directive, or touch the aircraft.
    /// </summary>
    public interface IWingReflex
    {
        /// <summary>
        /// Stable, unique, namespaced — <c>"wingcommand.missile-break"</c>. Used as the tie
        /// break when two reflexes in a band score identically, so registration order can
        /// never change the outcome.
        /// </summary>
        string Id { get; }

        /// <summary>Which precedence tier this competes in.</summary>
        WingReflexBand Band { get; }

        /// <summary>
        /// The behaviour to fly when this reflex wins, from <see cref="WingBehaviours"/> or
        /// registered by the plugin that owns it. A string rather than an enum so a third
        /// party can add a behaviour without the core enumerating it.
        /// </summary>
        string BehaviourId { get; }

        /// <summary>
        /// Seconds this reflex keeps control once it has it, even as its own score falls.
        /// A lower band still preempts it immediately — a minimum hold is not immunity.
        /// Zero for a reflex that should release the moment it stops scoring.
        /// </summary>
        float MinimumSeconds { get; }

        /// <summary>
        /// True for a reflex the Performance profile drops entirely.
        ///
        /// Dropping a reflex changes what the wingman does, and that is fine — Performance
        /// is a deliberately worse wingman, not a cheaper route to the same one. Use it for
        /// behaviour that is a luxury on a multiplayer host, and not for anything the
        /// aircraft needs in order to survive.
        ///
        /// Declared here rather than switched at the call site, so the mode stays one
        /// question asked in one place instead of a toggle per behaviour.
        /// </summary>
        bool RequiresSmartMode { get; }

        /// <summary>
        /// How much this reflex wants control right now: 0 to stand down, 1 for maximally
        /// urgent. Only ever compared against other scores in the same band.
        ///
        /// <paramref name="incumbent"/> is true when this reflex is the one currently in
        /// control, and it exists so hysteresis can be <i>declared</i> rather than tracked.
        /// A reflex that grabs control at one threshold and releases at a looser one — which
        /// is every reflex that should not flap on a boundary — reads its two thresholds off
        /// this flag and stays stateless. One instance serves the whole wing, so there is
        /// nowhere to keep a "was I running last tick" field even if it wanted one.
        ///
        /// Must not throw. One that does is caught, reported once and disabled for the
        /// mission rather than being allowed to take the wing AI down with it.
        /// </summary>
        float Score(in WingSituation situation, bool incumbent);
    }

    /// <summary>The behaviours this mod ships. A third party may register more.</summary>
    public static class WingBehaviours
    {
        /// <summary>Hands off entirely — the stock taxi/launch AI owns the airframe.</summary>
        public const string Held = "wingcommand.held";

        /// <summary>The missile break.</summary>
        public const string MissileBreak = "wingcommand.missile-break";

        /// <summary>Orbit overhead while the leader is on the runway.</summary>
        public const string DeckHold = "wingcommand.deck-hold";

        /// <summary>
        /// Wings-level climb before chasing a distant slot. Shared by the climb-out
        /// Safety reflex and the terrain-abort Survival reflex.
        /// </summary>
        public const string ClimbOut = "wingcommand.climb-out";

        /// <summary>Fly the slot to close a leash overshoot.</summary>
        public const string Rejoin = "wingcommand.rejoin";

        /// <summary>Whatever the standing directive says. The resting behaviour.</summary>
        public const string Task = "wingcommand.task";

        /// <summary>
        /// The behaviour a member with no autopilot flies, whatever it was told to do.
        ///
        /// Every built-in behaviour steers through <c>Autopilot.AutoAim</c>, which a ship or
        /// a ground vehicle does not have, so a surface member is routed here instead of
        /// through the switch above - and this is the one id Wing Command declares but does
        /// not implement. A companion plugin registers the state through
        /// <see cref="WingBehaviourCatalog"/>; with nothing registered a surface member is
        /// simply never given a state, which is inert rather than broken.
        ///
        /// Where it should go is Wing Command's answer, published through <c>WingSurface</c>.
        /// How to make a hull go there is the registrant's, because the gains that drive a
        /// light truck are not the gains that drive a fleet carrier.
        /// </summary>
        public const string Surface = "wingcommand.surface";
    }
}
