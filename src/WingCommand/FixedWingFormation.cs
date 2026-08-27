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
        /// baseline makes a high-gain loop that oscillates. Six seconds is about 1200 m at
        /// jet cruise.
        /// </summary>
        private const float LookAheadSeconds = 6f;

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

        /// <summary>Degrees of bank authority granted per degree of commanded turn.</summary>
        private const float TurnDemandGain = 4f;

        /// <summary>Correction gain on slot error, before <c>Aggression</c> scales it.</summary>
        private const float PositionGain = 2.5f;

        /// <summary>Damping gain on drift relative to the leader, before <c>Damping</c>.</summary>
        private const float DriftDamping = 1.6f;

        /// <summary>Damping gain on along-track closing rate, before <c>Damping</c>.</summary>
        private const float ClosureDamping = 0.4f;

        /// <summary>Along-track error, in metres, at which the speed demand saturates.</summary>
        private const float SlowingRadius = 300f;

        /// <summary>
        /// Bank beyond which the bank match disengages. The failure this guards against is
        /// real: driven from angle error alone the roll command was an undamped integrator
        /// and rolled wingmen inverted into the ground.
        /// </summary>
        private const float BankMatchLimit = 100f;

        /// <summary>Height below which the bank match yields to terrain avoidance.</summary>
        private const float BankMatchFloor = 150f;

        /// <summary>
        /// Leader bank below which the bank match does nothing. Level flight needs no help,
        /// and helping anyway is what made the roll axis chaotic.
        /// </summary>
        private const float BankMatchDeadband = 15f;

        /// <summary>Hard ceiling on the roll bias, as a fraction of full stick.</summary>
        private const float MaxBankTrim = 0.25f;

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

        public static void Fly(Aircraft aircraft, Aircraft leader, ControlInputs controls,
                               int slot, GlobalPosition slotPos, Vector3 toSlot,
                               float distance, float spacing, Rejoin rejoin, Vector3 smoothedLeaderDir,
                               bool report)
        {
            AircraftParameters p = aircraft.GetAircraftParameters();
            float leaderSpeed = Mathf.Max(leader.speed, 1f);
            float aggression = Plugin.Config2.Aggression.Value;
            float damping = Plugin.Config2.Damping.Value;

            Vector3 leaderVel = leader.rb.velocity;
            Vector3 drift = aircraft.rb.velocity - leaderVel;

            // How far out of position, as a fraction of the capture distance. One number
            // drives steering, bank authority and the throttle boost, so they can no longer
            // disagree about whether this wingman is settled.
            float capture = Mathf.Max(Plugin.Config2.CaptureDistance.Value, 1f);
            float outOfPosition = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance / capture));

            Throttle(aircraft, leader, controls, p, slot, toSlot, distance, capture,
                     leaderSpeed, drift, aggression, damping, rejoin, outOfPosition);

            Steer(aircraft, leader, slotPos, toSlot, distance,
                  leaderVel, drift, aggression, damping, spacing, outOfPosition, smoothedLeaderDir, report);

            MatchLeaderBank(aircraft, leader, controls, outOfPosition);
        }

        // ------------------------------------------------------------------- throttle

        private static void Throttle(Aircraft aircraft, Aircraft leader, ControlInputs controls,
                                     AircraftParameters p, int slot, Vector3 toSlot,
                                     float distance, float capture, float leaderSpeed, Vector3 drift,
                                     float aggression, float damping, Rejoin rejoin,
                                     float outOfPosition)
        {
            // --- Turn compensation: fly concentric arcs, not a swinging offset. ---
            // When the leader turns at yaw rate w, every slot orbits the same centre, so a
            // wingman at signed lateral offset d must fly at v_leader + w*d — the outside
            // one covers more ground, the inside one less. Without this the slot sweeps
            // around the leader and wingmen get whipped through every turn.
            float yawRate = leader.rb != null
                ? Vector3.Dot(leader.rb.angularVelocity, Vector3.up)
                : 0f;
            float lateral = FormationSolver.SlotLateral(
                slot, Plugin.Config2.Shape.Value, Plugin.Config2.SlotSpacing.Value);
            float turnCompensation = yawRate * lateral;

            // --- Arrival with rate damping. ---
            // Position error alone gives nothing to arrest closure with, so the gain has to
            // stay timid or the wingman sails past the slot. Formation technique is to pull
            // power *early* and let inertia carry you in, because throttle response lags.
            // Subtracting the closing rate does exactly that, and is what makes a higher
            // positional gain safe rather than merely twitchy.
            Vector3 leaderForward = leader.transform.forward;
            float alongTrack = Vector3.Dot(toSlot, leaderForward);
            float closingRate = Vector3.Dot(drift, leaderForward);

            float closure = Mathf.Clamp(alongTrack, -SlowingRadius, SlowingRadius) / SlowingRadius;
            closure *= leaderSpeed * aggression;
            closure -= closingRate * ClosureDamping * damping;

            // While waiting its turn in a staggered rejoin, a wingman matches the leader's
            // speed instead of closing, so it holds its place in the queue rather than
            // arriving alongside everyone else.
            if (rejoin.Holding)
                closure = Mathf.Min(closure, 0f);

            float desiredSpeed = leaderSpeed + turnCompensation + closure;
            desiredSpeed = Mathf.Clamp(desiredSpeed, p.landingSpeed * 1.2f,
                                       Mathf.Max(p.maxSpeed, leaderSpeed * 1.5f));

            // The resting throttle is the airframe's own cruise setting, which is by
            // definition the power that holds cruise. Biasing it downwards to "make room to
            // accelerate" was tried and is wrong: it is a feed-forward term, so anything
            // below it makes a wingman settle permanently slow and fall steadily behind.
            float speedError = desiredSpeed - aircraft.speed;
            float throttle = p.cruiseThrottle + speedError * Plugin.Config2.ThrottleGain.Value;

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
        }

        // -------------------------------------------------------------------- steering

        private static void Steer(Aircraft aircraft, Aircraft leader, GlobalPosition slotPos,
                                  Vector3 toSlot, float distance, Vector3 leaderVel,
                                  Vector3 drift, float aggression, float damping,
                                  float spacing, float outOfPosition, Vector3 smoothedLeaderDir, bool report)
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
            float maxAngle = Mathf.Clamp(Plugin.Config2.CommandAngle.Value, 1f, 80f);
            float maxCorrection = lookAhead * Mathf.Tan(maxAngle * Mathf.Deg2Rad);
            correction = Vector3.ClampMagnitude(correction, maxCorrection);

            GlobalPosition stationAim = aircraft.GlobalPosition() + baseDir * lookAhead + correction;

            // Beyond capture, chase the slot itself with a lead. Blended rather than
            // switched: the old controller stepped between these two aim points at the
            // capture boundary and the autopilot chased the step.
            float leadTime = Mathf.Clamp(distance / Mathf.Max(aircraft.speed, 50f), 0f, 6f);
            GlobalPosition pursuitAim = slotPos + leaderVel * leadTime;

            // GlobalPosition has no Lerp, but subtracting two of them gives a Vector3.
            GlobalPosition aimPoint = stationAim + (pursuitAim - stationAim) * outOfPosition;

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

            float commandAngle = (aimDir.sqrMagnitude > 1f && velocityDir.sqrMagnitude > 1f)
                ? Vector3.Angle(velocityDir, aimDir)
                : 0f;

            float turnDemand = Mathf.Abs(BankOf(leader)) + commandAngle * TurnDemandGain;

            float settledBank = Mathf.Clamp(turnDemand, LevelBank,
                                            Plugin.Config2.StationBankDegrees.Value);

            float bankAllowed = Mathf.Lerp(settledBank,
                                           Plugin.Config2.PursuitBankDegrees.Value,
                                           outOfPosition);

            if (report)
                Report(aircraft, leader, distance, correction.magnitude, maxCorrection, lookAhead,
                       commandAngle, bankAllowed);
            aircraft.autopilot.AutoAim(
                destination: aimPoint,
                aimVelocity: true,
                ignoreCollisions: false,
                runwayAlign: false,
                effort: FullAuthority,
                bankAllowed: bankAllowed,
                followTerrain: false,
                altitudeHold: Mathf.Clamp(leader.radarAlt, aircraft.maxRadius, 8000f),
                targetVelocity: leaderVel);
        }

        // ---------------------------------------------------------------- bank match

        /// <summary>
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
        private static float BankOf(Aircraft aircraft)
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
        /// </summary>
        private static void MatchLeaderBank(Aircraft aircraft, Aircraft leader,
                                            ControlInputs controls, float outOfPosition)
        {
            float blend = Plugin.Config2.BankMatchBlend.Value;
            if (blend <= 0.001f || outOfPosition > 0.5f) return;

            float myBank = BankOf(aircraft);

            if (Mathf.Abs(myBank) > BankMatchLimit || aircraft.radarAlt < BankMatchFloor)
            {
                ReportInterlock(aircraft, myBank);
                return;
            }

            float leaderBank = BankOf(leader);

            // Only while the leader is genuinely banking.
            //
            // This is the fix for the chaotic roll axis, and the reasoning is worth keeping.
            // In settled formation the aim point is a degree or two off the nose, so the
            // bank the autopilot wants is approximately zero — while this was asking for
            // whatever bank the leader happened to be holding. Two controllers commanding
            // the same axis towards different targets, fifty times a second, and the
            // autopilot's is a stateful loop that integrates against being overridden. The
            // formation held station to within twenty metres throughout, which is what made
            // it obvious the fault was not in the steering.
            //
            // Wings-level flight needs no help: a wingman flying the correct path already
            // banks like the leader through a turn, because that is what flying the same
            // curve means. So this now does nothing at all until the leader is actually
            // committed to a turn, which is the only time it was ever wanted.
            if (Mathf.Abs(leaderBank) < BankMatchDeadband) return;

            float error = Mathf.DeltaAngle(myBank, leaderBank);

            // Roll input is a *rate* command, not an angle. Driving it from angle error
            // alone is an undamped integrator: the aircraft rolls, sails past the leader's
            // bank, keeps rolling, and ends up inverted. The roll-rate term closes the loop.
            float rollRate = Vector3.Dot(aircraft.rb.angularVelocity, aircraft.transform.forward);
            float trim = Mathf.Clamp(error * 0.02f - rollRate * 0.5f, -1f, 1f);

            // Add a bounded trim rather than blending towards a command of our own. The
            // difference matters: lerping towards an absolute command discards what the
            // autopilot asked for, so the two fight over the axis outright. A small additive
            // nudge leaves the autopilot's decision intact and biases it, which is all this
            // was ever supposed to do.
            //
            // Fade it out as the wingman drifts off station, so authority is handed back
            // before there is anything large to disagree about.
            float authority = Mathf.Clamp01(blend * (1f - outOfPosition * 2f));
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
                                   float commandAngle, float bankAllowed)
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
                $"correction {correction:F0}/{maxCorrection:F0} m{(saturated ? " (SATURATED)" : "")}, " +
                $"baseline {lookAhead:F0} m, speed {aircraft.speed:F0} m/s, " +
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
            if (loggedInterlock || !Plugin.Config2.VerboseLogging.Value) return;
            loggedInterlock = true;

            Plugin.Logger.LogInfo(
                $"[Formation] bank match disengaged for {aircraft.unitName}: " +
                $"bank {bank:F0} deg, radar alt {aircraft.radarAlt:F0} m. " +
                "Further occurrences are not logged.");
        }
    }
}
