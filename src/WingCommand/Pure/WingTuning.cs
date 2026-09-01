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
        /// which is why station-keeping used to feel sluggish. Nudged from 25 to 27 to give a
        /// wingman the authority to hold the tighter slot zone.
        /// </summary>
        public const float CommandAngle = 27f;

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

        // --------------------------------------------------------- leader prediction
        //
        // Two feed-forwards, and they answer different questions. The lever says what the
        // leader has just been *asked* to do and says it immediately; the measured rate
        // says what it is *actually* doing and says it a second later but is never wrong.
        // Neither alone is enough: matching only the lever ignores a leader accelerating
        // down a dive, and matching only the rate is what put the wingman a fixed distance
        // behind for the whole of every acceleration.

        /// <summary>
        /// Seconds of the leader's measured acceleration fed into the speed demand: roughly
        /// the wingman's own thrust-response lag, so it arrives at the leader's new speed
        /// with the leader rather than starting to chase it then.
        ///
        /// Longer is not better, and the reason is that this term cannot tell a sustained
        /// acceleration from a jittery one. Simulating the closed loop against a firewall,
        /// a chop, repeated throttle jabs, a slow ramp and a bang-bang AI leader - each over
        /// two different drag models - a lead of 1.5 s is the best value that exists for the
        /// sustained cases and one of the worst for the jittery ones, because there it is
        /// projecting noise. Three quarters of a second is the value that is good at the
        /// first without being bad at the second, and the optimum is broad rather than sharp.
        /// </summary>
        public const float SpeedLeadSeconds = 0.75f;

        /// <summary>
        /// Largest leader acceleration treated as real, m/s². Roughly 2.5 g, well above what
        /// any airframe here sustains in level flight, so a genuine acceleration is never
        /// clipped. It exists only to stop a respawn, a collision or a dropped frame - the
        /// rate is differentiated from a velocity - being projected forward as a speed demand.
        /// </summary>
        public const float MaxCredibleAccel = 25f;

        /// <summary>
        /// How much of the leader's uncommanded lever travel the wingman copies.
        ///
        /// A fifth, not all of it, and the reason is worth stating because the obvious
        /// argument says otherwise: <see cref="ThrustModel.ThrottleAnticipation"/> is zero
        /// whenever the leader is settled, so copying it outright looks free. It is not,
        /// because it is not independent of <see cref="SpeedLeadSeconds"/> - the lever and
        /// the acceleration are the same speed change seen through two channels, one early
        /// and one accurate, and adding both in full counts it twice. Simulated, full gain
        /// alongside the acceleration lead is worse than either term alone. This is the
        /// share that buys the head start the lever offers without re-commanding what the
        /// acceleration term has already asked for.
        /// </summary>
        public const float AnticipationGain = 0.2f;

        /// <summary>
        /// Seconds of smoothing on the leader's throttle before it is read as intent. Long
        /// enough to ignore an AI leader's bang-bang throttle, short enough that a player
        /// moving the lever is still answered far sooner than the acceleration path could.
        /// </summary>
        public const float LeaderThrottleSmoothing = 0.15f;

        /// <summary>Seconds of smoothing on the differentiated leader speed.</summary>
        public const float SpeedRateSmoothing = 0.3f;

        /// <summary>
        /// Metres from the slot at which a wingman switches from rejoining to holding.
        /// Pulled in from 500 so the precise station-keeping law owns a larger share of the
        /// approach and the wing settles onto the slot sooner.
        /// </summary>
        public const float CaptureDistance = 375f;

        /// <summary>Seconds of rejoin boost per slot index, so a wing does not converge as one mass.</summary>
        public const float RejoinStagger = 1.2f;

        /// <summary>
        /// How much of the leader's bank a settled wingman copies outright. Blending with the
        /// autopilot's own roll rather than overriding it is what keeps the turn stable; the
        /// controller disengages the term past a hard bank limit and near the ground.
        /// </summary>
        public const float BankMatchBlend = 0.35f;

        /// <summary>
        /// Slot-spacing multipliers by rules of engagement. Defend/Hold (0.7) pulls the wing
        /// into a tight parade slot the leader keeps in sight and is hard to lose; Free (1.5)
        /// opens it toward combat-spread width for room to react and turn; Escort holds the
        /// unscaled baseline. <see cref="FormationFlyState"/> takes the larger of this and
        /// the reactive threat widen rather than multiplying the two, so selecting Free does
        /// not also compound a missile-warning widen on top of it.
        /// </summary>
        public const float RoeSpacingHold = 0.7f;
        public const float RoeSpacingEscort = 1f;
        public const float RoeSpacingFree = 1.5f;

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
        /// Seconds of the leader's measured acceleration fed into a helicopter's commanded
        /// velocity. Longer than the fixed-wing <see cref="SpeedLeadSeconds"/> because the
        /// lag being covered is longer: a helicopter accelerates by tilting its whole rotor
        /// disc, and <c>AutopilotHelo</c> rebuilds its steering waypoint only once a second
        /// on top of that.
        ///
        /// It is only a third longer rather than the several-fold the lag alone would argue
        /// for, because unlike the fixed-wing figure this one has not been simulated - the
        /// helicopter's plant is the game's own collective law, which the loop here does not
        /// model - so it sits at the conservative end of the range the fixed-wing sweep
        /// found safe.
        ///
        /// The throttle anticipation has no rotary counterpart: a helicopter's lever
        /// commands lift, not speed, so its position says nothing about where the leader's
        /// speed is going and there is nothing honest to read from it.
        /// </summary>
        public const float RotarySpeedLeadSeconds = 1f;

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

        // ------------------------------------------------------------------ jamming

        /// <summary>
        /// jamAmount sent to the designated target's own <c>Unit.Jam</c> on every pulse.
        ///
        /// The native call is <c>jamAmount / radar.jamTolerance</c> against a tolerance the mod
        /// can never read, and the result decays continuously in <c>Radar.Update</c>. The stock
        /// JammingPod weapon re-sends every 0.2 s at a range- and power-scaled amount; this
        /// pulses faster (alongside the self-protection ECM, on the same cadence) and generous
        /// enough to clear any tolerance outright, so the target stays saturated rather than
        /// merely nudged.
        /// </summary>
        public const float JamTargetAmount = 2f;

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
