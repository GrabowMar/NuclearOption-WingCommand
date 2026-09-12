using UnityEngine;

namespace WingCommand
{
    /// <summary>Fixed-wing station keeping: throttle controls longitudinal position, steering controls
    /// lateral/vertical error, and settled roll follows the leader. Separate from rotary control because
    /// native autopilot overloads and responses differ.</summary>
    internal static class FixedWingFormation
    {
        internal sealed class FlightMemory
        {
            public readonly FormationRecovery Recovery = new FormationRecovery();
            public Aircraft Leader;
            public WingMember Member;
            public FormationRecoveryMode LastRecoveryMode;
            public float Bank = LevelBank;
            public float Airspeed;
            public float StallAirspeed;
            public float MinimumAirspeed;
            public float WindAlong;
            public float LaneSide;
            public float Dt;
            public float BankScale = 1f;
            public float FlightSeconds;
            public float NextCaptureReport = 1f;
            public bool CaptureReported;
            public int Slot;
            public bool ModeChanged;
            public bool Airbraking;
            public FormationIntercept.Plan Intercept;
            public float AvoidanceUntil;
            public Aircraft AvoidanceThreat;
            public float AvoidanceMiss;
            public Vector3 AvoidanceEscape;
        }

        /// <summary>Effort above 1 bypasses AutopilotPlane's additional squared speed reduction of turn
        /// and bank authority below corner speed; any value above 1 has the same effect.</summary>
        private const float FullAuthority = 2f;

        /// <summary>Steering look-ahead in seconds, with a distance floor; shorter baselines increase
        /// response gain.</summary>
        private const float LookAheadSeconds = 3.5f;

        /// <summary>Minimum steering look-ahead distance for slow aircraft.</summary>
        private const float MinLookAhead = 650f;





        /// <summary>Vertical proportional and damping gains need in-game tuning; damping also depends on
        /// airspeed and native pitch response.</summary>
        private const float VerticalPositionGain = 1.0f;
        private const float VerticalDriftDamping = 4.0f;

        /// <summary>Low bank authority at zero turn demand forces wings level when native desired-bank
        /// geometry is degenerate.</summary>
        private const float LevelBank = 8f;

        /// <summary>Bank authority per degree of turn demand. Excess authority bleeds speed during
        /// positional corrections.</summary>
        private const float TurnDemandGain = 3f;

        /// <summary>Keep formation bank below vertical; larger authority can make native pursuit roll
        /// through the horizon toward a rearward slot.</summary>
        internal const float MaxSafeBank = 88f;

        /// <summary>Maximum course-change demand during distant-slot interception.</summary>
        private const float MaxRejoinCommandAngle = 55f;

        /// <summary>Maximum rejoin pitch-up angle to avoid stall-inducing zoom climbs.</summary>
        private const float MaxRejoinPitchUp = 18f;

        /// <summary>Maximum commanded rejoin descent angle.</summary>
        private const float MaxRejoinPitchDown = 15f;

        /// <summary>Scale leader bank above 1 to compensate for native speed/altitude reductions in
        /// allowed bank.</summary>
        private const float BankFollowScale = 1.7f;

        /// <summary>Seconds of turn-rate feed-forward to offset filtered leader-track lag.</summary>
        private const float TurnLeadSeconds = 0.85f;





        /// <summary>Along-track closing-rate damping, in speed-demand m/s per closing m/s, to suppress
        /// repeated overshoot.</summary>
        private const float ClosingDamp = 3.0f;

        /// <summary>Speed demand per metre of along-track error, in (m/s)/m.</summary>
        private const float GapGain = 0.45f;

        /// <summary>Maximum commanded closure speed, in m/s.</summary>
        private const float MaxClosure = 90f;

        /// <summary>Height below which climb safety overrides bank matching and pursuit.</summary>
        internal static float BankMatchFloor => WingTuning.BankMatchFloor;

        /// <summary>Reduce bank near terrain or during dangerous sink. Shared with orbit to protect deck
        /// holds.</summary>
        internal static float GroundLimitedBank(float radarAlt, float requested, float verticalSpeed = 0f)
        {
            float floor = BankMatchFloor;
            float sinkRate = Mathf.Max(0f, -verticalSpeed);
            float effectiveFloor = floor + sinkRate * 4f;

            if (radarAlt >= effectiveFloor) return requested;
            float scale = Mathf.Clamp01(radarAlt / effectiveFloor);
            return Mathf.Lerp(LevelBank, requested, scale);
        }

        /// <summary>Caller-owned rejoin timing: match leader speed during staggered hold, then allow
        /// bounded boost.</summary>
        internal readonly struct Rejoin
        {
            public readonly float HoldUntil;
            public readonly float BoostUntil;

            public Rejoin(float holdUntil, float boostUntil)
            {
                HoldUntil = holdUntil;
                BoostUntil = boostUntil;
            }

            public bool Holding => Time.timeSinceLevelLoad < HoldUntil;
            public bool Boosting => Time.timeSinceLevelLoad < BoostUntil;
        }

        /// <summary>Throttle diagnostics for periodic flight reports.</summary>
        private readonly struct ThrottleState
        {
            public readonly float Gap;
            public readonly float Closing;
            public readonly float DesiredSpeed;
            public readonly float Throttle;

            /// <summary>Measured leader acceleration in m/s² for feed-forward.</summary>
            public readonly float LeaderAccel;

            /// <summary>Leader throttle anticipation copied this tick; zero at steady state.</summary>
            public readonly float Anticipation;

            public ThrottleState(float gap, float closing, float desiredSpeed, float throttle,
                                 float leaderAccel, float anticipation)
            {
                Gap = gap;
                Closing = closing;
                DesiredSpeed = desiredSpeed;
                Throttle = throttle;
                LeaderAccel = leaderAccel;
                Anticipation = anticipation;
            }
        }

