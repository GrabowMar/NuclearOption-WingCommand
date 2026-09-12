namespace WingCommand
{
    /// <summary>Internal flight, combat, and economy tuning with units and constraints. Player preferences
    /// belong in WingConfig; mode-dependent gating belongs in WingFidelity.</summary>
    internal static class WingTuning
    {
        // Limit slot-transition speed relative to the leader so cross-unders cannot jump at implausible
        // velocity.
        public const float ShapeTransitionSeconds = 3f;
        public const float ShapeTransitionSpeed = 25f;
        // Separate recovery speed thresholds to avoid switching controllers near minimum flying speed.
        public const float FormationSpeedHysteresis = 5f;
        public const float FormationSlowEntrySeconds = 3f;
        public const float FormationSlowExitSeconds = 5f;
        public const float FormationOvershootEntry = 100f;
        public const float FormationRecoveryBlendSeconds = 2f;
        public const float FormationRecoveryBank = 25f;
        public const float FormationRecoveryHeading = 30f;
        public const float FormationInitialBraking = 2f;
        // Selected by tests/FlightTuning/Run.ps1; see its README.txt for model limits.
        public const float FormationBankRiseRate = 75f;
        public const float FormationLookAheadSeconds = 3f;
        public const float FormationMinLookAhead = 450f;
        public const float FormationRejoinCommandAngle = 55f;
        public const float FormationRejoinPitchUp = 25f;
        public const float FormationRejoinPitchDown = 22f;
        public const float FormationStationPitchUp = 7f;
        public const float FormationStationPitchDown = 6f;
        public const float FormationBurstSeconds = 8f;
        public const float FormationBurstCooldown = 30f;
        public const float FormationBurstInterval = 0.2f;

        /// <summary>Vertical stack unit in metres for wake separation.</summary>
        public const float SlotStack = 20f;

        /// <summary>Maximum station-keeping heading correction, in degrees.</summary>
        public const float CommandAngle = 40f;

        /// <summary>Settled bank limit with a level leader, in degrees; bounds lift loss and native
        /// elevator suppression. Leader turns may raise it through BankFollowScale.</summary>
        public const float StationBank = 45f;

        /// <summary>Level-leader rejoin bank limit beyond capture, in degrees; retains substantial
        /// vertical lift during pursuit.</summary>
        public const float PursuitBank = 70f;

        /// <summary>Altitude below which terrain clearance takes priority over bank matching.</summary>
        public const float BankMatchFloor = 280f;

        /// <summary>Bank limit during staggered track holding, in degrees. Allows alignment without
        /// pairing aggressive pursuit with speed-match throttle.</summary>
        public const float RejoinHoldBank = 45f;

        /// <summary>Throttle correction per m/s of speed error.</summary>
        public const float ThrottleGain = 0.12f;

        // Leader prediction combines immediate lever intent with measured acceleration, which also
        // captures dives and turns.

        /// <summary>Seconds of acceleration prediction, balancing thrust-response lag against amplified
        /// throttle jitter. Selected from fixed-wing closed-loop simulation sweeps.</summary>
        public const float SpeedLeadSeconds = 0.75f;

        /// <summary>Credible acceleration bound in m/s², rejecting derivative spikes from respawns,
        /// collisions, and missed samples.</summary>
        public const float MaxCredibleAccel = 25f;

        /// <summary>Fraction of the leader's unsettled throttle demand copied as anticipation.</summary>
        public const float AnticipationGain = 0.25f;

        /// <summary>Throttle smoothing duration in seconds, rejecting AI chatter while retaining prompt
        /// player intent.</summary>
        public const float LeaderThrottleSmoothing = 0.15f;

        /// <summary>Acceleration-sample smoothing duration in seconds.</summary>
        public const float SpeedRateSmoothing = 0.3f;

        /// <summary>Slot distance in metres defining the capture transition.</summary>
        public const float CaptureDistance = 300f;

        /// <summary>Rejoin stagger duration per slot index, in seconds.</summary>
        public const float RejoinStagger = 0.3f;

        /// <summary>ROE spacing scales: Hold tightens, Free widens, Tight stays at baseline. Combine with
        /// reactive widening using maximum, not multiplication.</summary>
        public const float RoeSpacingHold = 0.7f;
        public const float RoeSpacingTight = 1f;
        public const float RoeSpacingFree = 1.5f;

        // Rotary tuning for the distinct native autopilot and its rate-limited waypoint response.

        /// <summary>Rotary slot-spacing multiplier for slower, closer formations.</summary>
        public const float RotarySpacingScale = 0.55f;

        /// <summary>Wider surface spacing for slow steering response and no vertical escape; reduces
        /// collision risk in columns.</summary>
        public const float SurfaceSpacingScale = 2.8f;

        /// <summary>Leader horizontal-speed threshold for rotary hover selection, in m/s.</summary>
        public const float RotaryHoverSpeed = 25f;

        /// <summary>Rotary acceleration lead in seconds, covering rotor tilt and waypoint lag.
        /// Conservatively chosen without a full rotary simulation; no throttle anticipation because
        /// collective commands lift rather than speed.</summary>
        public const float RotarySpeedLeadSeconds = 1f;

        /// <summary>Travel seconds converted to rotary aim distance. Native collective uses 0.5 +
        /// distance*0.001 - speed*0.02, so 20 seconds balances speed drag at hover power.</summary>
        public const float RotaryPowerSeconds = 20f;

        // Manoeuvre tuning.

        /// <summary>Baseline manoeuvre entry altitude in metres AGL; individual manoeuvres may require
        /// more.</summary>
        public const float ManeuverEntryFloor = 250f;

        /// <summary>Radar-altitude emergency floor for wings-level manoeuvre recovery.</summary>
        public const float ManeuverHardFloor = 120f;

        /// <summary>Baseline minimum entry speed as a fraction of airframe maximum; manoeuvre gates may
        /// raise it.</summary>
        public const float ManeuverMinSpeedFraction = 0.35f;

        // Combat tuning.

        /// <summary>Reactive threat-spacing multiplier; WingFidelity gates its use.</summary>
        public const float ThreatWidenScale = 1.45f;

        /// <summary>Target saturation penalty per additional committed attacker.</summary>
        public const float TargetSaturationPenalty = 1.5f;

        /// <summary>Warning-free confirmation: native SARH retains tracking for two seconds after
        /// lock loss. One small physics margin avoids turning back during reacquisition.</summary>
        public const float PanicClearSeconds = 2.1f;

        /// <summary>Minimum missile-break duration in seconds to avoid mid-turn release on flickering
        /// warnings, subject to lifecycle and higher-priority safety.</summary>
        public const float PanicMinimumSeconds = 2f;

        /// <summary>Radar-altitude floor below which hard missile breaks are refused.</summary>
        public const float PanicFloorAlt = 5f;

        /// <summary>Map-point equality tolerance in metres, preventing nearby repeated clicks from
        /// restarting a task.</summary>
        public const float SamePointMetres = 5f;

        /// <summary>Predicted impact-time window for chaff in seconds. Native ejectors pace the held
        /// trigger.</summary>
        public const float ChaffWindowSeconds = 12f;

        /// <summary>Defensive bank limit below the formation inversion ceiling; bounds native roll noise
        /// when beam direction aligns with velocity.</summary>
        public const float DefensiveBankAllowed = 70f;

        /// <summary>Fraction of leash radius required for release after recall, providing wide
        /// hysteresis.</summary>
        public const float LeashReleaseFraction = 0.60f;

        /// <summary>Minimum recall hold in seconds; higher-priority survival still preempts
        /// immediately.</summary>
        public const float LeashHoldSeconds = 3f;

        /// <summary>Combat inactivity duration before temporary regrouping; retained combat intent can
        /// resume when activity returns.</summary>
        public const float EngageIdleSeconds = 8f;

        /// <summary>Hold/Tight station-keeping weapons range in metres.</summary>
        public const float HoldEngageRange = 6000f;

        /// <summary>Free-ROE weapons range in metres.</summary>
        public const float FreeEngageRange = 12000f;

        /// <summary>Maximum hunting distance from leader before recall, in metres.</summary>
        public const float LeashRadius = 5000f;

        /// <summary>Retreat stand-off distance from the threat, in metres.</summary>
        public const float FallBackStandoff = 6000f;

        /// <summary>Default point-hold orbit radius in metres.</summary>
        public const float OrbitRadius = 2000f;

        /// <summary>Base shot spacing in seconds; prevents repeated engagement ticks from emptying stores
        /// immediately.</summary>
        public const float FireInterval = 5f;

        /// <summary>Near-exhaustion fuel reserve allowed to interrupt a saturation salvo.</summary>
        public const float SplashCriticalFuel = 0.03f;

        /// <summary>Default maximum assigned shooters per target; effectiveness may reduce it and missiles
        /// receive one interceptor.</summary>
        public const int MaxWingmenPerTarget = 2;

        /// <summary>Fuel fraction triggering bingo RTB.</summary>
        public const float BingoFuel = 0.15f;

        /// <summary>Selectable launch fractions of full tank capacity, independent of preset fuel. Lower
        /// fills trade endurance for mass.</summary>
        public static readonly float[] SpawnFuelSteps = { 0.25f, 0.5f, 0.75f, 1f };

        /// <summary>Default requisition fuel fraction: full capacity.</summary>
        public const float DefaultSpawnFuel = 1f;

        // Delivery tuning.

        /// <summary>Stable-flight clearance above native takeoff's 75 m release, combined with speed,
        /// sink, and dwell gates.</summary>
        public const float FixedWingAirborneAlt = 80f;

        public const float LaunchSpeedMargin = 1.1f;
        public const float LaunchClearanceMinimum = 30f;
        public const float LaunchClearanceMargin = 10f;
        public const float DepartureTurnBank = 25f;
        public const float RejoinBankHeightSpan = 320f;
        public const float RejoinMaximumBank = 88f;
        public const float RejoinMinimumBank = 8f;
        public const float CollisionHorizon = 6f;
        public const float CollisionMinimumRadius = 45f;
        public const float CollisionCourseBias = 0.65f;
        public const float HoldPositionGain = 1.65f;
        public const float HoldDampingGain = 1.25f;
        public const float SlotVelocityLimit = 100f;

        /// <summary>Queued hangar retry interval in seconds; occupancy checks prevent repeated native
        /// charges.</summary>
        public const float HangarRetryInterval = 0.5f;

        /// <summary>Delivery timeout budget in seconds, long enough for native carrier doors and
        /// departure. Accepted unfinished sequences still require safe settlement checks.</summary>
        public const float HangarDeliveryTimeout = 420f;


        // Economy tuning.

        /// <summary>One-time command-right fee for active mission aircraft, as a fraction of list
        /// price.</summary>
        public const float RecruitmentCostRate = 0.25f;

        /// <summary>Over-cap purchase surcharge multiplier, preserving a costly option when mission AI
        /// capacity is full or zero.</summary>
        public const float ExceedLimitCostMultiplier = 3f;

        /// <summary>Minimum player rank for over-cap requisitions.</summary>
        public const int ExceedLimitRank = 3;

        /// <summary>Concurrent over-cap purchase allowance, freed on loss or recovery rather than consumed
        /// permanently.</summary>
        public const int ExceedLimitAllowance = 4;

        // Pilot progression uses a shared triangular rank curve.

        /// <summary>XP for credited target destruction.</summary>
        public const int XpPerKill = 25;

        /// <summary>XP for recovered sorties or completed cargo deliveries.</summary>
        public const int XpPerSortie = 40;

        /// <summary>XP for surviving missile defence.</summary>
        public const int XpPerEngagement = 10;

        /// <summary>Triangular rank step: Wingman 1, Veteran 3, Ace 6, Legend 10 steps.</summary>
        public const int XpPerRank = 120;

        /// <summary>Rank-effect scale; at full effect, Legend gains roughly 12% envelope and shot-cycle
        /// improvement over Rookie.</summary>
        public const float RankEffect = 1f;

        // Move uses a commanded AGL height; Alt+scroll adjusts it while the tool is selected.

        /// <summary>Default fixed-wing Move altitude in metres AGL.</summary>
        public const float MoveAltitudeFixed = 700f;

        /// <summary>Default rotary Move altitude in metres AGL.</summary>
        public const float MoveAltitudeRotary = 180f;

        public const float MoveAltitudeStepFixed = 250f;
        public const float MoveAltitudeStepRotary = 50f;
        public const float MoveAltitudeMinFixed = 100f;
        public const float MoveAltitudeMaxFixed = 8000f;
        public const float MoveAltitudeMinRotary = 25f;
        public const float MoveAltitudeMaxRotary = 3000f;

        /// <summary>Default Move throttle/speed fraction; 1 is full.</summary>
        public const float MoveSpeedDefault = 1f;
        public const float MoveSpeedStep = 0.15f;
        public const float MoveSpeedMin = 0.4f;
        public const float MoveSpeedMax = 1f;

        /// <summary>Native AutoAim effort for map Move.</summary>
        public const float MoveEffort = 2.2f;

        /// <summary>Move bank limit below the inversion-safe formation ceiling.</summary>
        public const float MoveBank = 75f;
    }
}
