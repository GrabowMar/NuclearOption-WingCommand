using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// A pilot state that flies a formation slot on a leader aircraft.
    ///
    /// This subclasses the game's own <see cref="PilotBaseState"/> and is installed with
    /// <c>Pilot.SwitchState</c>, exactly as the stock AI states are — no patching of the
    /// state machine is involved. It steers through <c>Autopilot.AutoAim</c>, the same
    /// primitive <see cref="AIPilotCombatModes"/> uses, and owns only throttle and
    /// destination.
    /// </summary>
    internal class FormationFlyState : PilotBaseState
    {
        private readonly WingMember member;

        private const float EngageInterval = 0.5f;

        private float lastSupportCheck;
        private float lastEngageCheck;
        private float lastFiredTime;
        private float rejoinBoostUntil;
        private float rejoinHoldUntil;
        private float lastKeepUpDistance = float.MaxValue;
        private float losingGroundSince;
        private Vector3 smoothedAvoidance;
        private float threatSpacing = 1f;
        private RotaryFormation.Mode lastRotaryMode = (RotaryFormation.Mode)(-1);
        private float lastRotaryReport;

        public FormationFlyState(WingMember member)
        {
            this.member = member;
            stateDisplayName = "Formation";
        }

        public Aircraft Leader => member.Leader;

        public override void EnterState(Pilot pilot)
        {
            base.pilot = pilot;
            aircraft = pilot.aircraft;
            controlInputs = aircraft.GetInputs();

            aircraft.SetFlightAssist(enabled: true);
            if (aircraft.gearState == LandingGear.GearState.LockedExtended)
                aircraft.SetGear(deployed: false);

            pilot.flightInfo.HasTakenOff = true;

            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"[Formation] {aircraft.unitName} entering slot {member.Slot}");
        }

        public override void LeaveState()
        {
        }

        public override void UpdateState(Pilot pilot)
        {
        }

        public override void FixedUpdateState(Pilot pilot)
        {
            Aircraft leader = Leader;

            // Leader gone, or we are no longer flyable: hand back to the stock AI.
            if (leader == null || leader.disabled || aircraft == null || aircraft.disabled)
            {
                member.ReleaseToCombat("leader lost");
                return;
            }

            if (CheckMutualSupport(leader))
                return;

            RunEngagement(leader);

            FormationShape shape = Plugin.Config2.Shape.Value;

            // Helicopters fly slower and much closer together than jets, so the same
            // spacing that reads as tight for a fighter formation looks scattered for them.
            float spacing = Plugin.Config2.SlotSpacing.Value;
            if (WingRegistry.IsRotary(aircraft))
                spacing *= Plugin.Config2.RotarySpacingScale.Value;

            spacing *= ThreatSpacingScale(leader);

            Vector3 offset = FormationSolver.SlotOffset(
                leader.transform.forward, member.Slot, shape, spacing,
                Plugin.Config2.SlotStack.Value);

            GlobalPosition slotPos = leader.GlobalPosition() + offset;

            // Separation keeps wingmen out of each other during a rejoin, and path-cut
            // avoidance keeps them out of the leader's nose.
            //
            // The radius has to scale with spacing. Left at its fixed-wing value it sat
            // wider than a rotary formation's own slots, so helicopters repelled each other
            // permanently and the formation could never settle.
            float separationRadius = Plugin.Config2.SeparationRadius.Value *
                                     (spacing / Mathf.Max(Plugin.Config2.SlotSpacing.Value, 1f));

            Vector3 avoidance =
                FormationSolver.Separation(
                    aircraft, member.Siblings,
                    separationRadius,
                    Plugin.Config2.SeparationStrength.Value) +
                FormationSolver.AvoidLeaderPath(
                    aircraft, leader,
                    Plugin.Config2.PathCutLookAhead.Value,
                    Plugin.Config2.PathCutRadius.Value,
                    Plugin.Config2.PathCutStrength.Value);

            // Both switch on and off as distances cross thresholds, so applied raw they
            // step the destination and the autopilot chases the step. Easing them in makes
            // the target something an aircraft can actually track.
            smoothedAvoidance = Vector3.Lerp(
                smoothedAvoidance, avoidance,
                1f - Mathf.Exp(-Time.fixedDeltaTime / Mathf.Max(0.05f, Plugin.Config2.AvoidanceSmoothing.Value)));

            slotPos += smoothedAvoidance;

            Vector3 toSlot = slotPos - aircraft.GlobalPosition();
            float distance = toSlot.magnitude;

            member.SlotError = distance;
            CheckAbleToKeepUp(leader, distance);

            // Rotary flight is a separate model, not a variation on this one. See
            // RotaryFormation: helicopters hold a point when the leader is slow and fly a
            // heading when it is cruising, because the two rotary control paths behave
            // nothing like the fixed-wing one.
            if (aircraft.autopilot is AutopilotPlane)
            {
                FlyFixedWing(leader, slotPos, toSlot, distance);
            }
            else
            {
                RotaryFormation.Mode mode = RotaryFormation.Fly(
                    aircraft, leader, slotPos, toSlot, distance, offset.y);

                ReportRotaryMode(mode, distance);
            }
        }

        /// <summary>
        /// Log rotary regime changes and periodic slot error.
        ///
        /// Four attempts at helicopter formation failed partly because the only evidence
        /// available was a description of how it looked. This says which control path is
        /// running and how far out the aircraft actually is, so the next diagnosis starts
        /// from data.
        /// </summary>
        private void ReportRotaryMode(RotaryFormation.Mode mode, float distance)
        {
            if (!Plugin.Config2.VerboseLogging.Value) return;

            bool changed = mode != lastRotaryMode;
            bool due = Time.timeSinceLevelLoad - lastRotaryReport > 5f;
            if (!changed && !due) return;

            lastRotaryMode = mode;
            lastRotaryReport = Time.timeSinceLevelLoad;

            Aircraft leader = Leader;
            Plugin.Logger.LogInfo(
                $"[Rotary] {aircraft.unitName} slot {member.Slot}: {mode}, " +
                $"error {distance:F0} m, own speed {aircraft.speed:F0}, " +
                $"leader {(leader != null ? leader.speed : 0f):F0} m/s, " +
                $"alt {aircraft.radarAlt:F0} m");
        }

        /// <summary>
        /// Open the formation up under threat and close it again when clear.
        ///
        /// Real formations widen when they expect to fight — a tight parade formation is
        /// easy to shoot at and leaves nobody room to manoeuvre. Eased rather than stepped,
        /// because a sudden change in spacing moves every slot at once and the autopilot
        /// would chase the jump.
        /// </summary>
        private float ThreatSpacingScale(Aircraft leader)
        {
            float target = 1f;

            if (Plugin.Config2.WidenUnderThreat.Value)
            {
                bool threatened =
                    (WingCommandManager.Instance?.Wing?.Posture ?? WingPosture.Defensive)
                        == WingPosture.Aggressive;

                if (!threatened)
                {
                    MissileWarning warning = leader.GetMissileWarningSystem();
                    threatened = warning != null && warning.IsWarning();
                }

                if (threatened) target = Plugin.Config2.ThreatSpacingScale.Value;
            }

            threatSpacing = Mathf.Lerp(
                threatSpacing <= 0f ? 1f : threatSpacing, target,
                1f - Mathf.Exp(-Time.fixedDeltaTime / 2f));

            return threatSpacing;
        }

        /// <summary>
        /// Roll with the leader once settled.
        ///
        /// AutopilotPlane derives roll from the desired flight direction and offers no way
        /// to command it, so this blends the result afterwards and re-filters. Wingmen that
        /// stay wings-level through a banked turn are one of the clearest giveaways that a
        /// formation is being simulated rather than flown.
        /// </summary>
        private void MatchLeaderBank(Aircraft leader, float distance)
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
            controlInputs.roll = Mathf.Lerp(
                controlInputs.roll, command, Plugin.Config2.BankMatchStrength.Value);

            aircraft.FilterInputs();
        }

        private void FlyFixedWing(Aircraft leader, GlobalPosition slotPos, Vector3 toSlot, float distance)
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
                member.Slot, Plugin.Config2.Shape.Value, Plugin.Config2.SlotSpacing.Value);
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
            if (Time.timeSinceLevelLoad < rejoinHoldUntil)
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
            bool holding = Time.timeSinceLevelLoad < rejoinHoldUntil;
            bool outOfPosition = distance > Plugin.Config2.CaptureDistance.Value;

            if (!holding && outOfPosition &&
                (distance > 1500f || Time.timeSinceLevelLoad < rejoinBoostUntil))
                throttle = 1f;

            controlInputs.throttle = Mathf.Clamp01(throttle);

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

            MatchLeaderBank(leader, distance);
        }

        /// <summary>
        /// Apply the wing's rules of engagement from inside the slot.
        ///
        /// Nothing here touches attitude or throttle, so a Defensive wingman can shoot
        /// without ever compromising station-keeping. Aggressive wingmen additionally look
        /// for an air target worth breaking formation for; ground targets are engaged from
        /// the slot in both postures.
        /// </summary>
        private void RunEngagement(Aircraft leader)
        {
            if (Time.timeSinceLevelLoad - lastEngageCheck < EngageInterval) return;
            lastEngageCheck = Time.timeSinceLevelLoad;

            // A weapon that passes its own checks would otherwise be fired on every tick,
            // emptying the aircraft in seconds. The stock AI leaves five seconds between
            // launches; this is the same idea, exposed so it can be tuned.
            bool mayFire = Time.timeSinceLevelLoad - lastFiredTime >= Plugin.Config2.FireInterval.Value;

            WingPosture posture = WingCommandManager.Instance?.Wing?.Posture ?? WingPosture.Defensive;

            WingWeapons.Allow allow = PostureRules.WeaponsFree(posture, aircraft);
            float range = PostureRules.EngageRange(posture);

            // An explicitly assigned target outranks whatever the wingman would pick, and
            // survives until it dies. Missile defence still takes precedence: a missile in
            // the air is more urgent than any order.
            Unit assigned = member.AssignedTarget;
            if (assigned != null && assigned.disabled)
            {
                WingComms.Say(member, WingComms.Call.Splash, assigned.unitName);
                member.ClearAssignedTarget();
                assigned = null;
            }

            bool fired;
            if (assigned != null && allow != WingWeapons.Allow.MissilesOnly)
            {
                fired = mayFire && WingWeapons.EngageSpecific(aircraft, pilot, assigned, range);
            }
            else if (allow == WingWeapons.Allow.MissilesOnly)
            {
                WingComms.Say(member, WingComms.Call.Defending);

                // Missile defence is time-critical and uses its own short interval.
                fired = Time.timeSinceLevelLoad - lastFiredTime >= 1f &&
                        WingWeapons.Engage(aircraft, pilot, allow, range);
            }
            else
            {
                fired = mayFire && WingWeapons.Engage(aircraft, pilot, allow, range);
            }

            if (fired) lastFiredTime = Time.timeSinceLevelLoad;

            // Aggressive only: hand over to the stock dogfight AI when an air threat is
            // close enough to be worth chasing. WingMember owns the leash that brings it
            // back, so the break is always temporary.
            if (posture == WingPosture.Aggressive && WingWeapons.HasAirThreatWithin(aircraft, range))
                member.BreakToEngage("air threat within " + (int)range + " m");
        }

        /// <summary>
        /// Break formation to fight when the leader is being shot at. This is the
        /// "smarter AI" behaviour: stock wingmen have no idea their leader is in trouble.
        /// </summary>
        private bool CheckMutualSupport(Aircraft leader)
        {
            if (!Plugin.Config2.MutualSupport.Value) return false;

            // Defensive means hold the slot no matter what, so breaking formation here
            // would contradict the posture the player chose. The defensive answer to a
            // missile on the leader is to shoot it down, which RunEngagement already does.
            WingPosture posture = WingCommandManager.Instance?.Wing?.Posture ?? WingPosture.Defensive;
            if (posture == WingPosture.Defensive) return false;

            if (Time.timeSinceLevelLoad - lastSupportCheck < 1f) return false;
            lastSupportCheck = Time.timeSinceLevelLoad;

            MissileWarning mw = leader.GetMissileWarningSystem();
            if (mw == null || !mw.IsWarning())
                return false;

            member.BreakToEngage("leader under missile attack");
            return true;
        }

        /// <summary>
        /// Notice when a wingman simply cannot hold the slot and stop it killing itself
        /// trying.
        ///
        /// A helicopter recruited into a jet flight is the clear case: it has nowhere near
        /// the speed, falls further behind every second, and chases flat out and nose-down
        /// until it sinks into the ground. Nothing else in the controller registers that
        /// the task is impossible — the slot is simply somewhere it can never reach.
        ///
        /// If the wingman is a long way out and losing ground for a sustained period, it
        /// reports unable and returns to base rather than pursuing to destruction.
        /// </summary>
        private void CheckAbleToKeepUp(Aircraft leader, float distance)
        {
            if (!Plugin.Config2.ReportUnableToKeepUp.Value) return;

            // Only a genuine performance gap counts as "unable". This check was written for
            // a helicopter recruited into a jet flight, but it fired on wingmen in the
            // *same airframe* as the leader that were simply a long way back and closing —
            // its own message read "max speed 100 vs leader 100" while the aircraft was
            // overtaking at 87 against 78. Being behind is not the same as being incapable,
            // and sending those wingmen home is what looked like ignoring the formation.
            float mine = aircraft.GetAircraftParameters().maxSpeed;
            float theirs = leader.GetAircraftParameters().maxSpeed;
            if (mine >= theirs * 0.9f) return;

            float threshold = Plugin.Config2.UnableDistance.Value;

            // Close enough, or the gap is not meaningfully growing: reset and carry on.
            //
            // The margin matters. Comparing bare distances meant any frame where the slot
            // shifted outward counted as losing ground, and a slot moves constantly as the
            // leader manoeuvres, so a wingman that was closing overall could still be
            // condemned by the noise.
            if (distance < threshold || distance < lastKeepUpDistance + 1f)
            {
                lastKeepUpDistance = Mathf.Min(lastKeepUpDistance, distance);
                losingGroundSince = 0f;
                return;
            }

            lastKeepUpDistance = distance;

            if (losingGroundSince <= 0f)
            {
                losingGroundSince = Time.timeSinceLevelLoad;
                return;
            }

            if (Time.timeSinceLevelLoad - losingGroundSince < Plugin.Config2.UnableSeconds.Value)
                return;

            losingGroundSince = 0f;

            Plugin.Logger.LogInfo(
                $"[Wing] {aircraft.unitName} cannot hold station " +
                $"({distance:F0} m out, max speed {mine:F0} vs leader {theirs:F0}) - returning to base");

            WingComms.Say(member, WingComms.Call.Unable);
            member.Apply(WingOrder.ReturnToBase);
        }

        /// <summary>
        /// Run the throttle wide open for a few seconds after a rejoin order. The delay
        /// staggers arrivals across the flight so they slot in one at a time.
        /// </summary>
        public void BoostRejoin(float delay = 0f)
        {
            rejoinHoldUntil = Time.timeSinceLevelLoad + delay;
            rejoinBoostUntil = rejoinHoldUntil + 8f;
        }
    }
}
