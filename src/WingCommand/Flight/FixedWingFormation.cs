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
        /// aircraft's velocity is rotated towards a point this far ahead, so a short
        /// baseline makes a high-gain loop that oscillates. The previous value of six
        /// seconds (~1200 m at cruise) read as sluggish — wingmen sat a long way off the
        /// slot before a lateral error grew into a visible correction. 4.5 seconds still
        /// sits above the 800 m floor at cruise, so it stays stable while responding a
        /// good deal more crisply to a manoeuvre.
        /// </summary>
        private const float LookAheadSeconds = 4.5f;

        /// <summary>Shortest baseline, for aircraft slow enough that seconds alone is not enough.</summary>
        private const float MinLookAhead = 800f;

        /// <summary>Slot radius, as a fraction of spacing, inside which position is not chased at all.</summary>
        private const float SlotZoneInner = 0.12f;

        /// <summary>Radius at which position correction reaches full authority.</summary>
        private const float SlotZoneOuter = 0.5f;

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
        private const float TurnLeadSeconds = 0.6f;

        /// <summary>
        /// Correction gain on slot error, before <c>Aggression</c> scales it.
        ///
        /// The lateral loop is a damped oscillator with ωₙ² = v·P/(τ·L) and
        /// ζ = D/(2√(P·τ·L/v)). The old gains (P = 2.5, D = 1.6) gave ζ ≈ 0.15 — an
        /// almost undamped pendulum, which is exactly what "sways left and right" is.
        /// P is lowered and D raised so ζ sits near 0.8 across the speed range.
        /// </summary>
        private const float PositionGain = 1.0f;

        /// <summary>
        /// Damping gain on drift relative to the leader, before <c>Damping</c>. Chosen with
        /// <see cref="PositionGain"/> for a damping ratio near 0.8: most of the correction
        /// authority now goes to arresting the drift the position error created, which is
        /// what stops the wingman from swinging through the slot on every correction.
        /// </summary>
        private const float DriftDamping = 5.5f;

        /// <summary>
        /// Damping on the along-track closing rate, in m/s of speed demand per m/s of
        /// closing rate. The throttle loop had the same disease as the lateral loop:
        /// with 0.4 the damping ratio was around 0.15, so the wingman swung through the
        /// slot like a pendulum — speed up, catch the leader, cut power, fall behind,
        /// repeat. Near 3 the loop is overdamped and the cycle is gone.
        /// </summary>
        private const float ClosingDamp = 3.0f;

        /// <summary>Speed demand per metre of along-track gap, in (m/s)/m.</summary>
        private const float GapGain = 0.35f;

        /// <summary>Hard ceiling on the closing speed demand, in m/s.</summary>
        private const float MaxClosure = 60f;

        /// <summary>
        /// Deceleration a wingman can rely on at idle, in m/s². Closes the loop on the
        /// approach geometry: to arrive at the slot at the leader's speed, the overspeed
        /// may never exceed √(2·a·gap) — that is how much speed the remaining distance can
        /// shed. Without this term the demand was proportional to the gap, which is
        /// exactly the profile that arrives hot and starts the catch/fall cycle.
        /// </summary>
        private const float MaxDecel = 3f;

        /// <summary>
        /// Bank beyond which the bank match disengages. The failure this guards against is
        /// real: driven from angle error alone the roll command was an undamped integrator
        /// and rolled wingmen inverted into the ground.
        /// </summary>
        private const float BankMatchLimit = 100f;

        /// <summary>Height below which the bank match yields to terrain avoidance.</summary>
        private const float BankMatchFloor = 150f;

        /// <summary>Roll command per degree of bank-angle error. Full stick around twenty degrees out.</summary>
        private const float BankAngleGain = 0.05f;

        /// <summary>Damping on the wingman's own roll rate, in stick fraction per rad/s.</summary>
        private const float BankRateGain = 0.5f;

        /// <summary>Leader roll rate fed forward, in stick fraction per rad/s, so a fast player roll is copied not chased.</summary>
        private const float BankFeedForward = 0.35f;

        /// <summary>Hard ceiling on the roll bias, as a fraction of full stick.</summary>
        private const float MaxBankTrim = 0.4f;

        /// <summary>Seconds of leader vertical speed fed into the altitude hold, matching the slot prediction.</summary>
        private const float AltitudeLeadSeconds = 1f;

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

            public ThrottleState(float gap, float closing, float desiredSpeed, float throttle)
            {
                Gap = gap;
                Closing = closing;
                DesiredSpeed = desiredSpeed;
                Throttle = throttle;
            }
        }

        /// <param name="leaderTurnRate">
        /// The leader's filtered heading rate in rad/s, positive to the right, supplied by
        /// <see cref="FormationFlyState"/>. It is not read from the rigidbody here: the
        /// world-y component of the angular velocity picks up roll rate at any nose-up
        /// attitude, and that leak was the formation's left-right sway.
        /// </param>
        public static void Fly(Aircraft aircraft, Aircraft leader, ControlInputs controls,
                               int slot, GlobalPosition slotPos, Vector3 toSlot,
                               float distance, float spacing, Rejoin rejoin, Vector3 smoothedLeaderDir,
                               float leaderTurnRate, bool report, FormationShape shape,
                               float lateralScale)
        {
            AircraftParameters p = aircraft.GetAircraftParameters();
            float leaderSpeed = Mathf.Max(leader.speed, 1f);
            float aggression = WingBrain.Aggression;
            float damping = WingBrain.Damping;

            Vector3 leaderVel = leader.rb.velocity;
            Vector3 drift = aircraft.rb.velocity - leaderVel;

            // How far out of position, as a fraction of the capture distance. One number
            // drives steering, bank authority and the throttle boost, so they can no longer
            // disagree about whether this wingman is settled.
            float capture = Mathf.Max(WingTuning.CaptureDistance, 1f);
            float outOfPosition = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance / capture));

            ThrottleState throttle = Throttle(aircraft, leader, controls, p, slot, toSlot, distance, capture, spacing,
                                              leaderSpeed, drift, aggression, damping, rejoin, outOfPosition,
                                              shape, lateralScale, leaderTurnRate);

            float commandAngle = Steer(aircraft, leader, slotPos, toSlot, distance,
                                       leaderVel, drift, aggression, damping, spacing,
                                       outOfPosition, smoothedLeaderDir, leaderTurnRate,
                                       throttle, report);

            MatchLeaderBank(aircraft, leader, controls, outOfPosition, commandAngle);
        }

        // ------------------------------------------------------------------- throttle

        private static ThrottleState Throttle(Aircraft aircraft, Aircraft leader, ControlInputs controls,
                                              AircraftParameters p, int slot, Vector3 toSlot,
                                              float distance, float capture, float spacing,
                                               float leaderSpeed, Vector3 drift,
                                               float aggression, float damping, Rejoin rejoin,
                                               float outOfPosition, FormationShape shape,
                                               float lateralScale, float leaderTurnRate)
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

            // Player acceleration is the feed-forward term the speed loop cannot see.
            // When the leader selects military/max power, its speed has not risen yet, so
            // a controller driven only by speed waits until a gap already exists. Match the
            // leader's power while behind, and use full power once a max-power leader has
            // opened a meaningful gap. Closing-rate gating keeps this from carrying the
            // wingman through the slot after it has already caught up.
            ControlInputs leaderInputs = leader.GetInputs();
            float leaderThrottle = leaderInputs != null ? leaderInputs.throttle : 0f;
            if (gap > 0f && closing < MaxClosure * 0.25f)
            {
                throttle = Mathf.Max(throttle, leaderThrottle);

                float maxPowerGap = Mathf.Max(15f, spacing * 0.15f);
                if (leaderThrottle >= 0.95f && gap >= maxPowerGap)
                    throttle = 1f;
            }

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

            return new ThrottleState(gap, closing, desiredSpeed, controls.throttle);
        }

        // -------------------------------------------------------------------- steering

        /// <summary>Where the aim point ended up, plus the figures the flight log reports.</summary>
        private readonly struct Aim
        {
            public readonly GlobalPosition Point;
            public readonly float Correction;
            public readonly float MaxCorrection;
            public readonly float LookAhead;

            public Aim(GlobalPosition point, float correction, float maxCorrection,
                       float lookAhead)
            {
                Point = point;
                Correction = correction;
                MaxCorrection = maxCorrection;
                LookAhead = lookAhead;
            }
        }

        private static float Steer(Aircraft aircraft, Aircraft leader, GlobalPosition slotPos,
                                   Vector3 toSlot, float distance, Vector3 leaderVel,
                                   Vector3 drift, float aggression, float damping,
                                   float spacing, float outOfPosition, Vector3 smoothedLeaderDir,
                                   float leaderTurnRate, ThrottleState throttle, bool report)
        {
            Aim aim = AimFor(aircraft, leader, slotPos, toSlot, distance, leaderVel, drift,
                             aggression, damping, spacing, outOfPosition, smoothedLeaderDir,
                             leaderTurnRate);

            float bankAllowed =
                BankAuthority(aircraft, leader, aim.Point, outOfPosition, out float commandAngle);

            if (report)
                Report(aircraft, leader, distance, aim.Correction, aim.MaxCorrection,
                       aim.LookAhead, commandAngle, bankAllowed, throttle);
            aircraft.autopilot.AutoAim(
                destination: aim.Point,
                aimVelocity: true,
                ignoreCollisions: false,
                runwayAlign: false,
                effort: FullAuthority,
                bankAllowed: bankAllowed,
                followTerrain: false,
                // Lead the leader's climb and dive the same way the slot position is led, so
                // a settled wingman follows the player's vertical motion instead of chasing
                // the altitude it already left behind.
                altitudeHold: Mathf.Clamp(
                    leader.radarAlt + leaderVel.y * AltitudeLeadSeconds,
                    aircraft.maxRadius, 8000f),
                targetVelocity: leaderVel);

            return commandAngle;
        }

        /// <summary>The point the autopilot is told to fly at, and how it was arrived at.</summary>
        private static Aim AimFor(Aircraft aircraft, Aircraft leader, GlobalPosition slotPos,
                                  Vector3 toSlot, float distance, Vector3 leaderVel,
                                  Vector3 drift, float aggression, float damping,
                                  float spacing, float outOfPosition,
                                  Vector3 smoothedLeaderDir, float leaderTurnRate)
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
            float inner = spacing * SlotZoneInner;
            float outer = spacing * SlotZoneOuter;
            float ramp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, outer, across.magnitude));

            Vector3 correction = (across * PositionGain * aggression * ramp)
                                 - (acrossDrift * DriftDamping * damping);
            // CommandAngle is the quantity being limited, and it is limited where it is
            // produced: over a baseline this long, a correction of baseline*tan(angle) is
            // exactly that many degrees of command. One configured angle, applied honestly,
            // and at cruise it allows roughly 2.5 times the correction the old fixed 220 m
            // clamp did.
            float maxAngle = Mathf.Clamp(WingTuning.CommandAngle, 1f, 80f);
            float maxCorrection = lookAhead * Mathf.Tan(maxAngle * Mathf.Deg2Rad);
            correction = Vector3.ClampMagnitude(correction, maxCorrection);

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
                Vector3 safeDirection = Vector3.RotateTowards(
                    currentDirection, requested.normalized, allowed * Mathf.Deg2Rad, 0f);
                aimPoint = aircraft.GlobalPosition()
                         + safeDirection * Mathf.Max(requested.magnitude, lookAhead);
            }

            return new Aim(aimPoint, correction.magnitude, maxCorrection, lookAhead);
        }

        /// <summary>How much bank the autopilot may use, and the command angle it came from.</summary>
        private static float BankAuthority(Aircraft aircraft, Aircraft leader,
                                           GlobalPosition aimPoint, float outOfPosition,
                                           out float commandAngle)
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
            float maxBank = Mathf.Lerp(WingTuning.StationBank,
                                       WingTuning.PursuitBank,
                                       outOfPosition);

            // The settled ceiling exists to stop the roll axis going chaotic in level
            // flight, but it must never clip a genuine turn: a leader banked hard needs a
            // wingman banked hard, whatever its slot state. Without this a wingman swung
            // wide on a fast manoeuvre, dropped behind, and then spent the whole chase
            // catching back up — the exact "they spend a lot of time chasing me" symptom.
            maxBank = Mathf.Max(maxBank, leaderBankMag * BankFollowScale + LevelBank);
            maxBank = Mathf.Min(maxBank, MaxSafeBank);

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
                blend * (1f - outOfPosition * 2f) * (1f - Mathf.Clamp01(commandAngle / 12f)));

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
                                   float correction, float maxCorrection, float lookAhead,
                                   float commandAngle, float bankAllowed, ThrottleState throttle)
        {
            bool saturated = correction >= maxCorrection * 0.99f;

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
                $"correction {correction:F0}/{maxCorrection:F0} m{(saturated ? " (SATURATED)" : "")}, " +
                $"baseline {lookAhead:F0} m, " +
                $"bank {BankOf(aircraft):F0} vs leader {BankOf(leader):F0} deg, " +
                $"roll rate {rollRate:F0} deg/s, " +
                $"cmd {commandAngle:F1} deg, bank allowed {bankAllowed:F0} deg");
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