        /// <param name="leaderState">Filtered motion from FormationFlyState. Use heading
        /// differentiation, not rigidbody world-y angular velocity, which mixes roll into yaw when
        /// pitched.</param>
        public static void Fly(Aircraft aircraft, Aircraft leader, ControlInputs controls,
                               GlobalPosition slotPos, Vector3 toSlot,
                               float distance, float spacing, Rejoin rejoin,
                               LeaderState leaderState, bool report, Vector3 slotVelocity,
                               System.Collections.Generic.IReadOnlyList<WingMember> members,
                               WingMember member, FlightMemory memory, float dt,
                               out Aircraft collisionThreat, out float predictedMiss)
        {
            AircraftParameters p = aircraft.GetAircraftParameters();
            memory.Dt = Mathf.Clamp(dt, 0f, 0.5f);
            memory.FlightSeconds += memory.Dt;
            memory.Slot = member.Slot;
            memory.Member = member;
            memory.BankScale = member.FlightProfile.BankScale;
            // Use wind-relative forward airspeed for aerodynamic margins; ground velocity governs
            // formation geometry.
            Vector3 wind = NetworkSceneSingleton<LevelInfo>.i != null
                ? NetworkSceneSingleton<LevelInfo>.i.GetWind(aircraft.GlobalPosition()) : Vector3.zero;
            memory.Airspeed = Vector3.Dot(aircraft.cockpit.xform.forward, aircraft.rb.velocity - wind);
            memory.WindAlong = Vector3.Dot(aircraft.cockpit.xform.forward, wind);
            float publishedStall = aircraft.definition.aircraftInfo?.stallSpeed ?? 0f;
            memory.StallAirspeed = FormationGuidance.StallAirspeed(publishedStall, p.landingSpeed);
            memory.MinimumAirspeed = FormationGuidance.MinimumAirspeed(publishedStall, p.landingSpeed);
            float minimumSpeed = memory.MinimumAirspeed;
            float leaderAlongSpeed = Vector3.Dot(leader.rb.velocity - wind, leaderState.FlatTrack);
            float gapFlat = Vector3.Dot(toSlot, leaderState.FlatTrack);
            // Newly launched distant members need bounded interception, not the slow-leader holding
            // circuit for captured slots.
            float horizontalDistance = new Vector3(toSlot.x, 0f, toSlot.z).magnitude;
            float slowRadius = FormationRecovery.HoldingRadius(minimumSpeed * 1.1f, spacing);
            bool alreadyHolding = memory.Recovery.Mode == FormationRecoveryMode.SlowLeader;
            bool allowSlowLeader = horizontalDistance <= (alreadyHolding
                ? slowRadius * 1.6f : WingTuning.CaptureDistance) &&
                (alreadyHolding || gapFlat <= spacing);
            Vector3 ownTrack = new Vector3(aircraft.rb.velocity.x, 0f, aircraft.rb.velocity.z).normalized;
            float crossTrack = Vector3.Dot(toSlot, Vector3.Cross(Vector3.up, leaderState.FlatTrack));
            bool allowOvershoot = FormationRecovery.CanYieldAhead(horizontalDistance, crossTrack,
                Vector3.Dot(ownTrack, leaderState.FlatTrack), leaderAlongSpeed, minimumSpeed, spacing,
                memory.Recovery.Mode == FormationRecoveryMode.Overshoot);
            memory.ModeChanged = memory.Recovery.UpdateMode(leaderAlongSpeed, minimumSpeed,
                gapFlat, spacing, memory.Dt, allowSlowLeader, allowOvershoot);
            if (memory.Recovery.Mode != FormationRecoveryMode.Station)
                memory.LastRecoveryMode = memory.Recovery.Mode;
            if (memory.ModeChanged)
            {
                float lateral = Vector3.Dot(aircraft.GlobalPosition() - leader.GlobalPosition(),
                    Vector3.Cross(Vector3.up, leaderState.FlatTrack));
                if (memory.Recovery.Mode != FormationRecoveryMode.Station)
                    memory.LaneSide = FormationRecovery.LaneSide(lateral, spacing, memory.Slot);
                if (Plugin.Settings.VerboseLogging.Value)
                    Plugin.LogVerbose($"[Formation] {aircraft.unitName} id={aircraft.GetInstanceID()} mode={memory.Recovery.Mode}" +
                        $" range={horizontalDistance:F0}m gap={gapFlat:F0}m cross={crossTrack:F0}m" +
                        $" leaderSpeed={leaderAlongSpeed:F1}m/s minimum={minimumSpeed:F1}m/s");
                if (memory.Recovery.Mode == FormationRecoveryMode.SlowLeader)
                    WingComms.Say(member, WingComms.Call.SlowLeader);
            }
            float terrainUrgency = aircraft.autopilot.GetTerrainWarningSystem()?.urgency ?? 0f;
            bool stableFlight = terrainUrgency <= 0f && aircraft.radarAlt > BankMatchFloor &&
                Mathf.Abs(BankOf(aircraft)) < 10f && Mathf.Abs(aircraft.rb.velocity.y) < 2f &&
                aircraft.rb.angularVelocity.magnitude < 0.1f && memory.Airspeed > minimumSpeed;
            // Exclude exact-idle airbrake drag when learning deceleration with brakes retracted.
            memory.Recovery.Observe(memory.Airspeed, controls.throttle,
                stableFlight && !memory.Airbraking && controls.throttle > 0f, dt);
            float holdBlend = FormationCollision.HoldBlend(CombatFacade.Roe.Current == WingRoe.Hold, distance, spacing);
            float aggression = Mathf.Lerp(1f, WingTuning.HoldPositionGain, holdBlend) * member.FlightProfile.CaptureGain;
            float damping = Mathf.Lerp(1f, WingTuning.HoldDampingGain, holdBlend) * member.FlightProfile.DampingScale;

            // Predict leader speed over the wingman's thrust-response time so acceleration feed-forward
            // avoids a persistent trailing gap.
            float leaderSpeed = Mathf.Max(
                leaderState.PredictedSpeed(leader.speed, memory.Recovery.ResponseSeconds), 1f);

            Vector3 leaderVel = leaderState.Velocity + slotVelocity;
            Vector3 drift = aircraft.rb.velocity - leaderVel;
            Vector3 slotOffset = slotPos - leader.GlobalPosition();
            Vector3 predictedVelocity = leaderState.Track * leaderSpeed;
            memory.Intercept = FormationIntercept.Solve(Horizontal(toSlot), Horizontal(predictedVelocity),
                Horizontal(slotOffset), Horizontal(predictedVelocity + slotVelocity), aircraft.speed,
                Mathf.Max(memory.MinimumAirspeed, p.maxSpeed + memory.WindAlong), leaderState.TurnRate);

            // Share capture-normalised error across steering, bank, and throttle decisions.
            float capture = Mathf.Max(WingTuning.CaptureDistance, 1f);
            float outOfPosition = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance / capture));

            ThrottleState throttle = Throttle(aircraft, leader, controls, p, toSlot, distance, capture, spacing,
                                              leaderSpeed, drift, aggression, damping, rejoin, outOfPosition,
                                              slotVelocity, leaderState, memory);

            bool isAvoiding = FormationCollisionGuard.TryAvoid(aircraft, leader, members, spacing,
                out Vector3 escape, out collisionThreat, out predictedMiss, memory.AvoidanceThreat);

