using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Formation flight for fixed-wing aircraft: throttle to hold fore-and-aft position,
    /// steering to hold the slot, and a bank match while settled.
    ///
    /// The counterpart to <see cref="RotaryFormation"/>, and separate from it for the same
    /// reason: <c>AutopilotPlane</c> and <c>AutopilotHelo</c> override different
    /// <c>AutoAim</c> overloads and respond to completely different commands, so one
    /// shared controller could only ever suit one of them.
    ///
    /// The technique is the standard one — power holds fore-and-aft position, bank holds
    /// lateral, and closure is arrested early because throttle response lags.
    ///
    /// Everything here is expressed in the unit it acts in. The previous version set a
    /// look-ahead distance and a maximum correction distance, which between them produced
    /// a maximum command angle of ten degrees that neither of them named — so tuning
    /// either one silently moved a quantity nobody was looking at.
    /// </summary>
    internal static class FixedWingFormation
    {
        internal sealed class FlightMemory
        {
            public readonly FormationRecovery Recovery = new FormationRecovery();
            public Aircraft Leader;
            public FormationRecoveryMode LastRecoveryMode;
            public float Bank = LevelBank;
            public float Trim;
            public float Airspeed;
            public float WindAlong;
            public float LaneSide;
            public float Dt;
            public int Slot;
            public bool ModeChanged;
        }

        /// <summary>
        /// Effort passed to <c>AutoAim</c>. Any value above 1 matters, and the exact value
        /// does not.
        ///
        /// <c>AutopilotPlane</c> computes
        /// <c>num3 = (effort > 1 || radarAlt &lt; 1) ? 1 : clamp01(airspeed / cornerSpeed)</c>
        /// and then spends it twice: as <c>RotateTowards(..., 0.9 * num3²)</c>, the cap on
        /// how far the commanded direction may swing in one step, and as
        /// <c>bankAllowed *= max(num3², 0.45)</c>. Below corner speed both shrink
        /// quadratically, so a wingman that has slowed down — exactly the one struggling to
        /// hold station — was having its own turn authority halved on top of the real
        /// aerodynamic penalty it already pays. Passing more than 1 removes that
        /// double-counting.
        /// </summary>
        private const float FullAuthority = 2f;

        /// <summary>
        /// Seconds of flight used as the steering baseline. This is the loop gain: the
        /// aircraft's velocity is rotated towards a point this far ahead. Tightened from 4.5
        /// to 3.5 seconds (with a 650 m floor) to tighten the formation leash and make wingmen
        /// respond crisply to player turns and manoeuvres without oscillating.
        /// </summary>
        private const float LookAheadSeconds = 3.5f;

        /// <summary>Shortest baseline, for aircraft slow enough that seconds alone is not enough.</summary>
        private const float MinLookAhead = 650f;

        /// <summary>
        /// Slot radius, as a fraction of spacing, inside which position is not chased at
        /// all. Tightened from 0.085 to 0.025 (~3 m at standard spacing) so wingmen hold a
        /// much tighter leash and react immediately to drifting off their slots.
        /// </summary>
        private const float SlotZoneInner = 0.025f;

        /// <summary>
        /// Radius at which position correction reaches full authority. Pulled in from 0.35
        /// to 0.18 alongside <see cref="SlotZoneInner"/> so correction ramps up over a much
        /// tighter band and wingmen stay firmly locked in formation during manoeuvres.
        /// </summary>
        private const float SlotZoneOuter = 0.18f;

        /// <summary>
        /// Correction and damping gains for the vertical axis.
        ///
        /// Dedicated vertical gains with a soft position zone. These require in-game tuning:
        /// damping ratio also depends on airspeed and the autopilot's pitch response.
        /// </summary>
        private const float VerticalPositionGain = 1.0f;
        private const float VerticalDriftDamping = 4.0f;

        /// <summary>
        /// Bank authority when nothing is being asked of the roll axis. Small on purpose:
        /// it is what forces the aircraft back to wings level when the autopilot's own
        /// desired-bank term is undefined.
        /// </summary>
        private const float LevelBank = 8f;

        /// <summary>
        /// Degrees of bank authority granted per degree of commanded turn. A commanded
        /// heading change of θ over the look-ahead time needs roughly
        /// atan(v·θ / (T·g)) degrees of bank — about two and a half per degree at cruise —
        /// not the five the first aggressive pass granted: that had a wingman correcting a
        /// twenty-five degree position error with a ninety degree bank, bleeding all its
        /// speed in the process and dropping straight off the back of the formation.
        /// </summary>
        private const float TurnDemandGain = 3f;

        /// <summary>
        /// Formation capture is not an aerobatic order. Authority beyond a vertical bank
        /// lets AutoAim roll through the horizon to chase a slot behind the aircraft, which
        /// the live log showed as 200-277 degree requests followed by pilot loss/ejection.
        /// </summary>
        internal const float MaxSafeBank = 88f;

        /// <summary>Largest course change allowed while intercepting a distant slot.</summary>
        private const float MaxRejoinCommandAngle = 55f;

        /// <summary>Maximum pitch-up angle allowed during rejoin navigation to prevent zoom-climbing into stall.</summary>
        private const float MaxRejoinPitchUp = 18f;

        /// <summary>Maximum pitch-down angle allowed during rejoin navigation.</summary>
        private const float MaxRejoinPitchDown = 15f;

        /// <summary>
        /// How much bank authority to grant relative to the leader's own bank. Above one
        /// on purpose: the autopilot de-rates <c>bankAllowed</c> by altitude and speed —
        /// down to 0.6 at low altitude — so asking for exactly the leader's bank would hand
        /// back only a fraction of it and the wingman would still swing wide.
        /// </summary>
        private const float BankFollowScale = 1.7f;

        /// <summary>
        /// Seconds of leader turn-rate fed forward into the reference direction. The leader
        /// track is low-pass filtered, so it always lags the real heading; this rotates the
        /// aim by the leader's yaw rate so the wingman starts turning the instant the player
        /// does, instead of waiting for the smoothing to catch up. It is the "cheat" that
        /// makes the formation read as glued to the player rather than chasing a memory.
        /// </summary>
        private const float TurnLeadSeconds = 0.85f;

        /// <summary>
        /// Correction gain on slot error, before <c>Aggression</c> scales it.
        ///
        /// The lateral loop is a damped oscillator with ωₙ² = v·P/(τ·L) and
        /// ζ = D/(2√(P·τ·L/v)). The old gains (P = 2.5, D = 1.6) gave ζ ≈ 0.15 — an
        /// almost undamped pendulum, which is exactly what "sways left and right" is.
        /// P is lowered and D raised so ζ sits near 0.8 across the speed range.
        /// </summary>
        private const float PositionGain = 1.35f;

        /// <summary>
        /// Damping gain on drift relative to the leader, before <c>Damping</c>. Chosen with
        /// <see cref="PositionGain"/> for a damping ratio near 0.8: most of the correction
        /// authority now goes to arresting the drift the position error created, which is
        /// what stops the wingman from swinging through the slot on every correction.
        /// </summary>
        private const float DriftDamping = 5.0f;

        /// <summary>
        /// Damping on the along-track closing rate, in m/s of speed demand per m/s of
        /// closing rate. The throttle loop had the same disease as the lateral loop:
        /// with 0.4 the damping ratio was around 0.15, so the wingman swung through the
        /// slot like a pendulum — speed up, catch the leader, cut power, fall behind,
        /// repeat. Near 3 the loop is overdamped and the cycle is gone.
        /// </summary>
        private const float ClosingDamp = 3.0f;

        /// <summary>Speed demand per metre of along-track gap, in (m/s)/m.</summary>
        private const float GapGain = 0.45f;

        /// <summary>Hard ceiling on the closing speed demand, in m/s.</summary>
        private const float MaxClosure = 90f;

        /// <summary>
        /// Bank beyond which the bank match disengages. The failure this guards against is
        /// real: driven from angle error alone the roll command was an undamped integrator
        /// and rolled wingmen inverted into the ground.
        /// </summary>
        private const float BankMatchLimit = 100f;

        /// <summary>Height below which bank match and pursuit authority yield to a climb.</summary>
        internal static float BankMatchFloor => WingTuning.BankMatchFloor;

        /// <summary>
        /// Collapse turn authority as the aircraft nears the ground or experiences a high
        /// sink rate. Shared with the orbit so a deck-hold cannot grant a steep bank either.
        /// </summary>
        internal static float GroundLimitedBank(float radarAlt, float requested, float verticalSpeed = 0f)
        {
            float floor = BankMatchFloor;
            float sinkRate = Mathf.Max(0f, -verticalSpeed);
            float effectiveFloor = floor + sinkRate * 4f;

            if (radarAlt >= effectiveFloor) return requested;
            float scale = Mathf.Clamp01(radarAlt / effectiveFloor);
            return Mathf.Lerp(LevelBank, requested, scale);
        }

        /// <summary>Roll command per degree of bank-angle error. Full stick around twenty degrees out.</summary>
        private const float BankAngleGain = 0.05f;

        /// <summary>Damping on the wingman's own roll rate, in stick fraction per rad/s.</summary>
        private const float BankRateGain = 0.5f;

        /// <summary>Leader roll rate fed forward, in stick fraction per rad/s, so a fast player roll is copied not chased.</summary>
        private const float BankFeedForward = 0.50f;

        /// <summary>Hard ceiling on the roll bias, as a fraction of full stick.</summary>
        private const float MaxBankTrim = 0.4f;

        private static bool loggedInterlock;

        /// <summary>
        /// Rejoin timing, owned by the caller. A staggered rejoin holds a wingman at the
        /// leader's speed until its turn comes, then allows a bounded boost.
        /// </summary>
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

        /// <summary>Diagnostics the throttle law hands to the periodic report.</summary>
        private readonly struct ThrottleState
        {
            public readonly float Gap;
            public readonly float Closing;
            public readonly float DesiredSpeed;
            public readonly float Throttle;

            /// <summary>The leader's measured acceleration, m/s². The ramp the speed loop is tracking.</summary>
            public readonly float LeaderAccel;

            /// <summary>Lever travel copied from the leader this tick. Zero when it is settled.</summary>
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

        /// <param name="leaderState">
        /// The leader's filtered motion, supplied by <see cref="FormationFlyState"/>. The
        /// heading rate in it is not read from the rigidbody: the world-y component of the
        /// angular velocity picks up roll rate at any nose-up attitude, and that leak was the
        /// formation's left-right sway.
        /// </param>
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
            memory.Slot = member.Slot;
            // Match the stock autopilot's forward airspeed, including wind, for
            // aerodynamic margins. Ground velocity still owns formation geometry.
            Vector3 wind = NetworkSceneSingleton<LevelInfo>.i != null
                ? NetworkSceneSingleton<LevelInfo>.i.GetWind(aircraft.GlobalPosition()) : Vector3.zero;
            memory.Airspeed = Vector3.Dot(aircraft.cockpit.xform.forward, aircraft.rb.velocity - wind);
            memory.WindAlong = Vector3.Dot(aircraft.cockpit.xform.forward, wind);
            float minimumSpeed = Mathf.Max(p.landingSpeed * 1.2f, 1f);
            float leaderAlongSpeed = Vector3.Dot(leader.rb.velocity - wind, leaderState.FlatTrack);
            float gapFlat = Vector3.Dot(toSlot, leaderState.FlatTrack);
            // A newly launched wingman can be kilometres behind a slow or landing leader.
            // It needs its bounded intercept and rejoin boost, not the close-in holding orbit
            // intended for an already captured formation slot.
            bool allowSlowLeader = distance <= WingTuning.CaptureDistance && gapFlat <= spacing;
            memory.ModeChanged = memory.Recovery.UpdateMode(leaderAlongSpeed, minimumSpeed,
                gapFlat, spacing, memory.Dt, allowSlowLeader);
            if (memory.Recovery.Mode != FormationRecoveryMode.Station)
                memory.LastRecoveryMode = memory.Recovery.Mode;
            if (memory.ModeChanged)
            {
                float lateral = Vector3.Dot(aircraft.GlobalPosition() - leader.GlobalPosition(),
                    Vector3.Cross(Vector3.up, leaderState.FlatTrack));
                if (memory.Recovery.Mode != FormationRecoveryMode.Station)
                    memory.LaneSide = FormationRecovery.LaneSide(lateral, spacing, memory.Slot);
                Plugin.Logger.LogInfo($"[Formation] {aircraft.unitName} id={aircraft.GetInstanceID()} mode={memory.Recovery.Mode}");
                if (memory.Recovery.Mode == FormationRecoveryMode.SlowLeader)
                    WingComms.Say(member, WingComms.Call.SlowLeader);
            }
            float terrainUrgency = aircraft.autopilot.GetTerrainWarningSystem()?.urgency ?? 0f;
            bool stableFlight = terrainUrgency <= 0f && aircraft.radarAlt > BankMatchFloor &&
                Mathf.Abs(BankOf(aircraft)) < 10f && Mathf.Abs(aircraft.rb.velocity.y) < 2f &&
                aircraft.rb.angularVelocity.magnitude < 0.1f && memory.Airspeed > minimumSpeed;
            memory.Recovery.Observe(memory.Airspeed, controls.throttle, stableFlight, dt);
            float leaderTurnRate = leaderState.TurnRate;
            float aggression = WingBrain.Aggression;
            float damping = WingBrain.Damping;
            float holdBlend = FormationCollision.HoldBlend(RoeRules.Current == WingRoe.Hold, distance, spacing);
            aggression *= Mathf.Lerp(1f, WingTuning.HoldPositionGain, holdBlend);
            damping *= Mathf.Lerp(1f, WingTuning.HoldDampingGain, holdBlend);

            // The speed to hold station on is the leader's speed *when we get there*, not
            // the one it has now. Formating on the current speed is a proportional
            // controller fed a ramp: through the whole of an acceleration the wingman sits a
            // fixed amount slow, so it drops back until the position term makes up the
            // difference and then holds that gap until the leader stops accelerating. The
            // lead is the wingman's own thrust-response time, so it arrives at the leader's
            // new speed with it rather than starting to chase it then.
            float leaderSpeed = Mathf.Max(
                leaderState.PredictedSpeed(leader.speed, memory.Recovery.ResponseSeconds), 1f);

            Vector3 leaderVel = leader.rb.velocity + slotVelocity;
            Vector3 drift = aircraft.rb.velocity - leaderVel;

            // How far out of position, as a fraction of the capture distance. One number
            // drives steering, bank authority and the throttle boost, so they can no longer
            // disagree about whether this wingman is settled.
            float capture = Mathf.Max(WingTuning.CaptureDistance, 1f);
            float outOfPosition = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance / capture));

            ThrottleState throttle = Throttle(aircraft, leader, controls, p, toSlot, distance, capture, spacing,
                                              leaderSpeed, drift, aggression, damping, rejoin, outOfPosition,
                                              slotVelocity, leaderState, memory);

            if (FormationCollisionGuard.TryAvoid(aircraft, leader, members, spacing,
                out Vector3 escape, out collisionThreat, out predictedMiss))
            {
                Vector3 current = aircraft.rb.velocity.sqrMagnitude > 1f
                    ? aircraft.rb.velocity.normalized : aircraft.transform.forward;
                Vector3 requested = current + escape * WingTuning.CollisionCourseBias;
                FormationControlRules.SafeRejoinDirection(current.x, current.y, current.z,
                    requested.x, requested.y, requested.z, 40f, 15f, 10f, aircraft.radarAlt,
                    out float x, out float y, out float z);
                GlobalPosition avoidAim = TerrainLimitedAim(aircraft, aircraft.GlobalPosition() +
                    new Vector3(x, y, z) * Mathf.Max(MinLookAhead, aircraft.speed * LookAheadSeconds));
                float bank = GroundLimitedBank(aircraft.radarAlt,
                    Mathf.Min(55f, LaunchSafety.RejoinBankLimit(aircraft.radarAlt, memory.Airspeed, p.takeoffSpeed)),
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
                memory.Trim = 0f;
                memory.Bank = bank;
                if (Plugin.Settings.VerboseLogging.Value && memory.Recovery.BurstReport(true, memory.Dt))
                    ReportControl(aircraft, controls, memory, "CollisionAvoidance");
                // No slot pursuit or leader roll trim may oppose this escape.
                return;
            }

            Steer(aircraft, leader, slotPos, toSlot, distance,
                                       leaderVel, drift, aggression, damping, spacing,
                                       outOfPosition, Vector3.Slerp(leaderState.Track, leaderVel.normalized, holdBlend), leaderTurnRate,
                                       leaderVel.y, throttle, report,
                                       rejoin.Holding, controls, memory);
        }

        // ------------------------------------------------------------------- throttle

        private static ThrottleState Throttle(Aircraft aircraft, Aircraft leader, ControlInputs controls,
                                              AircraftParameters p, Vector3 toSlot,
                                              float distance, float capture, float spacing,
                                               float leaderSpeed, Vector3 drift,
                                               float aggression, float damping, Rejoin rejoin,
                                               float outOfPosition, Vector3 slotVelocity, LeaderState leaderState,
                                               FlightMemory memory)
        {
            // --- Turn compensation: fly concentric arcs, not a swinging offset. ---
            // When the leader turns at heading rate w, every slot orbits the same centre, so
            // a wingman at signed lateral offset d must fly at v_leader + w*d — the outside
            // one covers more ground, the inside one less. Without this the slot sweeps
            // around the leader and wingmen get whipped through every turn.
            //
            // w is the caller's filtered heading rate. Reading it off the rigidbody meant a
            // rolling leader commanded its wingmen tens of m/s faster and slower by turns,
            // for a turn that was not happening.
            float turnCompensation = Mathf.Clamp(
                Vector3.Dot(slotVelocity, leaderState.Track), -p.maxSpeed * 0.25f, p.maxSpeed * 0.25f);

            // --- Arrival: deceleration-limited, rate-damped closure. ---
            // Two independent demands pull on the speed: the position error (gap ahead
            // means go faster) and the closing rate (already closing means ease off). The
            // gap demand is additionally capped by what the remaining distance can shed,
            // so the commanded overspeed can never exceed the one the airframe can scrub
            // off before it reaches the slot — that profile arrives at the slot at exactly
            // the leader's speed instead of arriving hot and swinging through.
            Vector3 leaderForward = leaderState.FlatTrack;
            float gap = Vector3.Dot(toSlot, leaderForward);           // + behind the slot
            float closing = Vector3.Dot(drift, leaderForward);         // + moving forward faster than the leader

            float closure = FormationControlRules.RejoinClosure(
                gap, closing, memory.Recovery.Braking, aggression, damping, GapGain, ClosingDamp, MaxClosure,
                memory.Recovery.ResponseSeconds);

            // While waiting its turn in a staggered rejoin, a wingman matches the leader's
            // speed instead of closing, so it holds its place in the queue rather than
            // arriving alongside everyone else.
            if (rejoin.Holding)
                closure = Mathf.Min(closure, 0f);

            float desiredSpeed = leaderSpeed + turnCompensation + closure;
            // Never ask an aircraft for a speed it cannot fly. The previous upper bound used
            // leaderSpeed*1.5, so a 100 m/s Cricket was routinely commanded to 150-175 m/s;
            // it stayed at full throttle, bled more speed in extreme bank, and fell farther
            // behind every report. Capability is the ceiling, not the leader's wish.
            float minSafeSpeed = Mathf.Max(p.landingSpeed * 1.2f + memory.WindAlong, 1f);
            float maxUsableSpeed = Mathf.Max(p.maxSpeed + memory.WindAlong, minSafeSpeed);
            desiredSpeed = Mathf.Clamp(desiredSpeed, minSafeSpeed, maxUsableSpeed);
            // Holding wide needs flying speed, not a permanent full-power chase of
            // a slot that a slower leader cannot make physically attainable.
            float recoverySpeed = memory.LastRecoveryMode == FormationRecoveryMode.SlowLeader
                ? p.landingSpeed * 1.2f * 1.1f + memory.WindAlong : Mathf.Max(minSafeSpeed, leaderSpeed - 10f);
            desiredSpeed = Mathf.Lerp(desiredSpeed, Mathf.Clamp(recoverySpeed, minSafeSpeed, maxUsableSpeed), memory.Recovery.Blend);

            // Feed-forward plus proportional, no integral. The feed-forward models the
            // throttle for the desired speed (the old resting point was cruise throttle,
            // the power that holds *cruise* speed — which demanded a permanent gap just to
            // earn enough power to hold station on a faster leader). The proportional term
            // covers the model's residual, a metre or two per second, so an integral is not
            // worth its memory: an integral remembers the old demand when the desired speed
            // drops, and that memory is exactly what carried a wingman through the slot.
            float desiredAirspeed = Mathf.Max(p.landingSpeed * 1.2f, desiredSpeed - memory.WindAlong);
            float speedError = desiredAirspeed - memory.Airspeed;
            float throttle = Mathf.Clamp01(desiredAirspeed / Mathf.Max(p.maxSpeed, 1f))
                           + speedError * WingTuning.ThrottleGain;

            // --- Anticipation: fly the lever, not just the speed. ---
            //
            // Everything above this line is driven by speed, and speed is the last thing to
            // move when a player works the throttle: the lever goes first, the engine spools,
            // and only then does a speed difference exist for a controller to notice. By that
            // point the wingman is already out of position and is correcting rather than
            // keeping station.
            //
            // ThrottleAnticipation is the difference between where the leader's lever is and
            // where it would be to hold the speed the leader currently has - that is, the
            // part of the player's throttle input that has not been flown yet. Copying it
            // puts the wingman's hand on the throttle at the same moment as the player's.
            //
            // The term is zero whenever the leader is settled, so it adds nothing to steady
            // formation flight and needs no gating to stay out of the way. It is also signed,
            // which is the half the old anticipation could not do: that one was a
            // Mathf.Max against the leader's lever, so it could add power for a leader
            // accelerating away but had no answer at all for one pulling power back - and a
            // wingman that keeps its throttle up through the player's deceleration is exactly
            // the one that slides out in front.
            float anticipation = leaderState.ThrottleKnown
                ? WingTuning.AnticipationGain * ThrustModel.ThrottleAnticipation(
                      leaderState.Throttle, leader.speed,
                      Mathf.Max(leader.GetAircraftParameters().maxSpeed, 1f))
                : 0f;

            throttle += anticipation;

            // Full throttle boost during rejoin order window, but only if behind the slot
            // and not already exceeding the deceleration-limited desired speed.
            if (!rejoin.Holding && memory.Recovery.Blend < 0.01f && rejoin.Boosting && gap > 0f && aircraft.speed < desiredSpeed)
                throttle = 1f;

            float rawThrottle = Mathf.Clamp01(throttle);
            float verticalSpeed = aircraft.rb != null ? aircraft.rb.velocity.y : 0f;
            controls.throttle = FormationControlRules.ClimbThrottleCap(rawThrottle, verticalSpeed, toSlot.y,
                airspeed: memory.Airspeed, minimumSpeed: Mathf.Max(p.landingSpeed * 1.2f, 1f));

            return new ThrottleState(gap, closing, desiredSpeed, controls.throttle,
                                     leaderState.SpeedRate, anticipation);
        }

        // -------------------------------------------------------------------- steering

        /// <summary>Where the aim point ended up, plus the figures the flight log reports.</summary>
        private readonly struct Aim
        {
            public readonly GlobalPosition Point;
            public readonly float Correction;
            public readonly float MaxCorrection;
            public readonly float LookAhead;

            /// <summary>Metres to the slot on the vertical axis, positive when the slot is above.</summary>
            public readonly float VerticalError;

            /// <summary>Signed metres of the correction spent on the vertical axis.</summary>
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
                                   float spacing, float outOfPosition, Vector3 smoothedLeaderDir,
                                   float leaderTurnRate, float leaderClimb,
                                   ThrottleState throttle, bool report, bool holding,
                                   ControlInputs controls, FlightMemory memory)
        {
            Aim aim = AimFor(aircraft, leader, slotPos, toSlot, distance, leaderVel, drift,
                             aggression, damping, spacing, outOfPosition, smoothedLeaderDir,
                             leaderTurnRate, leaderClimb, holding, memory);

            bool intercept = !holding && distance > Mathf.Max(WingTuning.CaptureDistance * 2f, 1500f);
            if (intercept)
            {
                // Far arrivals must point at the rendezvous, not fly a parallel track
                // with a small station-keeping correction capped to a few hundred metres.
                Vector3 interceptVector = toSlot + leader.rb.velocity * Mathf.Clamp(distance / Mathf.Max(aircraft.speed, 120f), 0f, 10f);
                float horizontal = new Vector2(interceptVector.x, interceptVector.z).magnitude;
                interceptVector.y = Mathf.Clamp(interceptVector.y, -horizontal * 0.15f, horizontal * 0.15f);
                GlobalPosition point = TerrainLimitedAim(aircraft, aircraft.GlobalPosition() +
                    interceptVector.normalized * Mathf.Max(3000f, interceptVector.magnitude));
                aim = new Aim(point, 0f, 0f, Mathf.Max(3000f, horizontal), toSlot.y, interceptVector.y);
                float desired = Mathf.Min(aircraft.GetAircraftParameters().maxSpeed, Mathf.Max(leader.speed + 60f, 150f));
                controls.throttle = aircraft.speed < desired ? 1f : aircraft.GetAircraftParameters().cruiseThrottle;
            }

            float bankAllowed =
                BankAuthority(aircraft, leader, aim.Point, toSlot.y, outOfPosition, holding,
                              memory.Airspeed, out float commandAngle);
            if (intercept)
                bankAllowed = GroundLimitedBank(aircraft.radarAlt,
                    Mathf.Min(Mathf.Clamp(commandAngle * 1.5f, LevelBank, 45f),
                        LaunchSafety.RejoinBankLimit(aircraft.radarAlt, memory.Airspeed,
                            aircraft.GetAircraftParameters().takeoffSpeed)), aircraft.rb.velocity.y);

            // Restrict ordinary recovery to shallow turns. Safety reductions happen
            // immediately; only increasing bank authority is eased.
            if (!intercept)
                bankAllowed = Mathf.Lerp(bankAllowed, Mathf.Min(bankAllowed, WingTuning.FormationRecoveryBank), memory.Recovery.Blend);
            bankAllowed = Mathf.Min(bankAllowed, memory.Bank + WingTuning.FormationBankRiseRate * memory.Dt);
            memory.Bank = bankAllowed;
            aircraft.autopilot.AutoAim(
                destination: aim.Point,
                aimVelocity: true,
                ignoreCollisions: false,
                runwayAlign: false,
                effort: FullAuthority,
                bankAllowed: FormationControlRules.BankInput(bankAllowed, aircraft.radarAlt),
                followTerrain: false,
                // Inert, and deliberately so. AutopilotPlane.AutoAim reads altitudeHold only
                // inside its `if (followTerrain)` branch - decompiled and confirmed against
                // the installed assembly - so with followTerrain false the argument has no
                // effect whatsoever. What stood here was a clamped leader.radarAlt plus a
                // vertical lead, which did nothing but read like a working altitude hold and
                // invite the vertical axis to be diagnosed in the wrong place. The whole
                // vertical command is the destination's own height; there is no second one.
                //
                // This is the plane overload only. RotaryFormation passes followTerrain true,
                // where altitudeHold is live and must describe the slot's height above ground.
                altitudeHold: 0f,
                targetVelocity: leaderVel);

            // Native terrain avoidance may have replaced our waypoint. Never put a
            // formation roll trim on top of that emergency controller's output.
            bool terrain = (aircraft.autopilot.GetTerrainWarningSystem()?.urgency ?? 0f) > 0f;
            if (!terrain && !intercept)
                MatchLeaderBank(aircraft, leader, controls, outOfPosition, commandAngle, leaderClimb, memory);
            else memory.Trim = 0f;
            bool unstable = memory.ModeChanged || terrain ||
                (Mathf.Abs(BankOf(aircraft)) > 45f && Mathf.Abs(BankOf(leader)) < 10f) ||
                Mathf.Abs(aircraft.rb.velocity.y - leaderClimb) > 20f ||
                Mathf.Abs(Vector3.Dot(aircraft.rb.angularVelocity, aircraft.transform.forward)) * Mathf.Rad2Deg > 60f;
            bool burst = Plugin.Settings.VerboseLogging.Value && memory.Recovery.BurstReport(unstable, memory.Dt);
            if (report || burst)
            {
                Report(aircraft, leader, distance, aim, commandAngle, bankAllowed, leaderClimb, throttle);
                ReportControl(aircraft, controls, memory, intercept ? "Intercept" : holding ? "StaggerHold" : memory.Recovery.Mode.ToString());
            }

            return commandAngle;
        }

        /// <summary>The point the autopilot is told to fly at, and how it was arrived at.</summary>
        private static Aim AimFor(Aircraft aircraft, Aircraft leader, GlobalPosition slotPos,
                                  Vector3 toSlot, float distance, Vector3 leaderVel,
                                  Vector3 drift, float aggression, float damping,
                                  float spacing, float outOfPosition,
                                  Vector3 smoothedLeaderDir, float leaderTurnRate,
                                  float leaderClimb, bool holding, FlightMemory memory)
        {
            // AutoAim is a pursuit controller: it rotates the aircraft's velocity toward the
            // destination and banks to chase it, so the distance to that destination sets the
            // gain of the whole loop. Aim a few hundred metres ahead and a jet covers the gap
            // in a second, which makes it a high-gain loop that oscillates; aim far ahead and
            // the same lateral error becomes a small, steady command.
            //
            // The baseline is therefore time, not distance: a fixed number of seconds of
            // flight, so it scales with speed instead of meaning something different for a
            // fast jet and a slow one. Deriving it from the correction instead — which is what
            // the first version of this did — let it collapse to a few hundred metres near the
            // slot and put the wobble straight back.
            // The reference direction is a *smoothed* leader track, not its instantaneous
            // velocity. Aimed two kilometres ahead, a wingman reproduces the direction it is
            // given one-for-one, so every small correction the player makes on the stick
            // arrives in the formation as a lateral swing of the aim point — which is what
            // "it is like I was constantly steering left and right" describes. Real wingmen
            // fly the leader's average track and let position error take care of the rest,
            // and the smoothing is short enough not to lag a genuine turn.
            Vector3 baseDir = smoothedLeaderDir.sqrMagnitude > 0.5f
                ? smoothedLeaderDir
                : (leaderVel.sqrMagnitude > 1f ? leaderVel.normalized : leader.transform.forward);

            // Feed the leader's heading rate forward so the aim point rotates with a
            // manoeuvre instead of trailing the (smoothed) heading. Without this the
            // formation turns half a second after the player does, which is the difference
            // between "in line with me" and "chasing me".
            //
            // The rate is the caller's filtered one, and it has to be: this rotation is
            // applied over a look-ahead well past a kilometre, so the roll-rate leak in the
            // rigidbody's world-y angular velocity used to translate a few degrees of roll
            // into a hundred metres of lateral aim swing — undoing, in one line, all of the
            // smoothing the line above it exists to apply.
            if (leaderTurnRate != 0f)
            {
                baseDir = Quaternion.AngleAxis(
                    leaderTurnRate * TurnLeadSeconds * Mathf.Rad2Deg, Vector3.up) * baseDir;
                baseDir.Normalize();
            }

            // Heading smoothing must not retain an old climb after a pitch reversal.
            // Vertical speed has its own command below, including slot motion.
            baseDir.y = 0f;
            if (baseDir.sqrMagnitude < 0.0001f)
                baseDir = new Vector3(aircraft.transform.forward.x, 0f, aircraft.transform.forward.z);
            if (baseDir.sqrMagnitude < 0.0001f) baseDir = Vector3.forward;
            baseDir.Normalize();

            float lookAhead = Mathf.Max(aircraft.speed * LookAheadSeconds, MinLookAhead);
            Vector3 ownVelocity = aircraft.rb.velocity;
            float horizontalSpeed = new Vector3(ownVelocity.x, 0f, ownVelocity.z).magnitude;

            // A staggered rejoin holds a wingman at the leader's track until its turn comes,
            // and the throttle already refuses to close. Chasing the slot from here is what
            // paired a full-bank pursuit with the hold's speed-match throttle and flew a
            // wingman knife-edge into the ground: fly straight along the leader's track, and
            // let the boost that follows the hold do the actual intercept.
            if (holding)
                return new Aim(TerrainLimitedAim(aircraft,
                                   aircraft.GlobalPosition() + baseDir * lookAhead + Vector3.up *
                                   FormationControlRules.VerticalAimRise(lookAhead, horizontalSpeed,
                                       lookAhead, leaderClimb, 0f)), 0f, 0f,
                               lookAhead, toSlot.y, 0f);

            // Only cross-track error steers. The along-track part is throttle's job, and
            // feeding it in here pushed the aim point forwards and backwards along the
            // direction of travel to no purpose, while inflating the correction that the
            // angle limit is measured against — so a wingman sitting behind its slot spent
            // its whole steering allowance on an error steering cannot fix.
            Vector3 across = toSlot - baseDir * Vector3.Dot(toSlot, baseDir);
            Vector3 acrossDrift = drift - baseDir * Vector3.Dot(drift, baseDir);

            // The slot is a zone, not a point.
            //
            // A hard deadband — which is what this was — is a bang-bang element: the
            // correction jumps from nothing to full the instant the error crosses the
            // threshold, which is a textbook way to produce a limit cycle. The zone now has
            // soft edges, so authority fades in across it and nothing ever switches.
            //
            // Only the proportional term is faded. Damping stays live inside the zone, which
            // is what a human formating actually does: match the leader's velocity and stop
            // caring about a few metres of position. Killing damping in the zone as well
            // would let a wingman drift out unopposed and be yanked back, which is the same
            // limit cycle wearing a different hat.
            //
            // The two axes are sized and limited separately, and they have to be.
            //
            // One isotropic zone and one ClampMagnitude over the whole 3D error treats a
            // metre up the same as a metre sideways, and the two axes are nothing like the
            // same size: see VerticalZoneInner for why that switched the vertical
            // proportional term off entirely. The clamp had the matching fault - being
            // isotropic, a lateral rejoin that saturated it (every "(SATURATED)" line in the
            // log) scaled the vertical term down with it, so the axis with the least
            // authority lost what it had precisely when the wingman was most out of place.
            //
            // Each axis has its own gains and clamp so lateral saturation cannot consume
            // the vertical correction's authority.
            float maxAngle = Mathf.Clamp(WingTuning.CommandAngle, 1f, 80f);
            float maxCorrection = lookAhead * Mathf.Tan(maxAngle * Mathf.Deg2Rad);

            Vector3 acrossFlat = new Vector3(across.x, 0f, across.z);
            Vector3 acrossDriftFlat = new Vector3(acrossDrift.x, 0f, acrossDrift.z);

            float inner = spacing * SlotZoneInner;
            float outer = spacing * SlotZoneOuter;
            float holdBlend = FormationCollision.HoldBlend(RoeRules.Current == WingRoe.Hold, distance, spacing);
            inner *= Mathf.Lerp(1f, 0.5f, holdBlend);
            outer *= Mathf.Lerp(1f, 0.5f, holdBlend);
            float ramp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, outer, acrossFlat.magnitude));

            Vector3 flatCorrection = (acrossFlat * PositionGain * aggression * ramp)
                                     - (acrossDriftFlat * DriftDamping * damping);
            // CommandAngle is the quantity being limited, and it is limited where it is
            // produced: over a baseline this long, a correction of baseline*tan(angle) is
            // exactly that many degrees of command. One configured angle, applied honestly,
            // and at cruise it allows roughly 2.5 times the correction the old fixed 220 m
            // clamp did.
            flatCorrection = Vector3.ClampMagnitude(flatCorrection, maxCorrection);

            // The vertical channel uses world altitude error and a soft position zone.
            // Within vertInner (2.5m), the proportional position gain smoothly ramps to 0 so
            // the aircraft does not continuously fight over the last metre. Damping remains 100%
            // active against current slot climb rate. A small steady altitude offset
            // inside the zone is expected; these gains alone do not guarantee critical damping.
            float vertInner = Mathf.Lerp(2.5f, 1f, holdBlend);
            float vertOuter = Mathf.Lerp(10f, 5f, holdBlend);
            float vertRamp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(vertInner, vertOuter, Mathf.Abs(toSlot.y)));
            float vertDrift = (aircraft.rb != null ? aircraft.rb.velocity.y : 0f) - leaderClimb;

            float verticalCorrection = FormationControlRules.KinematicVerticalCorrection(
                toSlot.y, vertDrift, maxCorrection,
                VerticalPositionGain, VerticalDriftDamping, aggression, damping, vertRamp);

            // Ground safety: if low, never command a descent into the terrain.
            if (aircraft.radarAlt < 250f && verticalCorrection < 0f)
            {
                float maxFloorDescent = Mathf.Max(0f, aircraft.radarAlt - 60f);
                verticalCorrection = Mathf.Max(verticalCorrection, -maxFloorDescent);
            }

            Vector3 correction = flatCorrection + Vector3.up * verticalCorrection;

            GlobalPosition stationAim = aircraft.GlobalPosition() + baseDir * lookAhead + correction;

            // Beyond capture, chase the slot itself with a lead. Blended rather than
            // switched: the old controller stepped between these two aim points at the
            // capture boundary and the autopilot chased the step.
            float leadTime = Mathf.Clamp(distance / Mathf.Max(aircraft.speed, 50f), 0f, 6f);
            GlobalPosition pursuitAim = slotPos + leaderVel * leadTime;

            // Cut the corner on a rejoin into a turn. The slot's future position lies on
            // the arc the leader is flying, not on a straight line from where it sits now,
            // so rotate the slot's offset-from-leader by the heading change the leader will
            // make over leadTime. A wingman closing from outside the turn then aims where
            // the slot is going instead of trailing it round. Bounded to half a turn; the
            // RotateTowards guard below still prevents a behind-the-tail command.
            if (WingBrain.SmartFormation && outOfPosition > 0.5f &&
                Mathf.Abs(leaderTurnRate) > 0.02f)
            {
                GlobalPosition leaderNow = leader.GlobalPosition();
                Vector3 offsetFromLeader = pursuitAim - leaderNow;
                float sweep = Mathf.Clamp(
                    leaderTurnRate * leadTime * Mathf.Rad2Deg, -180f, 180f);
                pursuitAim = leaderNow +
                             Quaternion.AngleAxis(sweep, Vector3.up) * offsetFromLeader;
            }

            // GlobalPosition has no Lerp, but subtracting two of them gives a Vector3.
            GlobalPosition aimPoint = stationAim + (pursuitAim - stationAim) * outOfPosition;

            // A pursuit point can lie behind a returning wingman after an attack. Feeding
            // that point straight to AutoAim produced 120-degree course commands and invited
            // an inversion. Intercept progressively, with a wider but still flyable limit
            // outside capture; subsequent ticks continue the turn until the slot is ahead.
            Vector3 requested = aimPoint - aircraft.GlobalPosition();
            if (memory.Recovery.Blend > 0f && !holding)
            {
                Vector3 recoveryDirection = RecoveryDirection(aircraft, leader, baseDir, spacing, memory);
                Vector3 recoveryAim = recoveryDirection * lookAhead;
                // No altitude step when entering a holding circuit: the vertical
                // controller below continues to track the formation's slot altitude.
                requested.x = Mathf.Lerp(requested.x, recoveryAim.x, memory.Recovery.Blend);
                requested.z = Mathf.Lerp(requested.z, recoveryAim.z, memory.Recovery.Blend);
            }
            // Pursuit owns only the horizontal intercept. Previously its blend reached
            // 100% at 300 m and silently discarded all vertical damping, even while the
            // report still printed a saturated downward correction. Use the same vertical
            // law at every rejoin distance, scaled by the actual horizontal aim baseline.
            float horizontalDistance = new Vector3(requested.x, 0f, requested.z).magnitude;
            requested.y = FormationControlRules.VerticalAimRise(horizontalDistance, horizontalSpeed,
                lookAhead, leaderClimb, verticalCorrection);
            aimPoint = aircraft.GlobalPosition() + requested;
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
                FormationControlRules.SafeRejoinDirection(
                    currentDirection.x, currentDirection.y, currentDirection.z,
                    requested.x, requested.y, requested.z,
                    allowed,
                    MaxRejoinPitchUp,
                    MaxRejoinPitchDown,
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

        private static Vector3 RecoveryDirection(Aircraft aircraft, Aircraft leader,
            Vector3 heading, float spacing, FlightMemory memory)
        {
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            Vector3 delta = aircraft.GlobalPosition() - leader.GlobalPosition();
            delta.y = 0f;
            if (memory.LastRecoveryMode == FormationRecoveryMode.SlowLeader)
            {
                // Same direction, separate rings. Radius comes from speed and a
                // shallow bank, so heavy/fast members are not asked to turn inside
                // their capability. Use radial error plus tangent (no phase jumps).
                float speed = aircraft.GetAircraftParameters().landingSpeed * 1.2f * 1.1f;
                float radius = Mathf.Max(spacing * 3f,
                    speed * speed / (9.81f * Mathf.Tan(WingTuning.FormationRecoveryBank * Mathf.Deg2Rad)))
                    + (memory.Slot + 1) * spacing * 2f;
                Vector3 radial = delta.sqrMagnitude > 1f ? delta.normalized : right;
                Vector3 tangent = Vector3.Cross(Vector3.up, radial);
                return (tangent - radial * Mathf.Clamp((delta.magnitude - radius) / radius, -1f, 1f)).normalized;
            }
            // Keep a stable side per slot. Fly forward beside the leader rather
            // than turning back across its nose to chase a point behind the tail.
            float side = memory.LaneSide;
            float lane = side * spacing * (2f + memory.Slot / 2);
            float lateral = FormationRecovery.LaneCorrection(Vector3.Dot(delta, right), lane,
                Mathf.Max(MinLookAhead, aircraft.speed * LookAheadSeconds));
            return (heading + right * lateral).normalized;
        }

        // Apply to pursuit AND staggered holds.
        private static GlobalPosition TerrainLimitedAim(Aircraft aircraft, GlobalPosition point)
        {
            float safeY = FormationSafety.AimAltitude(
                aircraft.GlobalPosition().y, point.y, aircraft.radarAlt);
            return new GlobalPosition(point.x, safeY, point.z);
        }

        /// <summary>How much bank the autopilot may use, and the command angle it came from.</summary>
        private static float BankAuthority(Aircraft aircraft, Aircraft leader,
                                           GlobalPosition aimPoint, float verticalError, float outOfPosition,
                                           bool holding, float airspeed, out float commandAngle)
        {
            // --- Bank authority, from actual turn demand ---
            //
            // This is the fix for the roll axis spinning while the formation sat three
            // metres off its slot, and it is a property of the game's controller rather
            // than of anything above. AutopilotPlane derives the bank it wants from
            //
            //     GetAngleOnAxis(command, up, -velocity)
            //       -> from = Cross(-velocity, command)
            //          SignedAngle(from, to, -velocity)
            //
            // and that cross product is the zero vector when the commanded direction is
            // parallel to the velocity. Settled formation flight is exactly that case: the
            // command is the leader's track and the aircraft is already flying it. So the
            // desired bank is computed from a vanishing vector, comes back as noise, and
            // `num6 = currentBank - noise` becomes the roll command. With bankAllowed at 75
            // degrees the noise was not clamped to anything, which is how a wingman holding
            // station to three metres ended up at 91 degrees of bank rolling at 200 deg/s.
            //
            // The clamp is the whole defence: with bankAllowed small, num6 collapses to
            // currentBank, which drives the aircraft to wings level — stable, and correct,
            // because when the command is parallel to the velocity there is no turn to bank
            // for. Authority is therefore granted in proportion to how much turning is
            // genuinely being asked for: the leader's own bank, plus the angle between where
            // the wingman is pointing and where it has been told to point. Both are zero in
            // level formation and both grow the moment a turn starts, so a wingman can still
            // follow anything the leader does.
            Vector3 aimDir = aimPoint - aircraft.GlobalPosition();
            Vector3 velocityDir = aircraft.rb.velocity;

            commandAngle = (aimDir.sqrMagnitude > 1f && velocityDir.sqrMagnitude > 1f)
                ? Vector3.Angle(velocityDir, aimDir)
                : 0f;

            float leaderBankMag = Mathf.Abs(BankOf(leader));
            float horizontalAngle = FormationControlRules.HorizontalAngle(
                velocityDir.x, velocityDir.z, aimDir.x, aimDir.z);
            float turnDemand = leaderBankMag + horizontalAngle * TurnDemandGain;

            // Bank authority follows genuine turn demand everywhere. Out of position the
            // ceiling rises toward PursuitBankDegrees so a hard rejoin turn is not clipped,
            // but the floor stays at LevelBank: a wingman flying straight at its slot is
            // commanded to fly straight at its slot, and granting it a constant 160 degrees
            // of bank for that is what let the autopilot's roll-noise term — the cross
            // product of velocity and command collapsing to zero — barrel-roll it. The roll
            // then bled the speed that would have closed the gap, which is why a
            // barrel-rolling wingman also fell behind and stayed there.
            float maxBank = holding
                ? WingTuning.RejoinHoldBank
                : Mathf.Lerp(WingTuning.StationBank,
                             WingTuning.PursuitBank,
                             outOfPosition);

            // The settled ceiling exists to stop the roll axis going chaotic in level
            // flight, but it must never clip a genuine turn: a leader banked hard needs a
            // wingman banked hard, whatever its slot state. Without this a wingman swung
            // wide on a fast manoeuvre, dropped behind, and then spent the whole chase
            // catching back up — the exact "they spend a lot of time chasing me" symptom.
            maxBank = Mathf.Max(maxBank, leaderBankMag * BankFollowScale + LevelBank);
            maxBank = Mathf.Min(maxBank, MaxSafeBank);

            // Sink limits depend on terrain clearance. A coordinated descending turn
            // at 3000 m must not lose 75% of its bank authority simply for descending.
            float verticalSpeed = aircraft.rb != null ? aircraft.rb.velocity.y : 0f;
            maxBank = GroundLimitedBank(aircraft.radarAlt, maxBank, verticalSpeed);
            AircraftParameters parameters = aircraft.GetAircraftParameters();
            maxBank = Mathf.Min(maxBank, LaunchSafety.RejoinBankLimit(aircraft.radarAlt,
                airspeed, parameters != null ? parameters.takeoffSpeed : 70f));

            float bankAllowed = Mathf.Clamp(turnDemand, LevelBank, maxBank);

            // Pitch-down authority protection:
            // When flight-path pitch exceeds aim pitch (pitch deficit) or the aircraft is climbing
            // while the slot is below, AutopilotPlane.AutoAim commands 120°-180° inverted roll.
            // Clamping bankAllowed to LevelBank keeps wings level, prevents 90% elevator suppression,
            // and gives the elevator 100% downward pitch authority to bunt down immediately.
            float velH = Mathf.Sqrt(velocityDir.x * velocityDir.x + velocityDir.z * velocityDir.z);
            float pitchVel = Mathf.Atan2(velocityDir.y, Mathf.Max(1f, velH)) * Mathf.Rad2Deg;

            float aimH = Mathf.Sqrt(aimDir.x * aimDir.x + aimDir.z * aimDir.z);
            float pitchAim = Mathf.Atan2(aimDir.y, Mathf.Max(1f, aimH)) * Mathf.Rad2Deg;

            bankAllowed = FormationControlRules.PitchDownBankAuthority(
                pitchVel, pitchAim, verticalSpeed, verticalError, bankAllowed, LevelBank);

            return bankAllowed;
        }

        // ---------------------------------------------------------------- bank match

        /// <summary>
        /// True bank angle, in degrees, independent of pitch.
        ///
        /// The obvious formula — the signed angle between world up and the aircraft's up
        /// about its forward axis — is wrong, and wrong in a way that matters here: it does
        /// not project onto the plane the roll axis actually turns in, so pitch leaks
        /// straight into the answer. An aircraft pulling thirty degrees nose-up with its
        /// wings dead level reports thirty degrees of bank. Feeding that to a bank match
        /// meant every time the leader pulled, its wingmen saw a bank error that did not
        /// exist and rolled to correct it — which is what the constant left-right rocking
        /// was.
        ///
        /// Measuring the aircraft's right wing against the horizon's right, about its own
        /// forward axis, is pitch-independent. It is only degenerate pointing straight up or
        /// down, where bank has no meaning anyway.
        /// </summary>
        internal static float BankOf(Aircraft aircraft)
        {
            Vector3 forward = aircraft.transform.forward;
            Vector3 horizonRight = Vector3.Cross(Vector3.up, forward);

            if (horizonRight.sqrMagnitude < 0.0001f) return 0f;

            return Vector3.SignedAngle(horizonRight.normalized, aircraft.transform.right, forward);
        }

        /// <summary>
        /// Roll with the leader while settled, so a turning formation looks like one
        /// formation rather than several aircraft that happen to be nearby.
        ///
        /// This competes with the autopilot for the roll axis, so it is fenced: it only
        /// runs while settled, disengages past <see cref="BankMatchLimit"/> — the inversion
        /// this used to cause — and yields near the ground where terrain avoidance owns the
        /// aircraft.
        ///
        /// It is also the formation's "locked" roll axis: once a wingman is in its slot it
        /// tracks the leader's bank continuously — including a level leader, which commands
        /// wings level — rather than only when the leader is committed to a turn.
        /// </summary>
        private static void MatchLeaderBank(Aircraft aircraft, Aircraft leader,
                                            ControlInputs controls, float outOfPosition,
                                            float commandAngle, float slotClimb, FlightMemory memory)
        {
            float blend = WingTuning.BankMatchBlend;
            float myBank = BankOf(aircraft);

            float verticalSpeed = aircraft.rb != null ? aircraft.rb.velocity.y : 0f;
            if (Mathf.Abs(myBank) > BankMatchLimit ||
                !FormationSafety.AllowsBankMatch(aircraft.radarAlt, verticalSpeed, slotClimb))
            {
                ReportInterlock(aircraft, myBank);
                memory.Trim = 0f;
                return;
            }

            float leaderBank = BankOf(leader);
            AircraftParameters parameters = aircraft.GetAircraftParameters();
            float safeBank = LaunchSafety.RejoinBankLimit(aircraft.radarAlt, memory.Airspeed,
                parameters != null ? parameters.takeoffSpeed : 70f);
            if (Mathf.Abs(leaderBank) > safeBank || Mathf.Abs(myBank) > safeBank)
            { memory.Trim = 0f; return; }
            if (blend <= 0.001f || outOfPosition > 0.5f || memory.Recovery.Blend > 0.01f)
            {
                ApplyTrim(aircraft, controls, memory, 0f);
                return;
            }

            // No deadband any more. The deadband is what produced the constant small
            // left-right roll: with the leader level the match did nothing at all, which
            // left the roll axis to wander inside the autopilot's own noise band — the
            // bankAllowed floor that its vanishing cross product makes. Tracking the
            // leader's bank continuously closes that gap: a level leader now commands
            // wings level, and the bias actively holds it there.
            float error = Mathf.DeltaAngle(myBank, leaderBank);

            // Roll input is a *rate* command, not an angle. Driving it from angle error
            // alone is an undamped integrator: the aircraft rolls, sails past the leader's
            // bank, keeps rolling, and ends up inverted. The wingman's own roll rate damps
            // it, and the leader's roll rate is fed forward so a fast player roll is copied
            // rather than chased — the locked feel.
            float myRollRate = Vector3.Dot(aircraft.rb.angularVelocity, aircraft.transform.forward);
            float leaderRollRate = leader.rb != null
                ? Vector3.Dot(leader.rb.angularVelocity, leader.transform.forward)
                : 0f;

            // Bank angle and roll input run in *opposite* senses, and reconciling them is
            // the fix for wingmen rocking beside a level leader.
            //
            // BankOf measures the right wing against the horizon about the forward axis, so
            // a right bank reads negative, and a positive roll rate about that axis lifts
            // the right wing. The autopilot's roll input goes the other way: it feeds
            // GetAngleOnAxis(up, desiredUp, -velocity) into the roll PID, and that is
            // negative when a right-banked aircraft is told to level — so positive input
            // rolls right. Every term therefore has to be stated in the input's sense.
            //
            // Written in the measurement's sense, as this was, all three pushed the wrong
            // way: the proportional term rolled *away* from the leader's bank and the rate
            // term was negative damping, which is a limit cycle by construction. The flight
            // log shows precisely that — a wingman sitting within ten metres of its slot
            // behind a dead-level leader, rocking between 28 degrees of bank one way and 23
            // the other at roll rates past 45 degrees a second, with a commanded course
            // change of under one degree the whole time.
            float trim = -error * BankAngleGain
                       + myRollRate * BankRateGain
                       - leaderRollRate * BankFeedForward;
            trim = Mathf.Clamp(trim, -1f, 1f);

            // Add a bounded trim rather than blending towards a command of our own. The
            // difference matters: lerping towards an absolute command discards what the
            // autopilot asked for, so the two fight over the axis outright. A small additive
            // nudge leaves the autopilot's decision intact and biases it, which is all this
            // was ever supposed to do.
            //
            // Fade it out as the wingman drifts off station, so authority is handed back
            // before there is anything large to disagree about.
            //
            // And yield while the wingman is actively turning for a position correction:
            // then the autopilot's bank is well-defined and genuinely needed, and matching
            // the leader's bank would fight it. The match owns the axis exactly when the
            // autopilot's bank term is noise — settled flight, straight or in a steady turn —
            // which is also exactly when the sway it fixes lives.
            float authority = Mathf.Clamp01(
                blend * (1f - outOfPosition * 2f) * (1f - Mathf.Clamp01(commandAngle / 18f)));

            float bias = Mathf.Clamp(trim * authority, -MaxBankTrim, MaxBankTrim);
            ApplyTrim(aircraft, controls, memory, bias);
        }

        private static void ApplyTrim(Aircraft aircraft, ControlInputs controls, FlightMemory memory, float target)
        {
            memory.Trim = FormationRecovery.Move(memory.Trim, target, WingTuning.FormationTrimRate * memory.Dt);
            controls.roll = Mathf.Clamp(controls.roll + memory.Trim, -1f, 1f);
            aircraft.FilterInputs();
        }

        private static void ReportControl(Aircraft aircraft, ControlInputs controls, FlightMemory memory, string mode)
        {
            float minimum = aircraft.GetAircraftParameters().landingSpeed * 1.2f;
            float urgency = aircraft.autopilot.GetTerrainWarningSystem()?.urgency ?? 0f;
            Plugin.Logger.LogInfo($"[FormationControl] t={Time.timeSinceLevelLoad:F2} id={aircraft.GetInstanceID()} " +
                $"mode={mode} blend={memory.Recovery.Blend:F2} pitch={controls.pitch:F3} roll={controls.roll:F3} " +
                $"throttle={controls.throttle:F3} airspeed={memory.Airspeed:F1} margin={memory.Airspeed - minimum:F1} " +
                $"terrain={urgency:F2} braking={memory.Recovery.Braking:F2} response={memory.Recovery.ResponseSeconds:F2}");
        }
        /// <summary>
        /// Periodic station-keeping numbers, the fixed-wing equivalent of the [Rotary]
        /// line.
        ///
        /// Its absence is why the last wobble report had to be diagnosed from arithmetic:
        /// the rotary path had a diagnostic and the fixed-wing path did not. Saturation is
        /// the thing to watch — a wingman pinned at its command limit is one that cannot
        /// correct any harder no matter how far out it is.
        /// </summary>
        private static void Report(Aircraft aircraft, Aircraft leader, float distance,
                                   Aim aim, float commandAngle, float bankAllowed,
                                   float leaderClimb, ThrottleState throttle)
        {
            float correction = aim.Correction;
            float maxCorrection = aim.MaxCorrection;
            float lookAhead = aim.LookAhead;
            bool saturated = correction >= maxCorrection * 0.99f;

            // The vertical axis, reported separately from everything above it. It has to be
            // separate: `error` and `correction` are 3D magnitudes with the vertical folded
            // into them, so a wingman porpoising against its slot looked identical in the log
            // to one sitting still, and the axis had to be diagnosed from arithmetic instead
            // of from evidence. Own and leader climb rates are both here because a wingman
            // climbing at 10 m/s behind a leader climbing at 10 m/s is a formation climbing,
            // while the same number with the leader level is the wobble.
            float ownClimb = aircraft.rb != null ? aircraft.rb.velocity.y : 0f;
            Vector3 finalAim = aim.Point - aircraft.GlobalPosition();
            float aimPitch = Mathf.Atan2(finalAim.y,
                new Vector3(finalAim.x, 0f, finalAim.z).magnitude) * Mathf.Rad2Deg;
            Vector3 vel = aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero;
            float velH = new Vector3(vel.x, 0f, vel.z).magnitude;
            float ownPitch = Mathf.Atan2(ownClimb, Mathf.Max(1f, velH)) * Mathf.Rad2Deg;

            // Bank is reported against the leader, and with its rate, because a wingman
            // banked 40 degrees behind a leader banked 40 degrees is a formation turning,
            // while the same number with the leader level is a wingman rolling for no
            // reason. The first version logged only the wingman and could not tell them
            // apart.
            float rollRate = Vector3.Dot(aircraft.rb.angularVelocity, aircraft.transform.forward)
                             * Mathf.Rad2Deg;

            Plugin.Logger.LogInfo(
                $"[Formation] {aircraft.unitName} id={aircraft.GetInstanceID()} shape={WingFormation.Shape}: error {distance:F0} m, " +
                $"gap {throttle.Gap:F0} m, closing {throttle.Closing:F0} m/s, " +
                $"speed {aircraft.speed:F0} -> {throttle.DesiredSpeed:F0} m/s, thr {throttle.Throttle:F2}, " +
                $"leader accel {throttle.LeaderAccel:F1} m/s2, anticip {throttle.Anticipation:+0.00;-0.00; 0.00}, " +
                $"correction {correction:F0}/{maxCorrection:F0} m{(saturated ? " (SATURATED)" : "")}, " +
                $"baseline {lookAhead:F0} m, " +
                $"bank {BankOf(aircraft):F0} vs leader {BankOf(leader):F0} deg, " +
                $"roll rate {rollRate:F0} deg/s, " +
                $"cmd {commandAngle:F1} deg, bank allowed {bankAllowed:F0} deg, " +
                $"bank input {FormationControlRules.BankInput(bankAllowed, aircraft.radarAlt):F1} deg, " +
                $"vert err {aim.VerticalError:+0;-0; 0} m, " +
                $"vert corr {aim.VerticalCorrection:+0;-0; 0} m, " +
                $"climb {ownClimb:+0.0;-0.0; 0.0} vs leader {leader.rb.velocity.y:+0.0;-0.0; 0.0} m/s, " +
                $"slot climb {leaderClimb:+0.0;-0.0; 0.0} m/s, pitch {ownPitch:+0.0;-0.0; 0.0} -> aim {aimPitch:+0.0;-0.0; 0.0} deg, " +
                $"radar alt {aircraft.radarAlt:F0} m, {NearestPass(aircraft)}");
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

        /// <summary>
        /// Say so, once, when an interlock fires. If wallowing is ever reported again this
        /// is the difference between evidence and another round of guessing.
        /// </summary>
        private static void ReportInterlock(Aircraft aircraft, float bank)
        {
            if (loggedInterlock || !Plugin.Settings.VerboseLogging.Value) return;
            loggedInterlock = true;

            Plugin.Logger.LogInfo(
                $"[Formation] bank match disengaged for {aircraft.unitName}: " +
                $"bank {bank:F0} deg, radar alt {aircraft.radarAlt:F0} m. " +
                "Further occurrences are not logged.");
        }
    }
}
