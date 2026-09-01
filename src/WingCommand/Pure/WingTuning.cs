namespace WingCommand
{
    /// <summary>
    /// Every tuned number the wing flies and fights by.
    ///
    /// These were configuration entries. They were the wrong shape for one: a player has no
    /// way to know what a good value is, no way to tell a bad one from a bug, and no reason
    /// to want a different one - while every value here is load-bearing, several are derived
    /// from the game's own arithmetic, and a few are safety floors that only ever want
    /// raising in code. Fifty-three settings nobody could tune made the dozen that people do
    /// change impossible to find.
    ///
    /// So they live here instead, as constants, with the reasoning that fixed each one. The
    /// remaining <see cref="WingConfig"/> entries are the ones a player has an opinion about.
    /// Anything genuinely mode-dependent belongs in <see cref="WingBrain"/>, not here.
    /// </summary>
    internal static class WingTuning
    {
        // ------------------------------------------------------------------ formation

        /// <summary>Vertical stagger per slot, metres. Keeps wingmen out of each other's wash.</summary>
        public const float SlotStack = 20f;

        /// <summary>
        /// Largest heading correction, in degrees, a wingman commands while holding station -
        /// the real limit on how fast it can close a lateral error. The settings this replaced
        /// (a 1200 m look-ahead over a 220 m maximum correction) worked out to 10.4 degrees,
        /// which is why station-keeping used to feel sluggish.
        /// </summary>
        public const float CommandAngle = 25f;

        /// <summary>
        /// Bank authority, degrees, once settled in the slot. The game scales this down again
        /// by altitude and speed, so the old 45 left only 27-54 degrees of real authority and
        /// a wingman simply could not follow a hard turn.
        /// </summary>
        public const float StationBank = 75f;

        /// <summary>
        /// Bank authority, degrees, while rejoining from outside <see cref="CaptureDistance"/>.
        /// Held below inversion on purpose: the live formation log recorded requests as high
        /// as 277 degrees after leader-bank feed-forward, which is an inversion request rather
        /// than useful pursuit authority.
        /// </summary>
        public const float PursuitBank = 88f;

        /// <summary>Throttle change per m/s of speed error.</summary>
        public const float ThrottleGain = 0.12f;

        /// <summary>Metres from the slot at which a wingman switches from rejoining to holding.</summary>
        public const float CaptureDistance = 500f;

        /// <summary>Seconds of rejoin boost per slot index, so a wing does not converge as one mass.</summary>
        public const float RejoinStagger = 1.2f;

        /// <summary>
        /// How much of the leader's bank a settled wingman copies outright. Blending with the
        /// autopilot's own roll rather than overriding it is what keeps the turn stable; the
        /// controller disengages the term past a hard bank limit and near the ground.
        /// </summary>
        public const float BankMatchBlend = 0.35f;

        // ------------------------------------------------------------------ rotary
        //
        // Helicopters get their own model rather than a variation on the fixed-wing one,
        // because AutopilotHelo answers to completely different commands. Its forward
        // waypoint is recomputed once a second and rate-limited to 0.8 rad, so there is a
        // ceiling on rotary responsiveness that no number here can raise.

        /// <summary>Slot spacing multiplier for helicopters, which fly slower and much closer together.</summary>
        public const float RotarySpacingScale = 0.55f;

        /// <summary>Leader speed, m/s, below which helicopters hold their slot as a point in space.</summary>
        public const float RotaryHoverSpeed = 25f;

        /// <summary>
        /// Seconds of travel used as the helicopter's destination distance. A power setting,
        /// not a steering one: AutopilotHelo derives collective from
        /// <c>0.5 + distance*0.001 - speed*0.02</c>, so distance IS the throttle command and
        /// 20 is the value that makes those terms cancel at hover power.
        /// </summary>
        public const float RotaryPowerSeconds = 20f;

        // ------------------------------------------------------------------ manoeuvres

        /// <summary>
        /// Height above ground, metres, before a wingman will start a manoeuvre at all. Each
        /// manoeuvre carries its own higher minimum on top of this.
        /// </summary>
        public const float ManeuverEntryFloor = 250f;

        /// <summary>
        /// Radar altitude at which a manoeuvre in progress is abandoned wings-level. The
        /// last-ditch anti-crash guard, and the reason it is not a setting.
        /// </summary>
        public const float ManeuverHardFloor = 120f;

        /// <summary>
        /// Baseline airspeed, as a fraction of the airframe's maximum, below which a manoeuvre
        /// is refused. Individual manoeuvres raise this for themselves.
        /// </summary>
        public const float ManeuverMinSpeedFraction = 0.35f;

        // ------------------------------------------------------------------ engagement

        /// <summary>Seconds a missile warning must stay clear before a defensive wingman resumes its order.</summary>
        public const float PanicClearSeconds = 2.5f;

        /// <summary>
        /// Weapons range, metres, for a wingman shooting from its slot. Hold and Escort both
        /// use it; neither manoeuvres to engage, so for both it is purely a range limit.
        /// </summary>
        public const float HoldEngageRange = 6000f;

        /// <summary>Weapons range, metres, for a Free wingman.</summary>
        public const float FreeEngageRange = 12000f;

        /// <summary>
        /// How far a wingman may stray from the leader before it abandons the fight and
        /// rejoins. This is what stops the wing dispersing.
        /// </summary>
        public const float LeashRadius = 8000f;

        /// <summary>Metres from the threat a Fall Back runs before the wing settles into its holding orbit.</summary>
        public const float FallBackStandoff = 6000f;

        /// <summary>Radius of the circle flown when holding over a point, for Orbit Here and after a Fall Back.</summary>
        public const float OrbitRadius = 2000f;

        /// <summary>
        /// Seconds a Defend wingman stays weapons-free against ground targets after the player
        /// fires an anti-surface weapon.
        /// </summary>
        public const float MirrorWindowSeconds = 15f;

        /// <summary>
        /// Minimum seconds between shots from one wingman. Without a gap they fire on every
        /// engagement tick and empty the aircraft in seconds.
        /// </summary>
        public const float FireInterval = 5f;

        /// <summary>
        /// Ceiling on simultaneous wingmen assigned to one target. Weapon effectiveness may
        /// choose fewer; missiles always receive one interceptor.
        /// </summary>
        public const int MaxWingmenPerTarget = 2;

        /// <summary>Fuel fraction at which a wingman calls bingo and heads home.</summary>
        public const float BingoFuel = 0.15f;

        // ------------------------------------------------------------------ economy

        /// <summary>
        /// Fraction of an airframe's list price charged the first time an already-active
        /// mission aircraft is assigned to the wing.
        /// </summary>
        public const float RecruitmentCostRate = 0.25f;

        /// <summary>
        /// Price multiplier for an airframe requisitioned past the mission's AI aircraft limit.
        /// Missions often leave a limit of zero once the player's own presence is subtracted,
        /// so this is what keeps the shop usable there without making the cap meaningless.
        /// </summary>
        public const float ExceedLimitCostMultiplier = 3f;

        /// <summary>Player rank required before the squadron limit may be exceeded at all.</summary>
        public const int ExceedLimitRank = 3;

        /// <summary>
        /// How many over-limit airframes may be flying at once. The allowance frees up as they
        /// are lost or recovered, so it caps how far past the mission's cap the shop can take
        /// you rather than how often you may buy.
        /// </summary>
        public const int ExceedLimitAllowance = 3;

        // ------------------------------------------------------------------ pilots
        //
        // Ordinary numbers rather than a formula: the whole curve is one triangular step, so
        // moving XpPerRank retunes progression without any two values disagreeing.

        /// <summary>Experience for a contact a wingman was shooting at being destroyed.</summary>
        public const int XpPerKill = 25;

        /// <summary>Experience for bringing an airframe home or completing a cargo delivery.</summary>
        public const int XpPerSortie = 40;

        /// <summary>Experience for surviving a missile engagement.</summary>
        public const int XpPerEngagement = 10;

        /// <summary>
        /// Experience step between ranks. Thresholds grow triangularly from it: Wingman at one
        /// step, Veteran at three, Ace at six, Legend at ten.
        /// </summary>
        public const int XpPerRank = 120;

        /// <summary>
        /// How much rank changes a wingman's shooting: a Legend gets roughly 12% more weapon
        /// reach and off-boresight tolerance and cycles shots about 12% faster than a rookie.
        /// </summary>
        public const float RankEffect = 1f;
    }
}