            if (!isAvoiding && memory.FlightSeconds < memory.AvoidanceUntil && memory.AvoidanceThreat != null && !memory.AvoidanceThreat.disabled)
            {
                isAvoiding = true;
                collisionThreat = memory.AvoidanceThreat;
                escape = memory.AvoidanceEscape;
                predictedMiss = memory.AvoidanceMiss;
            }

            if (isAvoiding)
            {
                memory.AvoidanceThreat = collisionThreat;
                memory.AvoidanceEscape = escape;
                memory.AvoidanceMiss = predictedMiss;
                memory.AvoidanceUntil = memory.FlightSeconds + 0.35f;

                // Apply positive throttle to retract arrival airbrakes and maintain energy during escape
                controls.throttle = Mathf.Max(0.7f, controls.throttle);
                controls.brake = 0f;
                memory.Airbraking = false;
                Vector3 current = aircraft.rb.velocity.sqrMagnitude > 1f
                    ? aircraft.rb.velocity.normalized : aircraft.transform.forward;
                Vector3 requested = current + escape * WingTuning.CollisionCourseBias;
                FormationControlRules.SafeRejoinDirection(current.x, current.y, current.z,
                    requested.x, requested.y, requested.z, 40f, 15f, 10f, aircraft.radarAlt,
                    out float x, out float y, out float z);
                GlobalPosition avoidAim = TerrainLimitedAim(aircraft, aircraft.GlobalPosition() +
                    new Vector3(x, y, z) * Mathf.Max(MinLookAhead, aircraft.speed * LookAheadSeconds));
                float bank = GroundLimitedBank(aircraft.radarAlt,
                    Mathf.Min(55f, FormationGuidance.AirborneBankLimit(aircraft.radarAlt, memory.Airspeed, memory.StallAirspeed)),
                    aircraft.rb.velocity.y);
                float curH = Mathf.Sqrt(current.x * current.x + current.z * current.z);
                float curPitch = Mathf.Atan2(current.y, Mathf.Max(0.001f, curH)) * Mathf.Rad2Deg;
                float avoidH = Mathf.Sqrt(x * x + z * z);
                float avoidPitch = Mathf.Atan2(y, Mathf.Max(0.001f, avoidH)) * Mathf.Rad2Deg;
                bank = FormationControlRules.PitchDownBankAuthority(
                    curPitch, avoidPitch, aircraft.rb.velocity.y, 0f, bank, LevelBank);
                aircraft.autopilot.AutoAim(avoidAim, aimVelocity: true, ignoreCollisions: false,
                    runwayAlign: false, effort: FullAuthority,
                    bankAllowed: FormationControlRules.BankInput(bank, aircraft.radarAlt),
                    followTerrain: false, altitudeHold: 0f, targetVelocity: Vector3.zero);
                memory.Bank = bank;
                if (Plugin.Settings.VerboseLogging.Value)
                {
                    ReportCapture(aircraft, controls, memory, distance, spacing, avoidAim, bank, "CollisionAvoidance");
                    bool terrain = (aircraft.autopilot.GetTerrainWarningSystem()?.urgency ?? 0f) > 0f;
                    bool changed = memory.Recovery.ReportStateChanged("CollisionAvoidance", terrain);
                    bool burst = memory.Recovery.BurstReport(true, memory.Dt);
                    if (report || burst || changed)
                        ReportControl(aircraft, controls, memory, "CollisionAvoidance");
                }
                // Return before slot pursuit or roll trim can oppose escape.
                return;
            }
            memory.AvoidanceThreat = null;

            Vector3 leaderV = leader.rb != null ? leader.rb.velocity : Vector3.zero;
            float leaderH = Mathf.Sqrt(leaderV.x * leaderV.x + leaderV.z * leaderV.z);
            float effectiveLeaderClimb = leaderState.EffectiveClimb(leaderVel.y, leaderH, holdBlend);

