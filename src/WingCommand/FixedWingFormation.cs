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
                               float distance, Rejoin rejoin)
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
                  leaderVel, drift, aggression, damping, outOfPosition);

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
                                  float outOfPosition)
        {
            // AutoAim is a pursuit controller: it rotates the aircraft's velocity toward the
            // destination and banks to chase it. Aimed at a point a few tens of metres away,
            // a small lateral drift swings the commanded direction by tens of degrees —
            // which is why wingmen wandered and rolled constantly. So the station-keeping
            // aim point is built at a distance chosen to produce exactly the command angle
            // asked for, and no more.
            Vector3 baseDir = leaderVel.sqrMagnitude > 1f
                ? leaderVel.normalized
                : leader.transform.forward;

            // Correction in metres: proportional to slot error, damped by drift rate.
            // Proportional-only overshoots and reverses, which the autopilot answers with
            // yaw — the slow left-right rocking.
            Vector3 correction = (toSlot * PositionGain * aggression)
                                 - (drift * DriftDamping * damping);

            // A deadband proportional to spacing, so it scales with the formation instead of
            // being an absolute that means something different for helicopters and jets.
            float deadband = Plugin.Config2.SlotSpacing.Value * 0.05f;
            if (distance < deadband) correction = Vector3.zero;

            // The baseline distance is derived from the correction, not configured: setting
            // L = |correction| / tan(maxAngle) makes the commanded angle exactly maxAngle
            // when the correction saturates, and gentler when it does not. One configured
            // angle replaces the two distances that used to imply it.
            float maxAngle = Mathf.Clamp(Plugin.Config2.CommandAngle.Value, 1f, 80f);
            float tan = Mathf.Tan(maxAngle * Mathf.Deg2Rad);
            float lookAhead = Mathf.Max(correction.magnitude / Mathf.Max(tan, 0.01f), 300f);

            GlobalPosition stationAim = aircraft.GlobalPosition() + baseDir * lookAhead + correction;

            // Beyond capture, chase the slot itself with a lead. Blended rather than
            // switched: the old controller stepped between these two aim points at the
            // capture boundary and the autopilot chased the step.
            float leadTime = Mathf.Clamp(distance / Mathf.Max(aircraft.speed, 50f), 0f, 6f);
            GlobalPosition pursuitAim = slotPos + leaderVel * leadTime;

            // GlobalPosition has no Lerp, but subtracting two of them gives a Vector3.
            GlobalPosition aimPoint = stationAim + (pursuitAim - stationAim) * outOfPosition;

            float bankAllowed = Mathf.Lerp(Plugin.Config2.StationBankDegrees.Value,
                                           Plugin.Config2.PursuitBankDegrees.Value,
                                           outOfPosition);

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

            // Bank angle about each aircraft's own forward axis.
            float myBank = Vector3.SignedAngle(
                Vector3.up, aircraft.transform.up, aircraft.transform.forward);

            if (Mathf.Abs(myBank) > BankMatchLimit || aircraft.radarAlt < BankMatchFloor)
            {
                ReportInterlock(aircraft, myBank);
                return;
            }

            float leaderBank = Vector3.SignedAngle(
                Vector3.up, leader.transform.up, leader.transform.forward);

            float error = Mathf.DeltaAngle(myBank, leaderBank);

            // Roll input is a *rate* command, not an angle. Driving it from angle error
            // alone is an undamped integrator: the aircraft rolls, sails past the leader's
            // bank, keeps rolling, and ends up inverted. The roll-rate term closes the loop.
            float rollRate = Vector3.Dot(aircraft.rb.angularVelocity, aircraft.transform.forward);
            float command = Mathf.Clamp(error * 0.02f - rollRate * 0.5f, -1f, 1f);

            // Blend rather than override: the autopilot is still flying the aircraft, and
            // fighting it outright makes wingmen wallow.
            //
            // Ease the blend out as the wingman drifts off station, so authority is handed
            // back before the two controllers can argue about a large correction.
            float authority = blend * (1f - outOfPosition * 2f);
            controls.roll = Mathf.Lerp(controls.roll, command, Mathf.Clamp01(authority));

            aircraft.FilterInputs();
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
