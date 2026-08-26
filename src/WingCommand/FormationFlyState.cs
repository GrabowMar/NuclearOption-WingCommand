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

        private float lastSupportCheck;
        private float rejoinBoostUntil;

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

            Vector3 offset = FormationSolver.SlotOffset(
                leader.transform.forward,
                member.Slot,
                Plugin.Config2.Shape.Value,
                Plugin.Config2.SlotSpacing.Value,
                Plugin.Config2.SlotStack.Value);

            GlobalPosition slotPos = leader.GlobalPosition() + offset;
            Vector3 toSlot = slotPos - aircraft.GlobalPosition();
            float distance = toSlot.magnitude;

            member.SlotError = distance;

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

            // --- Throttle: match the leader, biased by along-track error. ---
            Vector3 leaderFwd = leader.transform.forward;
            float alongTrack = Vector3.Dot(toSlot, leaderFwd);

            float leaderSpeed = Mathf.Max(leader.speed, 1f);
            float desiredSpeed = leaderSpeed + Mathf.Clamp(alongTrack * 0.35f, -leaderSpeed * 0.5f, leaderSpeed * 0.8f);
            desiredSpeed = Mathf.Clamp(desiredSpeed, p.landingSpeed * 1.2f, Mathf.Max(p.maxSpeed, leaderSpeed * 1.5f));

            float speedError = desiredSpeed - aircraft.speed;
            float throttle = p.cruiseThrottle + speedError * 0.06f;

            // A long way out, just get there.
            if (distance > 1500f || Time.timeSinceLevelLoad < rejoinBoostUntil)
                throttle = 1f;

            controlInputs.throttle = Mathf.Clamp01(throttle);

            // --- Steering ---
            // Close in, aim at the slot itself and match the leader's velocity so the
            // autopilot stops chasing and settles. Far out, fly a lead pursuit.
            bool closed = distance < 400f;
            float effort = closed ? 0.6f : 1f;
            float bankAllowed = closed ? 100f : 160f;

            GlobalPosition aimPoint = closed
                ? slotPos
                : slotPos + leader.rb.velocity * Mathf.Clamp(distance / Mathf.Max(aircraft.speed, 50f), 0f, 6f);

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
        /// Break formation to fight when the leader is being shot at. This is the
        /// "smarter AI" behaviour: stock wingmen have no idea their leader is in trouble.
        /// </summary>
        private bool CheckMutualSupport(Aircraft leader)
        {
            if (!Plugin.Config2.MutualSupport.Value) return false;
            if (Time.timeSinceLevelLoad - lastSupportCheck < 1f) return false;
            lastSupportCheck = Time.timeSinceLevelLoad;

            MissileWarning mw = leader.GetMissileWarningSystem();
            if (mw == null || !mw.IsWarning())
                return false;

            member.ReleaseToCombat("leader under missile attack");
            return true;
        }

        /// <summary>Run the throttle wide open for a few seconds after a rejoin order.</summary>
        public void BoostRejoin()
        {
            rejoinBoostUntil = Time.timeSinceLevelLoad + 8f;
        }
    }
}