            Steer(aircraft, leader, slotPos, toSlot, distance,
                                       leaderVel, drift, aggression, damping, spacing,
                                       outOfPosition, leaderState,
                                       effectiveLeaderClimb, throttle, report,
                                       rejoin.Holding, controls, memory);
            // Observe final native throttle after exclusion-zone escape rather than restoring the
            // arrival request.
            memory.Airbraking = controls.throttle == 0f;
        }

        // Throttle control.

        private static ThrottleState Throttle(Aircraft aircraft, Aircraft leader, ControlInputs controls,
                                              AircraftParameters p, Vector3 toSlot,
                                              float distance, float capture, float spacing,
                                               float leaderSpeed, Vector3 drift,
                                               float aggression, float damping, Rejoin rejoin,
                                               float outOfPosition, Vector3 slotVelocity, LeaderState leaderState,
                                               FlightMemory memory)
        {
            // Compensate concentric turns with leader speed plus filtered yaw rate times signed lateral
            // offset. Do not use rigidbody yaw contaminated by roll.
            float turnCompensation = Mathf.Clamp(
                Vector3.Dot(slotVelocity, leaderState.Track), -p.maxSpeed * 0.25f, p.maxSpeed * 0.25f);

            // Combine gap demand with closing-rate damping, capping overspeed by the deceleration
            // available before reaching the slot.
            Vector3 leaderForward = leaderState.FlatTrack;
            float gap = Vector3.Dot(toSlot, leaderForward);           // Positive behind the slot.
            float closing = Vector3.Dot(drift, leaderForward);         // Positive when closing forward faster than the leader.

            float closure = FormationControlRules.RejoinClosure(
                gap, closing, memory.Recovery.Braking, aggression, damping, GapGain, ClosingDamp, MaxClosure,
                memory.Recovery.ResponseSeconds);

            // Match leader speed during staggered hold instead of closing with the other members.
            if (rejoin.Holding)
                closure = Mathf.Min(closure, 0f);

            float desiredSpeed = leaderSpeed + turnCompensation + closure;
            if (!rejoin.Holding)
            {
                Vector3 slotMotion = leaderState.Velocity + slotVelocity;
                float approachSpeed = FormationTracking.ApproachSpeed(toSlot.x, toSlot.z,
                    aircraft.rb.velocity.x, aircraft.rb.velocity.z, slotMotion.x, slotMotion.z,
                    memory.Recovery.Braking, aggression, damping, memory.Recovery.ResponseSeconds,
                    slotMotion.y, leaderSpeed - leader.speed);
                desiredSpeed = Mathf.Lerp(desiredSpeed, approachSpeed, outOfPosition);
            }
            // Bound requested speed by this airframe's capability, not a multiple of leader speed.
            float minSafeSpeed = Mathf.Max(memory.MinimumAirspeed + memory.WindAlong, 1f);
            float maxUsableSpeed = Mathf.Max(p.maxSpeed + memory.WindAlong, minSafeSpeed);
            if (!rejoin.Holding && memory.Recovery.Blend < 0.01f)
                desiredSpeed = FormationClosure.PursuitSpeed(Horizontal(toSlot), Horizontal(aircraft.rb.velocity),
                    memory.Intercept, desiredSpeed, maxUsableSpeed, memory.Recovery.Braking,
                    memory.Recovery.ResponseSeconds, spacing);
            desiredSpeed = Mathf.Clamp(desiredSpeed, minSafeSpeed, maxUsableSpeed);
            // Slow-leader recovery needs sustainable flying speed, not full-power pursuit of an
            // unattainable slot.
            float recoverySpeed = memory.LastRecoveryMode == FormationRecoveryMode.SlowLeader
                ? memory.MinimumAirspeed * 1.1f + memory.WindAlong : Mathf.Max(minSafeSpeed, leaderSpeed - 10f);
            desiredSpeed = Mathf.Lerp(desiredSpeed, Mathf.Clamp(recoverySpeed, minSafeSpeed, maxUsableSpeed), memory.Recovery.Blend);

            // Use model feed-forward plus proportional correction. Avoid integral memory that would
            // preserve excess power after speed demand falls.
            float desiredAirspeed = Mathf.Max(memory.MinimumAirspeed, desiredSpeed - memory.WindAlong);
            float speedError = desiredAirspeed - memory.Airspeed;
            float throttle = Mathf.Clamp01(desiredAirspeed / Mathf.Max(p.maxSpeed, 1f))
                           + speedError * WingTuning.ThrottleGain;

            // Copy the difference between the leader's throttle and steady-speed throttle to anticipate
            // engine response. The signed term adds or removes power and vanishes at steady state.
            float anticipation = leaderState.ThrottleKnown
                ? WingTuning.AnticipationGain * ThrustModel.ThrottleAnticipation(
                      leaderState.Throttle, leader.speed,
                      Mathf.Max(leader.GetAircraftParameters().maxSpeed, 1f))
                : 0f;

            throttle += anticipation;

            // Boost only while behind the slot, inside the rejoin window, and below the
            // deceleration-limited desired speed.
            if (!rejoin.Holding && memory.Recovery.Blend < 0.01f && rejoin.Boosting && gap > 0f && speedError > 0f)
                throttle = 1f;

            float verticalSpeed = aircraft.rb != null ? aircraft.rb.velocity.y : 0f;
            Vector3 flatGap = new Vector3(toSlot.x, 0f, toSlot.z);
            float arrivalDistance = flatGap.magnitude;
            float arrivalClosing = arrivalDistance > 1f ? Vector3.Dot(drift, flatGap / arrivalDistance) : closing;
            // In the overshoot lane, shed forward closure beside the leader; rearward line of sight
            // would misclassify it as opening.
            if (memory.Recovery.Mode == FormationRecoveryMode.Overshoot)
            { arrivalDistance = Mathf.Max(0f, gap); arrivalClosing = closing; }
            var ownMotion = Horizontal(aircraft.rb.velocity);
            float alignment = ownMotion.LengthSquared() > 1f && memory.Intercept.Gap.LengthSquared() > 1f
                ? System.Numerics.Vector2.Dot(System.Numerics.Vector2.Normalize(ownMotion),
                    System.Numerics.Vector2.Normalize(memory.Intercept.Gap)) : 0f;
            var energy = FormationClosure.Resolve(throttle, speedError, memory.Airspeed, memory.MinimumAirspeed,
                arrivalDistance, arrivalClosing, spacing, memory.Recovery.Braking, memory.Recovery.ResponseSeconds,
                alignment, BankOf(aircraft), aircraft.radarAlt, verticalSpeed,
                (aircraft.autopilot.GetTerrainWarningSystem()?.urgency ?? 0f) > 0f,
                !rejoin.Holding && memory.Recovery.Blend < 0.01f,
                !rejoin.Holding && memory.Recovery.Mode != FormationRecoveryMode.SlowLeader, memory.Airbraking);
            controls.throttle = FormationControlRules.ClimbThrottleCap(energy.Throttle,
                verticalSpeed - (leaderState.Velocity + slotVelocity).y, toSlot.y,
                airspeed: memory.Airspeed, minimumSpeed: FormationClosure.LoadedMinimum(memory.MinimumAirspeed, BankOf(aircraft)),
                gap: gap, distance: distance);
            memory.Airbraking = controls.throttle == 0f;

            return new ThrottleState(gap, closing, desiredSpeed, controls.throttle,
                                     leaderState.SpeedRate, anticipation);
        }

        // Steering control.

        /// <summary>Resolved aim point and flight-report diagnostics.</summary>
        private readonly struct Aim
        {
            public readonly GlobalPosition Point;
            public readonly float Correction;
            public readonly float MaxCorrection;
            public readonly float LookAhead;

            /// <summary>Vertical slot error in metres; positive above the aircraft.</summary>
            public readonly float VerticalError;

            /// <summary>Signed vertical correction distance, in metres.</summary>
            public readonly float VerticalCorrection;

            public Aim(GlobalPosition point, float correction, float maxCorrection,
                       float lookAhead, float verticalError, float verticalCorrection)
            {
                Point = point;
                Correction = correction;
                MaxCorrection = maxCorrection;
                LookAhead = lookAhead;
                VerticalError = verticalError;
                VerticalCorrection = verticalCorrection;
            }
        }

        private static float Steer(Aircraft aircraft, Aircraft leader, GlobalPosition slotPos,
                                   Vector3 toSlot, float distance, Vector3 leaderVel,
                                   Vector3 drift, float aggression, float damping,
                                   float spacing, float outOfPosition, LeaderState leaderState,
                                   float leaderClimb,
                                   ThrottleState throttle, bool report, bool holding,
                                   ControlInputs controls, FlightMemory memory)
        {
            Aim aim = AimFor(aircraft, leader, slotPos, toSlot, distance, leaderVel, drift,
                             aggression, damping, spacing, outOfPosition, leaderState,
                             leaderClimb, holding, memory);

            bool intercept = !holding && (distance > Mathf.Max(WingTuning.CaptureDistance * 1.5f, spacing * 3.0f) || outOfPosition > 0.35f);
            // Retain curved rendezvous and bounded closure at all ranges, including overshoot recovery.

            float bankAllowed =
                BankAuthority(aircraft, leaderState.Bank, aim.Point, toSlot.y, outOfPosition, holding,
                              memory.Airspeed, memory.StallAirspeed, out float commandAngle);
            if (intercept)
                bankAllowed = GroundLimitedBank(aircraft.radarAlt,
                    Mathf.Min(FormationGuidance.InterceptBank(commandAngle),
                        FormationGuidance.AirborneBankLimit(aircraft.radarAlt, memory.Airspeed,
                            memory.StallAirspeed)), aircraft.rb.velocity.y);

            // Keep ordinary recovery turns shallow. Apply safety reductions immediately and ease only
            // authority increases.
            if (!intercept)
                bankAllowed = Mathf.Lerp(bankAllowed, Mathf.Min(bankAllowed, WingTuning.FormationRecoveryBank), memory.Recovery.Blend);
            bankAllowed = WingFlightProfile.LimitBank(bankAllowed, LevelBank, memory.BankScale);

            // Cap bank ceiling when pitched up or climbing to prevent elevator cross-coupling into zoom climbs
            float vertSpeed = aircraft.rb != null ? aircraft.rb.velocity.y : 0f;
            if (aircraft.transform.forward.y > 0.15f || vertSpeed > 5f)
            {
                bankAllowed = Mathf.Min(bankAllowed, 55f);
            }

            // Unify climb-arrest bank restriction across all modes
            Vector3 velDir = aircraft.rb != null && aircraft.rb.velocity.sqrMagnitude > 1f
                ? aircraft.rb.velocity : aircraft.transform.forward;
            float velH = Mathf.Sqrt(velDir.x * velDir.x + velDir.z * velDir.z);
            float pitchVel = Mathf.Atan2(velDir.y, Mathf.Max(1f, velH)) * Mathf.Rad2Deg;
            Vector3 aimDir = aim.Point - aircraft.GlobalPosition();
            float aimH = Mathf.Sqrt(aimDir.x * aimDir.x + aimDir.z * aimDir.z);
            float pitchAim = Mathf.Atan2(aimDir.y, Mathf.Max(1f, aimH)) * Mathf.Rad2Deg;
            bankAllowed = FormationControlRules.PitchDownBankAuthority(
                pitchVel, pitchAim, vertSpeed, toSlot.y, bankAllowed, LevelBank);

            float holdBlend = FormationCollision.HoldBlend(CombatFacade.Roe.Current == WingRoe.Hold, distance, spacing);
            float riseRate = FormationGuidance.BankRiseRate(leaderState.BankRate) * (1f + 0.8f * holdBlend);
            bankAllowed = Mathf.Min(bankAllowed, memory.Bank + riseRate * memory.Dt);
            memory.Bank = bankAllowed;
            aircraft.autopilot.AutoAim(
                destination: aim.Point,
                aimVelocity: true,
                ignoreCollisions: false,
                runwayAlign: false,
                effort: FullAuthority,
                bankAllowed: FormationControlRules.BankInput(bankAllowed, aircraft.radarAlt),
                followTerrain: false,
                // Unused by the plane overload when followTerrain=false; destination height supplies
                // vertical command. Rotary uses followTerrain=true, where altitudeHold remains active.
                altitudeHold: 0f,
                targetVelocity: Vector3.zero);

            // Native AutoAim owns actuator signs and stability filtering. Do not overwrite its
            // pitch/roll outputs with geometric commands after filtering.
            if (Plugin.Settings.VerboseLogging.Value)
            {
                bool terrain = (aircraft.autopilot.GetTerrainWarningSystem()?.urgency ?? 0f) > 0f;
                // Constant names avoid boxing/string allocation on every physics update.
                string mode = intercept ? "Intercept" : holding ? "StaggerHold" :
                    memory.Recovery.Mode == FormationRecoveryMode.SlowLeader ? "SlowLeader" :
                    memory.Recovery.Mode == FormationRecoveryMode.Overshoot ? "Overshoot" : "Station";
                ReportCapture(aircraft, controls, memory, distance, spacing, aim.Point, bankAllowed, mode);
                bool changed = memory.Recovery.ReportStateChanged(mode, terrain);
                bool unstable = memory.ModeChanged || terrain ||
                    (Mathf.Abs(BankOf(aircraft)) > 45f && Mathf.Abs(BankOf(leader)) < 10f) ||
                    Mathf.Abs(aircraft.rb.velocity.y - leaderClimb) > 20f ||
                    Mathf.Abs(Vector3.Dot(aircraft.rb.angularVelocity, aircraft.transform.forward)) * Mathf.Rad2Deg > 60f;
                bool burst = memory.Recovery.BurstReport(unstable, memory.Dt);
                if (report || burst || changed)
                {
                    Report(aircraft, leader, distance, aim, commandAngle, bankAllowed, leaderClimb, throttle, report);
                    ReportControl(aircraft, controls, memory, mode);
                }
            }

            return commandAngle;
        }

        /// <summary>Compute the autopilot aim point and its diagnostics.</summary>
        private static Aim AimFor(Aircraft aircraft, Aircraft leader, GlobalPosition slotPos,
                                  Vector3 toSlot, float distance, Vector3 leaderVel,
                                  Vector3 drift, float aggression, float damping,
                                  float spacing, float outOfPosition,
                                  LeaderState leaderState,
                                  float leaderClimb, bool holding, FlightMemory memory)
        {
            // Scale pursuit look-ahead by flight time with a distance floor so gain stays stable across
            // airspeeds and near-slot errors. Use filtered leader track and continuous slot velocity
            // for every ROE.
            Vector3 baseDir = leaderVel.sqrMagnitude > 1f
                ? leaderVel.normalized : leaderState.Track;

            // Use average direction over a short preview arc for stable turn anticipation.
            var previewArc = FormationTracking.Arc(baseDir.x, 0f, baseDir.z,
                leaderState.TurnRate, TurnLeadSeconds * 2f);
            baseDir = new Vector3(previewArc.x, 0f, previewArc.z);

            // Handle vertical motion separately in the damped altitude command.
            baseDir.y = 0f;
            if (baseDir.sqrMagnitude < 0.0001f)
                baseDir = new Vector3(aircraft.transform.forward.x, 0f, aircraft.transform.forward.z);
            if (baseDir.sqrMagnitude < 0.0001f) baseDir = Vector3.forward;
            baseDir.Normalize();

            float lookAhead = Mathf.Max(aircraft.speed * LookAheadSeconds, MinLookAhead);
            Vector3 ownVelocity = aircraft.rb.velocity;
            float horizontalSpeed = new Vector3(ownVelocity.x, 0f, ownVelocity.z).magnitude;

            // During staggered hold, fly the leader's track while throttle matches speed; defer slot
            // pursuit until the intercept window.
            if (holding)
                return new Aim(TerrainLimitedAim(aircraft,
                                   aircraft.GlobalPosition() + baseDir * lookAhead + Vector3.up *
                                   FormationControlRules.VerticalAimRise(lookAhead, horizontalSpeed,
                                       lookAhead, leaderClimb, 0f)), 0f, 0f,
                               lookAhead, toSlot.y, 0f);

            float maxAngle = Mathf.Clamp(WingTuning.CommandAngle, 1f, 80f);
            var horizontal = FormationGuidance.Horizontal(Horizontal(toSlot), Horizontal(ownVelocity),
                Horizontal(leaderVel), Horizontal(baseDir), memory.Intercept.Gap, memory.Intercept.ArrivalVelocity,
                distance, lookAhead, aircraft.speed, outOfPosition, aggression, damping);
            float maxCorrection = horizontal.MaxCorrection;
            Vector3 flatCorrection = new Vector3(horizontal.Correction.X, 0f, horizontal.Correction.Y);
            // Correct altitude proportionally and damp relative slot climb.
            float vertDrift = (aircraft.rb != null ? aircraft.rb.velocity.y : 0f) - leaderClimb;

            float maxVerticalCorrection = Mathf.Lerp(20f, maxCorrection, outOfPosition);
            float verticalCorrection = FormationControlRules.KinematicVerticalCorrection(
                toSlot.y, vertDrift, maxVerticalCorrection,
                VerticalPositionGain, VerticalDriftDamping, aggression, damping);

            // Prevent low-altitude corrections from commanding descent.
            if (aircraft.radarAlt < 250f && verticalCorrection < 0f)
            {
                float maxFloorDescent = Mathf.Max(0f, aircraft.radarAlt - 60f);
                verticalCorrection = Mathf.Max(verticalCorrection, -maxFloorDescent);
            }

            Vector3 correction = flatCorrection + Vector3.up * verticalCorrection;

            Vector3 requested = new Vector3(horizontal.Aim.X, 0f, horizontal.Aim.Y);
            if (memory.Recovery.Blend > 0f && !holding)
            {
                Vector3 recoveryDirection = RecoveryDirection(aircraft, leader, slotPos, baseDir, spacing, memory);
                Vector3 recoveryAim = recoveryDirection * lookAhead;
                // Keep tracking slot altitude while blending into a holding circuit.
                requested.x = Mathf.Lerp(requested.x, recoveryAim.x, memory.Recovery.Blend);
                requested.z = Mathf.Lerp(requested.z, recoveryAim.z, memory.Recovery.Blend);
            }
            // Apply pursuit only horizontally. Preserve the same vertical damping at every range,
            // scaled by actual horizontal aim distance.
            float horizontalDistance = new Vector3(requested.x, 0f, requested.z).magnitude;
            requested.y = FormationControlRules.VerticalAimRise(horizontalDistance, horizontalSpeed,
                lookAhead, leaderClimb, verticalCorrection);
            GlobalPosition aimPoint = aircraft.GlobalPosition() + requested;
            Vector3 currentDirection = aircraft.rb.velocity.sqrMagnitude > 1f
                ? aircraft.rb.velocity.normalized
                : aircraft.transform.forward;
            if (requested.sqrMagnitude > 1f)
            {
                float allowed = Mathf.Lerp(maxAngle, MaxRejoinCommandAngle, outOfPosition);
                allowed = Mathf.Lerp(allowed, Mathf.Min(allowed, WingTuning.FormationRecoveryHeading), memory.Recovery.Blend);
                if (aircraft.radarAlt < BankMatchFloor)
                {
                    float scale = Mathf.Clamp01(aircraft.radarAlt / BankMatchFloor);
                    allowed = Mathf.Lerp(maxAngle * 0.25f, allowed, scale);
                }
                float allowedPitchUp = Mathf.Lerp(4.0f, MaxRejoinPitchUp, outOfPosition);
                float allowedPitchDown = Mathf.Lerp(3.5f, MaxRejoinPitchDown, outOfPosition);
                FormationControlRules.SafeRejoinDirection(
                    currentDirection.x, currentDirection.y, currentDirection.z,
                    requested.x, requested.y, requested.z,
                    allowed,
                    allowedPitchUp,
                    allowedPitchDown,
                    aircraft.radarAlt,
                    out float sx, out float sy, out float sz);

                Vector3 safeDirection = new Vector3(sx, sy, sz);

                aimPoint = aircraft.GlobalPosition()
                         + safeDirection * Mathf.Max(requested.magnitude, lookAhead);
            }

            aimPoint = TerrainLimitedAim(aircraft, aimPoint);

            return new Aim(aimPoint, correction.magnitude, maxCorrection, lookAhead,
                           toSlot.y, verticalCorrection);
        }

        private static Vector3 RecoveryDirection(Aircraft aircraft, Aircraft leader, GlobalPosition slotPos,
            Vector3 heading, float spacing, FlightMemory memory)
        {
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            Vector3 delta = aircraft.GlobalPosition() - leader.GlobalPosition();
            delta.y = 0f;
            if (memory.LastRecoveryMode == FormationRecoveryMode.SlowLeader)
            {
                // Centre holding near this slot; slot spacing, vertical stagger, and collision guarding
                // provide separation without extra rings per member.
                float speed = memory.MinimumAirspeed * 1.1f;
                float radius = FormationRecovery.HoldingRadius(speed, spacing);
                delta = aircraft.GlobalPosition() - slotPos;
                delta.y = 0f;
                Vector3 radial = delta.sqrMagnitude > 1f ? delta.normalized : right;
                Vector3 tangent = Vector3.Cross(Vector3.up, radial);
                Vector3 course = (tangent - radial * Mathf.Clamp((delta.magnitude - radius) / radius, -1f, 1f)).normalized;
                // Add leader translation to the circuit tangent so its moving centre does not escape
                // the aircraft.
                Vector3 leaderMotion = leader.rb.velocity;
                leaderMotion.y = 0f;
                float circulationSpeed = FormationRecovery.CirculationSpeed(
                    Vector3.Dot(leaderMotion, course), leaderMotion.magnitude, speed);
                return (leaderMotion + course * circulationSpeed).normalized;
            }
            // Keep a stable side lane and continue forward beside the leader instead of crossing its
            // nose to chase behind.
            float side = memory.LaneSide;
            float lane = side * spacing * (2f + memory.Slot / 2);
            float lateral = FormationRecovery.LaneCorrection(Vector3.Dot(delta, right), lane,
                Mathf.Max(MinLookAhead, aircraft.speed * LookAheadSeconds));
            return (heading + right * lateral).normalized;
        }

        private static System.Numerics.Vector2 Horizontal(Vector3 vector) =>
            new System.Numerics.Vector2(vector.x, vector.z);

        // Terrain-limit both pursuit and staggered holds.
        private static GlobalPosition TerrainLimitedAim(Aircraft aircraft, GlobalPosition point)
        {
            float safeY = FormationSafety.AimAltitude(
                aircraft.GlobalPosition().y, point.y, aircraft.radarAlt);
            return new GlobalPosition(point.x, safeY, point.z);
        }

        /// <summary>Compute bank allowance from genuine turn demand.</summary>
        private static float BankAuthority(Aircraft aircraft, float leaderBank,
                                           GlobalPosition aimPoint, float verticalError, float outOfPosition,
                                           bool holding, float airspeed, float stallAirspeed, out float commandAngle)
        {
            // Native desired-bank geometry uses a velocity/command cross product that vanishes in
            // straight flight. Keep allowance low there to force wings level; increase with leader bank
            // and commanded turn angle.
            Vector3 aimDir = aimPoint - aircraft.GlobalPosition();
            Vector3 velocityDir = aircraft.rb.velocity;

            commandAngle = (aimDir.sqrMagnitude > 1f && velocityDir.sqrMagnitude > 1f)
                ? Vector3.Angle(velocityDir, aimDir)
                : 0f;

            float leaderBankMag = Mathf.Abs(leaderBank);
            float horizontalAngle = FormationControlRules.HorizontalAngle(
                velocityDir.x, velocityDir.z, aimDir.x, aimDir.z);
            float turnDemand = leaderBankMag + horizontalAngle * TurnDemandGain;

            // Raise the rejoin ceiling with error while retaining the low straight-flight floor;
            // distant slots alone must not permit noise-driven rolls.
            float maxBank = holding
                ? WingTuning.RejoinHoldBank
                : Mathf.Lerp(WingTuning.StationBank,
                             WingTuning.PursuitBank,
                             outOfPosition);

            // Allow genuine leader-bank demand above the settled ceiling so turns do not force
            // followers wide.
            maxBank = Mathf.Max(maxBank, leaderBankMag * BankFollowScale + LevelBank);
            float safeCeiling = (aircraft.transform.forward.y > 0.15f || (aircraft.rb != null && aircraft.rb.velocity.y > 5f))
                ? 55f : MaxSafeBank;
            maxBank = Mathf.Min(maxBank, safeCeiling);

            // Limit sink-related bank reduction by terrain clearance; a high-altitude descent is not a
            // terrain emergency.
            float verticalSpeed = aircraft.rb != null ? aircraft.rb.velocity.y : 0f;
            maxBank = GroundLimitedBank(aircraft.radarAlt, maxBank, verticalSpeed);
            maxBank = Mathf.Min(maxBank, FormationGuidance.AirborneBankLimit(aircraft.radarAlt,
                airspeed, stallAirspeed));

            float bankAllowed = Mathf.Clamp(turnDemand, LevelBank, maxBank);

            // When native pursuit would invert to pitch down, restrict bank toward level to preserve
            // downward elevator authority.
            float velH = Mathf.Sqrt(velocityDir.x * velocityDir.x + velocityDir.z * velocityDir.z);
            float pitchVel = Mathf.Atan2(velocityDir.y, Mathf.Max(1f, velH)) * Mathf.Rad2Deg;

            float aimH = Mathf.Sqrt(aimDir.x * aimDir.x + aimDir.z * aimDir.z);
            float pitchAim = Mathf.Atan2(aimDir.y, Mathf.Max(1f, aimH)) * Mathf.Rad2Deg;

            bankAllowed = FormationControlRules.PitchDownBankAuthority(
                pitchVel, pitchAim, verticalSpeed, verticalError, bankAllowed, LevelBank);

            return bankAllowed;
        }

        // Bank measurement.

        /// <summary>Measure bank around the aircraft's forward axis using wing-right versus horizon-right,
        /// avoiding pitch leakage. Vertical flight is degenerate because bank has no defined horizon
        /// reference.</summary>
        internal static float BankOf(Aircraft aircraft)
        {
            Vector3 forward = aircraft.transform.forward;
            Vector3 horizonRight = Vector3.Cross(Vector3.up, forward);

            if (horizonRight.sqrMagnitude < 0.0001f) return 0f;

            return Vector3.SignedAngle(horizonRight.normalized, aircraft.transform.right, forward);
        }

        private static void ReportCapture(Aircraft aircraft, ControlInputs controls, FlightMemory memory,
            float distance, float spacing, GlobalPosition aim, float bankAllowed, string mode)
        {
            if (Plugin.Settings == null || !Plugin.Settings.VerboseLogging.Value) return;
            bool joining = distance > Mathf.Max(WingTuning.CaptureDistance, spacing * 2f);
            if (memory.FlightSeconds < memory.NextCaptureReport || (memory.CaptureReported && !joining)) return;
            memory.CaptureReported = true;
            memory.NextCaptureReport = memory.FlightSeconds + 20f;
            Vector3 direction = aim - aircraft.GlobalPosition();
            AircraftParameters p = aircraft.GetAircraftParameters();
            float urgency = aircraft.autopilot.GetTerrainWarningSystem()?.urgency ?? 0f;
            Plugin.LogVerbose($"[FormationCapture] id={aircraft.GetInstanceID()} slot={memory.Slot} mode={mode}" +
                $" safety={(urgency > 0f ? "TerrainAvoidance" : "None")} terrain={urgency:F2}" +
                $" leaderId={(memory.Leader != null ? memory.Leader.GetInstanceID() : 0)}" +
                $" decision={memory.Member.Behaviour.ReflexId}->{memory.Member.Behaviour.BehaviourId}" +
                $" pilotState={memory.Member.Pilot?.currentState?.GetType().Name}" +
                $" range={distance:F0}m speed={aircraft.speed:F1}m/s air={memory.Airspeed:F1}m/s" +
                $" leader={memory.Leader?.speed:F1}m/s minimum={memory.MinimumAirspeed:F1} stall={memory.StallAirspeed:F1}" +
                $" landing={p.landingSpeed:F1} takeoff={p.takeoffSpeed:F1} max={p.maxSpeed:F1}" +
                $" headingError={FormationControlRules.HorizontalAngle(aircraft.rb.velocity.x, aircraft.rb.velocity.z, direction.x, direction.z):F1}" +
                $" bank={BankOf(aircraft):F1}/{bankAllowed:F1} altitude={aircraft.radarAlt:F0}" +
                $" input={controls.pitch:F2},{controls.roll:F2},{controls.yaw:F2} throttle={controls.throttle:F2}" +
                $" nozzleAxis={controls.customAxis1:F2} hover={aircraft.IsAutoHoverEnabled()} sameInputs={ReferenceEquals(controls, aircraft.GetInputs())}");
        }

        private static void ReportControl(Aircraft aircraft, ControlInputs controls, FlightMemory memory, string mode)
        {
            if (Plugin.Settings == null || !Plugin.Settings.VerboseLogging.Value) return;
            float minimum = memory.MinimumAirspeed;
            float urgency = aircraft.autopilot.GetTerrainWarningSystem()?.urgency ?? 0f;
            Plugin.LogVerbose($"[FormationControl] t={Time.timeSinceLevelLoad:F2} id={aircraft.GetInstanceID()} " +
                $"mode={mode} safety={(urgency > 0f ? "TerrainAvoidance" : "None")} " +
                $"leaderId={(memory.Leader != null ? memory.Leader.GetInstanceID() : 0)} " +
                $"decision={memory.Member.Behaviour.ReflexId}->{memory.Member.Behaviour.BehaviourId} " +
                $"blend={memory.Recovery.Blend:F2} pitch={controls.pitch:F3} roll={controls.roll:F3} " +
                $"throttle={controls.throttle:F3} airspeed={memory.Airspeed:F1} margin={memory.Airspeed - minimum:F1} " +
                $"terrain={urgency:F2} braking={memory.Recovery.Braking:F2} response={memory.Recovery.ResponseSeconds:F2}" +
                $" airbrake={memory.Airbraking} interceptLead={memory.Intercept.Seconds:F1}s");
        }
        /// <summary>Report fixed-wing station keeping periodically, including command saturation that
        /// indicates exhausted correction authority.</summary>
        private static void Report(Aircraft aircraft, Aircraft leader, float distance,
                                   Aim aim, float commandAngle, float bankAllowed,
                                   float leaderClimb, ThrottleState throttle, bool includeNeighbors)
        {
            if (Plugin.Settings == null || !Plugin.Settings.VerboseLogging.Value) return;
            float correction = aim.Correction;
            float maxCorrection = aim.MaxCorrection;
            float lookAhead = aim.LookAhead;
            bool saturated = maxCorrection > 0f && correction >= maxCorrection * 0.99f;

            // Report vertical error, correction, and both climb rates separately so slot tracking and
            // altitude oscillation can be distinguished.
            float ownClimb = aircraft.rb != null ? aircraft.rb.velocity.y : 0f;
            Vector3 finalAim = aim.Point - aircraft.GlobalPosition();
            float aimPitch = Mathf.Atan2(finalAim.y,
                new Vector3(finalAim.x, 0f, finalAim.z).magnitude) * Mathf.Rad2Deg;
            Vector3 vel = aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero;
            float velH = new Vector3(vel.x, 0f, vel.z).magnitude;
            float ownPitch = Mathf.Atan2(ownClimb, Mathf.Max(1f, velH)) * Mathf.Rad2Deg;

            // Compare follower bank and roll rate against the leader to distinguish coordinated turning
            // from unwanted roll.
            float rollRate = Vector3.Dot(aircraft.rb.angularVelocity, aircraft.transform.forward)
                             * Mathf.Rad2Deg;

            Plugin.LogVerbose(
                $"[Formation] {aircraft.unitName} id={aircraft.GetInstanceID()} shape={WingFormation.Shape}: error {distance:F0} m, " +
                $"gap {throttle.Gap:F0} m, closing {throttle.Closing:F0} m/s along leader track, " +
                $"speed {aircraft.speed:F0} -> {throttle.DesiredSpeed:F0} m/s, thr {throttle.Throttle:F2}, " +
                $"leader accel {throttle.LeaderAccel:F1} m/s2, anticip {throttle.Anticipation:+0.00;-0.00; 0.00}, " +
                $"correction {correction:F0}/{maxCorrection:F0} m{(maxCorrection <= 0f ? " (DISABLED)" : saturated ? " (SATURATED)" : "")}, " +
                $"baseline {lookAhead:F0} m, " +
                $"bank {BankOf(aircraft):F0} vs leader {BankOf(leader):F0} deg, " +
                $"roll rate {rollRate:F0} deg/s, " +
                $"cmd {commandAngle:F1} deg, bank allowed {bankAllowed:F0} deg, " +
                $"bank input {FormationControlRules.BankInput(bankAllowed, aircraft.radarAlt):F1} deg, " +
                $"vert err {aim.VerticalError:+0;-0; 0} m, " +
                $"vert corr {aim.VerticalCorrection:+0;-0; 0} m, " +
                $"climb {ownClimb:+0.0;-0.0; 0.0} vs leader {leader.rb.velocity.y:+0.0;-0.0; 0.0} m/s, " +
                $"slot climb {leaderClimb:+0.0;-0.0; 0.0} m/s, pitch {ownPitch:+0.0;-0.0; 0.0} -> aim {aimPitch:+0.0;-0.0; 0.0} deg, " +
                // The safety guard still checks collisions every control update; this scan is only
                // diagnostic and runs on the five-second routine report, not every burst sample.
                $"radar alt {aircraft.radarAlt:F0} m{(includeNeighbors ? ", " + NearestPass(aircraft) : "")}");
        }

        private static string NearestPass(Aircraft aircraft)
        {
            WingRegistry wing = WingCommandManager.Instance?.Wing;
            if (wing == null || aircraft.rb == null) return "neighbor=none";
            Aircraft nearest = null;
            float miss = float.PositiveInfinity;
            float range = 0f;
            foreach (WingMember member in wing.Members)
            {
                Aircraft other = member.Aircraft;
                if (other == null || other == aircraft || other.disabled || other.rb == null) continue;
                Vector3 delta = other.transform.position - aircraft.transform.position;
                Vector3 velocity = other.rb.velocity - aircraft.rb.velocity;
                float time = velocity.sqrMagnitude > 1f
                    ? Mathf.Clamp(-Vector3.Dot(delta, velocity) / velocity.sqrMagnitude, 0f, 4f) : 0f;
                float predicted = (delta + velocity * time).magnitude;
                if (predicted >= miss) continue;
                nearest = other;
                miss = predicted;
                range = delta.magnitude;
            }
            return nearest == null ? "neighbor=none" :
                $"neighbor={nearest.GetInstanceID()} range={range:F0} m predicted miss={miss:F0} m";
        }

    }
}
