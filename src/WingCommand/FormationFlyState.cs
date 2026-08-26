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

            RunEngagement(leader);

            FormationShape shape = Plugin.Config2.Shape.Value;
            float spacing = Plugin.Config2.SlotSpacing.Value;

            Vector3 offset = FormationSolver.SlotOffset(
                leader.transform.forward, member.Slot, shape, spacing,
                Plugin.Config2.SlotStack.Value);

            GlobalPosition slotPos = leader.GlobalPosition() + offset;

            // Separation keeps wingmen out of each other during a rejoin, when several
            // converge on the leader from arbitrary angles.
            slotPos += FormationSolver.Separation(
                aircraft, member.Siblings,
                Plugin.Config2.SeparationRadius.Value,
                Plugin.Config2.SeparationStrength.Value);

            // And out of the leader's own path: a wingman rejoining from in front would
            // otherwise fly straight through the player to reach a slot behind them.
            slotPos += FormationSolver.AvoidLeaderPath(
                aircraft, leader,
                Plugin.Config2.PathCutLookAhead.Value,
                Plugin.Config2.PathCutRadius.Value,
                Plugin.Config2.PathCutStrength.Value);

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

            // --- Steering: offset pursuit. ---
            // Aim at where the slot will be, with the lead time scaled by how far out we
            // are (Reynolds uses T = D * c). Closing in, the prediction shrinks to zero so
            // the aircraft settles on the slot instead of continually overshooting it.
            bool closed = distance < 400f;
            float effort = closed ? 0.6f : 1f;
            float bankAllowed = closed ? 100f : 160f;

            float leadTime = Mathf.Clamp(distance / Mathf.Max(aircraft.speed, 50f), 0f, 6f);
            if (closed) leadTime *= distance / 400f;

            GlobalPosition aimPoint = slotPos + leader.rb.velocity * leadTime;

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

            if (assigned != null && allow != WingWeapons.Allow.MissilesOnly)
            {
                WingWeapons.EngageSpecific(aircraft, pilot, assigned, range);
            }
            else
            {
                if (allow == WingWeapons.Allow.MissilesOnly)
                    WingComms.Say(member, WingComms.Call.Defending);

                WingWeapons.Engage(aircraft, pilot, allow, range);
            }

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
