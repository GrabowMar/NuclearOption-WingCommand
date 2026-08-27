using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Formation flight for fixed-wing aircraft: throttle to hold fore-and-aft position,
    /// steering to hold the slot, and an optional bank match.
    ///
    /// The counterpart to <see cref="RotaryFormation"/>, and separate from it for the same
    /// reason: <c>AutopilotPlane</c> and <c>AutopilotHelo</c> override different
    /// <c>AutoAim</c> overloads and respond to completely different commands, so one
    /// shared controller could only ever suit one of them.
    ///
    /// The technique is the standard one — power holds fore-and-aft position, bank holds
    /// lateral, and closure is arrested early because throttle response lags.
    /// </summary>
    internal static class FixedWingFormation
    {
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
            float closingRate = Vector3.Dot(aircraft.rb.velocity - leader.rb.velocity, leaderForward);

            float slowingRadius = Mathf.Max(Plugin.Config2.SlowingRadius.Value, 1f);
            float closure = Mathf.Clamp(alongTrack, -slowingRadius, slowingRadius) / slowingRadius;
            closure *= leaderSpeed * Plugin.Config2.ClosureAuthority.Value;
            closure -= closingRate * Plugin.Config2.ClosureDamping.Value;

            // While waiting its turn in a staggered rejoin, a wingman matches the leader's
            // speed instead of closing, so it holds its place in the queue rather than
            // arriving alongside everyone else.
            if (rejoin.Holding)
                closure = Mathf.Min(closure, 0f);

            float desiredSpeed = leaderSpeed + turnCompensation + closure;
            desiredSpeed = Mathf.Clamp(desiredSpeed, p.landingSpeed * 1.2f,
                                       Mathf.Max(p.maxSpeed, leaderSpeed * 1.5f));

            // Bias the baseline below cruise so there is usable headroom in both
            // directions. Sitting at cruiseThrottle (typically 0.9) left roughly 0.1 of
            // range above against 0.9 below, so any demand to accelerate saturated
            // immediately and the wingman had no fine control at all while catching up.
            float baseline = p.cruiseThrottle * Plugin.Config2.ThrottleBaseline.Value;
            float speedError = desiredSpeed - aircraft.speed;
            float throttle = baseline + speedError * Plugin.Config2.ThrottleGain.Value;

            // Full throttle only when genuinely out of position, and never once inside the
            // capture radius.
            //
            // The rejoin boost used to run for a fixed eight seconds regardless of distance,
            // so a wingman spawned a hundred metres from its slot got the same firewalled
            // throttle as one two kilometres out. It would rocket past the leader and then
            // have to be dragged back, which is the "turbo and break" behaviour — and jets
            // decelerate on throttle alone very slowly, so the overshoot was large.
            bool outOfPosition = distance > Plugin.Config2.CaptureDistance.Value;

            if (!rejoin.Holding && outOfPosition && (distance > 1500f || rejoin.Boosting))
                throttle = 1f;

            controls.throttle = Mathf.Clamp01(throttle);

            // --- Steering ---
            // AutoAim is a pursuit controller: it rotates the aircraft's velocity toward
            // the destination and banks to chase it. Aimed at a point a few tens of metres
            // away, a small lateral drift swings the commanded direction by tens of
            // degrees — which is why wingmen wandered and rolled constantly.
            //
            // So chase the slot only while closing on it. Once in place, fly *parallel* to
            // the leader and aim at a distant point displaced by a bounded correction: a
            // 200 m correction over a 1200 m look-ahead is under ten degrees of command no
            // matter how far the wingman drifts, turning station-keeping into small steady
            // inputs instead of a continuous chase.
            float capture = Plugin.Config2.CaptureDistance.Value;
            float bankAllowed;
            GlobalPosition aimPoint;

            if (distance < capture)
            {
                Vector3 leaderVel = leader.rb.velocity;
                Vector3 ahead = (leaderVel.sqrMagnitude > 1f
                                    ? leaderVel.normalized
                                    : leader.transform.forward)
                                * Plugin.Config2.StationLookAhead.Value;

                // Proportional-only correction overshoots and reverses, which the autopilot
                // answers with yaw — the slow left-right rocking. Subtracting a term
                // proportional to drift rate damps it: the correction eases off while
                // already closing, instead of driving all the way to the slot and back.
                Vector3 drift = aircraft.rb.velocity - leader.rb.velocity;

                Vector3 correction = distance > Plugin.Config2.StationDeadband.Value
                    ? Vector3.ClampMagnitude(
                          toSlot * 2.5f - drift * Plugin.Config2.StationDamping.Value,
                          Plugin.Config2.StationMaxCorrection.Value)
                    : Vector3.zero;

                aimPoint = aircraft.GlobalPosition() + ahead + correction;

                // Bank authority eases in from settled to pursuit across the capture
                // radius, so nothing steps at the boundary.
                bankAllowed = Mathf.Lerp(Plugin.Config2.StationBank.Value, 140f,
                                         distance / Mathf.Max(capture, 1f));
            }
            else
            {
                float leadTime = Mathf.Clamp(distance / Mathf.Max(aircraft.speed, 50f), 0f, 6f);
                aimPoint = slotPos + leader.rb.velocity * leadTime;
                bankAllowed = 160f;
            }

            // effort is read as (effort > 1f || radarAlt < 1f) ? 1f : clamp01(airspeed /
            // cornerSpeed), so anything at or below 1 behaves identically. Varying it, as
            // this used to, achieved nothing.
            const float effort = 1f;

            aircraft.autopilot.AutoAim(
                destination: aimPoint,
                aimVelocity: true,
                ignoreCollisions: false,
                runwayAlign: false,
                effort: effort,
                bankAllowed: bankAllowed,
                followTerrain: false,
                altitudeHold: Mathf.Clamp(leader.radarAlt, aircraft.maxRadius, 8000f),
                targetVelocity: leader.rb.velocity);

            MatchLeaderBank(aircraft, leader, controls, distance);
        }

        /// <summary>
        /// Roll with the leader while settled, so a turning formation looks like one
        /// formation rather than several aircraft that happen to be nearby.
        ///
        /// Off by default: it competes with the autopilot for the same axis.
        /// </summary>
        private static void MatchLeaderBank(Aircraft aircraft, Aircraft leader,
                                            ControlInputs controls, float distance)
        {
            if (!Plugin.Config2.BankMatching.Value) return;
            if (distance > Plugin.Config2.CaptureDistance.Value) return;

            // Bank angle about each aircraft's own forward axis.
            float leaderBank = Vector3.SignedAngle(
                Vector3.up, leader.transform.up, leader.transform.forward);
            float myBank = Vector3.SignedAngle(
                Vector3.up, aircraft.transform.up, aircraft.transform.forward);

            float error = Mathf.DeltaAngle(myBank, leaderBank);

            // Roll input is a *rate* command, not an angle. Driving it from angle error
            // alone is an undamped integrator: the aircraft rolls, sails past the leader's
            // bank, keeps rolling, and ends up inverted — which is how wingmen were flying
            // themselves into the ground at full speed. The roll-rate term is what closes
            // the loop.
            float rollRate = Vector3.Dot(aircraft.rb.angularVelocity, aircraft.transform.forward);
            float command = Mathf.Clamp(error * 0.02f - rollRate * 0.5f, -1f, 1f);

            // Blend rather than override: the autopilot is still flying the aircraft, and
            // fighting it outright makes wingmen wallow.
            controls.roll = Mathf.Lerp(
                controls.roll, command, Plugin.Config2.BankMatchStrength.Value);

            aircraft.FilterInputs();
        }
    }
}
