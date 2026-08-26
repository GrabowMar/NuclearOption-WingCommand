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
        private float lastKeepUpDistance = float.MaxValue;
        private float losingGroundSince;
        private Vector3 smoothedAvoidance;

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
            float spacing = Plugin.Config2.SlotSpacing.Value;

            Vector3 offset = FormationSolver.SlotOffset(
                leader.transform.forward, member.Slot, shape, spacing,
                Plugin.Config2.SlotStack.Value);

            GlobalPosition slotPos = leader.GlobalPosition() + offset;

            // Separation keeps wingmen out of each other during a rejoin, and path-cut
            // avoidance keeps them out of the leader's nose.
            Vector3 avoidance =
                FormationSolver.Separation(
                    aircraft, member.Siblings,
                    Plugin.Config2.SeparationRadius.Value,
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

            // Fixed-wing and rotary aircraft use different Autopilot overloads, and the
            // one each does not implement is an empty method on the base class. Calling
            // the wrong one produces no control input at all, so the aircraft simply
            // falls out of the sky.
            if (aircraft.autopilot is AutopilotPlane)
                FlyFixedWing(leader, slotPos, toSlot, distance);
            else
                FlyRotary(leader, slotPos);
        }

        /// <summary>
        /// Rotary and tiltwing aircraft: <c>AutopilotHelo</c> / <c>AutopilotTiltwing</c>
        /// override the five-argument overload and manage collective themselves, so
        /// throttle is deliberately left alone here.
        /// </summary>
        private void FlyRotary(Aircraft leader, GlobalPosition slotPos)
        {
            Vector3 heading = leader.transform.forward;
            heading.y = 0f;

            // For rotary aircraft altitudeHold is a height *above ground* to fly at, not a
            // target altitude: AutopilotHelo feeds it to TerrainWaypoint and then steers on
            // (radarAlt - altitudeHold). Passing anything small flies them into the ground,
            // so track the leader's own AGL and never go below the airframe's stated floor.
            AircraftParameters p = aircraft.GetAircraftParameters();
            float agl = Mathf.Max(p.minimumRadarAlt, leader.radarAlt);

            aircraft.autopilot.AutoAim(
                destination: slotPos,
                altitudeHold: Mathf.Clamp(agl, 30f, 3000f),
                aimDirection: heading.sqrMagnitude > 0.01f ? heading.normalized : Vector3.zero,
                targetVelocity: leader.rb != null ? leader.rb.velocity : Vector3.zero,
                followTerrain: true);
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

            // --- Arrival: ramp closure down inside the slowing radius. ---
            // A raw proportional term hunts around the slot; arrival converges on it.
            float alongTrack = Vector3.Dot(toSlot, leader.transform.forward);
            float slowingRadius = Mathf.Max(Plugin.Config2.SlowingRadius.Value, 1f);
            float closure = Mathf.Clamp(alongTrack, -slowingRadius, slowingRadius) / slowingRadius;
            closure *= leaderSpeed * Plugin.Config2.ClosureAuthority.Value;

            float desiredSpeed = leaderSpeed + turnCompensation + closure;
            desiredSpeed = Mathf.Clamp(desiredSpeed, p.landingSpeed * 1.2f,
                                       Mathf.Max(p.maxSpeed, leaderSpeed * 1.5f));

            float speedError = desiredSpeed - aircraft.speed;
            float throttle = p.cruiseThrottle + speedError * 0.06f;

            // A long way out, just get there.
            if (distance > 1500f || Time.timeSinceLevelLoad < rejoinBoostUntil)
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

            float threshold = Plugin.Config2.UnableDistance.Value;

            // Closing, or close enough: reset and carry on.
            if (distance < threshold || distance < lastKeepUpDistance)
            {
                lastKeepUpDistance = distance;
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

            float mine = aircraft.GetAircraftParameters().maxSpeed;
            float theirs = leader.GetAircraftParameters().maxSpeed;

            Plugin.Logger.LogInfo(
                $"[Wing] {aircraft.unitName} cannot hold station " +
                $"({distance:F0} m out, max speed {mine:F0} vs leader {theirs:F0}) - returning to base");

            WingComms.Say(member, WingComms.Call.Unable);
            member.Apply(WingOrder.ReturnToBase);
        }

        /// <summary>Run the throttle wide open for a few seconds after a rejoin order.</summary>
        public void BoostRejoin()
        {
            rejoinBoostUntil = Time.timeSinceLevelLoad + 8f;
        }
    }
}
