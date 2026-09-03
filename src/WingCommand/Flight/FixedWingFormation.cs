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
        /// Elevator pitch response is direct and aerodynamic, without the roll inertia and
        /// bank-angle lag of lateral turns. Using the lateral gains on the vertical channel
        /// vastly over-damped pitch corrections, while the previous 2-metre deadband dropped
        /// proportional authority to zero and allowed the damping term alone to kick the
        /// aircraft into a limit-cycle oscillation ("porpoising" / bouncing up and down).
        ///
        /// Dedicated gains with zero deadband provide continuous, critically-damped altitude
        /// holding that settles cleanly without hunting or limit cycles.
        /// </summary>
        private const float VerticalPositionGain = 1.4f;
        private const float VerticalDriftDamping = 2.0f;

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
        /// Deceleration a wingman can rely on at idle, in m/s². Closes the loop on the
        /// approach geometry: to arrive at the slot at the leader's speed, the overspeed
        /// may never exceed √(2·a·gap) — that is how much speed the remaining distance can
        /// shed. Without this term the demand was proportional to the gap, which is
        /// exactly the profile that arrives hot and starts the catch/fall cycle.
        /// </summary>
        private const float MaxDecel = 4.5f;

        /// <summary>
        /// Bank beyond which the bank match disengages. The failure this guards against is
        /// real: driven from angle error alone the roll command was an undamped integrator
        /// and rolled wingmen inverted into the ground.
        /// </summary>
        private const float BankMatchLimit = 100f;

        /// <summary>Height below which bank match and pursuit authority yield to a climb.</summary>
        internal const float BankMatchFloor = 150f;

        /// <summary>
        /// Collapse turn authority as the aircraft nears the ground. Shared with the
        /// orbit so a deck-hold at 80 m AGL cannot grant a vertical bank either.
        /// </summary>
        internal static float GroundLimitedBank(float radarAlt, float requested)
        {
            if (radarAlt >= BankMatchFloor) return requested;
            float scale = Mathf.Clamp01(radarAlt / BankMatchFloor);
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
                               int slot, GlobalPosition slotPos, Vector3 toSlot,
                               float distance, float spacing, Rejoin rejoin,
                               LeaderState leaderState, bool report, FormationShape shape,
                               float lateralScale)
        {
            AircraftParameters p = aircraft.GetAircraftParameters();
            float leaderTurnRate = leaderState.TurnRate;
            float aggression = WingBrain.Aggression;
            float damping = WingBrain.Damping;

            // The speed to hold station on is the leader's speed *when we get there*, not
            // the one it has now. Formating on the current speed is a proportional
            // controller fed a ramp: through the whole of an acceleration the wingman sits a
            // fixed amount slow, so it drops back until the position term makes up the
            // difference and then holds that gap until the leader stops accelerating. The
            // lead is the wingman's own thrust-response time, so it arrives at the leader's
            // new speed with it rather than starting to chase it then.
            float leaderSpeed = Mathf.Max(
                leaderState.PredictedSpeed(leader.speed, WingTuning.SpeedLeadSeconds), 1f);

            Vector3 leaderVel = leader.rb.velocity;
            Vector3 drift = aircraft.rb.velocity - leaderVel;

            // How far out of position, as a fraction of the capture distance. One number
            // drives steering, bank authority and the throttle boost, so they can no longer
            // disagree about whether this wingman is settled.
            float capture = Mathf.Max(WingTuning.CaptureDistance, 1f);
            float outOfPosition = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance / capture));

            ThrottleState throttle = Throttle(aircraft, leader, controls, p, slot, toSlot, distance, capture, spacing,
                                              leaderSpeed, drift, aggression, damping, rejoin, outOfPosition,
                                              shape, lateralScale, leaderState);

            float commandAngle = Steer(aircraft, leader, slotPos, toSlot, distance,
                                       leaderVel, drift, aggression, damping, spacing,
                                       outOfPosition, leaderState.Track, leaderTurnRate,
                                       leaderState.ClimbRate, throttle, report,
                                       rejoin.Holding);

            MatchLeaderBank(aircraft, leader, controls, outOfPosition, commandAngle);
        }

        // ------------------------------------------------------------------- throttle

        private static ThrottleState Throttle(Aircraft aircraft, Aircraft leader, ControlInputs controls,
                                              AircraftParameters p, int slot, Vector3 toSlot,
                                              float distance, float capture, float spacing,
                                               float leaderSpeed, Vector3 drift,
                                               float aggression, float damping, Rejoin rejoin,
                                               float outOfPosition, FormationShape shape,
                                               float lateralScale, LeaderState leaderState)
        {
            float leaderTurnRate = leaderState.TurnRate;

            // --- Turn compensation: fly concentric arcs, not a swinging offset. ---
            // When the leader turns at heading rate w, every slot orbits the same centre, so
            // a wingman at signed lateral offset d must fly at v_leader + w*d — the outside
            // one covers more ground, the inside one less. Without this the slot sweeps
            // around the leader and wingmen get whipped through every turn.
            //
            // w is the caller's filtered heading rate. Reading it off the rigidbody meant a
            // rolling leader commanded its wingmen tens of m/s faster and slower by turns,
            // for a turn that was not happening.
            float lateral = FormationSolver.SlotLateral(
                slot, shape, spacing, lateralScale);
            float turnCompensation = Mathf.Clamp(
                leaderTurnRate * lateral, -p.maxSpeed * 0.25f, p.maxSpeed * 0.25f);

            // --- Arrival: deceleration-limited, rate-damped closure. ---
            // Two independent demands pull on the speed: the position error (gap ahead
            // means go faster) and the closing rate (already closing means ease off). The
            // gap demand is additionally capped by what the remaining distance can shed,
            // so the commanded overspeed can never exceed the one the airframe can scrub
            // off before it reaches the slot — that profile arrives at the slot at exactly
            // the leader's speed instead of arriving hot and swinging through.
            Vector3 leaderForward = leader.transform.forward;
            float gap = Vector3.Dot(toSlot, leaderForward);           // + behind the slot
            float closing = Vector3.Dot(drift, leaderForward);         // + moving forward faster than the leader

            float closure = GapGain * gap * aggression
                          - ClosingDamp * closing * damping;

            float overspeedCap = Mathf.Sqrt(2f * MaxDecel * Mathf.Max(gap, 0f));
            closure = Mathf.Clamp(closure, -MaxClosure, Mathf.Min(MaxClosure, overspeedCap));

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
            float minSafeSpeed = Mathf.Max(p.landingSpeed * 1.2f, 1f);
            float maxUsableSpeed = Mathf.Max(p.maxSpeed, minSafeSpeed);
            desiredSpeed = Mathf.Clamp(desiredSpeed, minSafeSpeed, maxUsableSpeed);

            // Feed-forward plus proportional, no integral. The feed-forward models the
            // throttle for the desired speed (the old resting point was cruise throttle,
            // the power that holds *cruise* speed — which demanded a permanent gap just to
            // earn enough power to hold station on a faster leader). The proportional term
            // covers the model's residual, a metre or two per second, so an integral is not
            // worth its memory: an integral remembers the old demand when the desired speed
            // drops, and that memory is exactly what carried a wingman through the slot.
            float speedError = desiredSpeed - aircraft.speed;
            float throttle = Mathf.Clamp01(desiredSpeed / Mathf.Max(p.maxSpeed, 1f))
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

            // Full throttle only when genuinely out of position, and never once close in.
            //
            // The rejoin boost used to run for a fixed eight seconds regardless of distance,
            // so a wingman spawned a hundred metres from its slot got the same firewalled
            // throttle as one two kilometres out. It would rocket past the leader and then
            // have to be dragged back — and jets decelerate on throttle alone very slowly,
            // so the overshoot was large.
            bool far = outOfPosition >= 1f;
            if (!rejoin.Holding && far && (distance > capture * 3f || rejoin.Boosting))
                throttle = 1f;

            controls.throttle = Mathf.Clamp01(throttle);

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
                                   ThrottleState throttle, bool report, bool holding)
        {
            Aim aim = AimFor(aircraft, leader, slotPos, toSlot, distance, leaderVel, drift,
                             aggression, damping, spacing, outOfPosition, smoothedLeaderDir,
                             leaderTurnRate, holding);

            float bankAllowed =
                BankAuthority(aircraft, leader, aim.Point, outOfPosition, holding,
                              out float commandAngle);

            if (report)
                Report(aircraft, leader, distance, aim, commandAngle, bankAllowed,
                       leaderClimb, throttle);
            aircraft.autopilot.AutoAim(
                destination: aim.Point,
                aimVelocity: true,
                ignoreCollisions: false,
                runwayAlign: false,
                effort: FullAuthority,
                bankAllowed: bankAllowed,
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

            return commandAngle;
        }

        /// <summary>The point the autopilot is told to fly at, and how it was arrived at.</summary>
        private static Aim AimFor(Aircraft aircraft, Aircraft leader, GlobalPosition slotPos,
                                  Vector3 toSlot, float distance, Vector3 leaderVel,
                                  Vector3 drift, float aggression, float damping,
                                  float spacing, float outOfPosition,
                                  Vector3 smoothedLeaderDir, float leaderTurnRate,
                                  bool holding)
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

            float lookAhead = Mathf.Max(aircraft.speed * LookAheadSeconds, MinLookAhead);

            // A staggered rejoin holds a wingman at the leader's track until its turn comes,
            // and the throttle already refuses to close. Chasing the slot from here is what
            // paired a full-bank pursuit with the hold's speed-match throttle and flew a
            // wingman knife-edge into the ground: fly straight along the leader's track, and
            // let the boost that follows the hold do the actual intercept.
            if (holding)
                return new Aim(aircraft.GlobalPosition() + baseDir * lookAhead, 0f, 0f,
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
            // Splitting them changes the *geometry* of the zone, not the loop gains:
            // PositionGain and DriftDamping are the values chosen for a damping ratio near
            // 0.8 and are used unchanged on both axes. The vertical loop simply stops being
            // switched off. It stays comfortably overdamped, because vertical errors are
            // metres while the drift that damps them is metres per second.
            float maxAngle = Mathf.Clamp(WingTuning.CommandAngle, 1f, 80f);
            float maxCorrection = lookAhead * Mathf.Tan(maxAngle * Mathf.Deg2Rad);

            Vector3 acrossFlat = new Vector3(across.x, 0f, across.z);
            Vector3 acrossDriftFlat = new Vector3(acrossDrift.x, 0f, acrossDrift.z);

            float inner = spacing * SlotZoneInner;
            float outer = spacing * SlotZoneOuter;
            float ramp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, outer, acrossFlat.magnitude));

            Vector3 flatCorrection = (acrossFlat * PositionGain * aggression * ramp)
                                     - (acrossDriftFlat * DriftDamping * damping);
            // CommandAngle is the quantity being limited, and it is limited where it is
            // produced: over a baseline this long, a correction of baseline*tan(angle) is
            // exactly that many degrees of command. One configured angle, applied honestly,
            // and at cruise it allows roughly 2.5 times the correction the old fixed 220 m
            // clamp did.
            flatCorrection = Vector3.ClampMagnitude(flatCorrection, maxCorrection);

            // The vertical channel operates as a continuous, critically-damped linear PD
            // controller with zero deadband. Without a deadband where restoring authority
            // collapses to zero, there is no boundary to trigger limit-cycle hunting, and
            // the dedicated VerticalPositionGain / VerticalDriftDamping settle the aircraft
            // smoothly onto slot altitude.
            float verticalCorrection = Mathf.Clamp(
                (across.y * VerticalPositionGain * aggression)
                - (acrossDrift.y * VerticalDriftDamping * damping),
                -maxCorrection, maxCorrection);

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
            Vector3 currentDirection = aircraft.rb.velocity.sqrMagnitude > 1f
                ? aircraft.rb.velocity.normalized
                : aircraft.transform.forward;
            if (requested.sqrMagnitude > 1f)
            {
                float allowed = Mathf.Lerp(maxAngle, MaxRejoinCommandAngle, outOfPosition);
                if (aircraft.radarAlt < BankMatchFloor)
                {
                    float scale = Mathf.Clamp01(aircraft.radarAlt / BankMatchFloor);
                    allowed = Mathf.Lerp(maxAngle * 0.25f, allowed, scale);
                }
                Vector3 safeDirection = Vector3.RotateTowards(
                    currentDirection, requested.normalized, allowed * Mathf.Deg2Rad, 0f);
                aimPoint = aircraft.GlobalPosition()
                         + safeDirection * Mathf.Max(requested.magnitude, lookAhead);
            }

            return new Aim(aimPoint, correction.magnitude, maxCorrection, lookAhead,
                           toSlot.y, verticalCorrection);
        }

        /// <summary>How much bank the autopilot may use, and the command angle it came from.</summary>
        private static float BankAuthority(Aircraft aircraft, Aircraft leader,
                                           GlobalPosition aimPoint, float outOfPosition,
                                           bool holding, out float commandAngle)
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
            float turnDemand = leaderBankMag + commandAngle * TurnDemandGain;

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

            // Near the ground, turn authority collapses. Formation used to grant
            // PursuitBank the moment a wingman was out of position, including a jet
            // that had just left the runway — 131° of commanded bank at 25 m AGL in
            // the log. Leader-bank feed-forward is included in maxBank before this
            // scale, so an inverted player cannot authorize 88° at 30 m either.
            maxBank = GroundLimitedBank(aircraft.radarAlt, maxBank);

            float bankAllowed = Mathf.Clamp(turnDemand, LevelBank, maxBank);

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
                                            float commandAngle)
        {
            float blend = WingTuning.BankMatchBlend;
            if (blend <= 0.001f || outOfPosition > 0.5f) return;

            float myBank = BankOf(aircraft);

            if (Mathf.Abs(myBank) > BankMatchLimit || aircraft.radarAlt < BankMatchFloor)
            {
                ReportInterlock(aircraft, myBank);
                return;
            }

            float leaderBank = BankOf(leader);

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

            controls.roll = Mathf.Clamp(controls.roll + bias, -1f, 1f);

            aircraft.FilterInputs();
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

            // Bank is reported against the leader, and with its rate, because a wingman
            // banked 40 degrees behind a leader banked 40 degrees is a formation turning,
            // while the same number with the leader level is a wingman rolling for no
            // reason. The first version logged only the wingman and could not tell them
            // apart.
            float rollRate = Vector3.Dot(aircraft.rb.angularVelocity, aircraft.transform.forward)
                             * Mathf.Rad2Deg;

            Plugin.Logger.LogInfo(
                $"[Formation] {aircraft.unitName}: error {distance:F0} m, " +
                $"gap {throttle.Gap:F0} m, closing {throttle.Closing:F0} m/s, " +
                $"speed {aircraft.speed:F0} -> {throttle.DesiredSpeed:F0} m/s, thr {throttle.Throttle:F2}, " +
                $"leader accel {throttle.LeaderAccel:F1} m/s2, anticip {throttle.Anticipation:+0.00;-0.00; 0.00}, " +
                $"correction {correction:F0}/{maxCorrection:F0} m{(saturated ? " (SATURATED)" : "")}, " +
                $"baseline {lookAhead:F0} m, " +
                $"bank {BankOf(aircraft):F0} vs leader {BankOf(leader):F0} deg, " +
                $"roll rate {rollRate:F0} deg/s, " +
                $"cmd {commandAngle:F1} deg, bank allowed {bankAllowed:F0} deg, " +
                $"vert err {aim.VerticalError:+0;-0; 0} m, " +
                $"vert corr {aim.VerticalCorrection:+0;-0; 0} m, " +
                $"climb {ownClimb:+0.0;-0.0; 0.0} vs leader {leaderClimb:+0.0;-0.0; 0.0} m/s, " +
                $"radar alt {aircraft.radarAlt:F0} m");
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
