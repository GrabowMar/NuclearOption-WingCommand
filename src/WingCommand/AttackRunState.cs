using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Prosecute one assigned target: run in on it, shoot, come home.
    ///
    /// This exists because ordering an attack used to do nothing visible. The order set the
    /// member's <c>AssignedTarget</c>, which only <see cref="FormationFlyState"/> reads —
    /// so it worked while a wingman held station and was silently ignored the moment it was
    /// under an Engage order, which hands flying to the stock combat AI. That AI picks its
    /// own targets: <c>AIPilotCombatModes</c> never reads <c>Pilot.GetPrimaryTarget</c>, so
    /// the <c>SetPrimaryTarget</c> call the order made was dead code. A wingman told to hit
    /// a helipad went hunting for aircraft instead and, finding none worth chasing, circled.
    ///
    /// An explicit attack order now flies an attack, and the target is honoured wherever the
    /// wingman happens to be.
    /// </summary>
    internal class AttackRunState : PilotBaseState
    {
        /// <summary>Height held above a surface target while running in, in metres.</summary>
        private const float AttackAltitude = 900f;

        /// <summary>Rotary aircraft attack from much lower.</summary>
        private const float RotaryAttackAltitude = 220f;

        /// <summary>Seconds between firing attempts, matching the formation path.</summary>
        private float lastFiredTime;

        private readonly WingMember member;

        public AttackRunState(WingMember member)
        {
            this.member = member;
            stateDisplayName = "attacking";
        }

        public override void EnterState(Pilot pilot)
        {
            base.pilot = pilot;
            aircraft = pilot.aircraft;
            controlInputs = aircraft.GetInputs();

            aircraft.SetFlightAssist(enabled: true);
            if (aircraft.gearState != LandingGear.GearState.LockedRetracted)
                aircraft.SetGear(deployed: false);

            pilot.flightInfo.HasTakenOff = true;
            lastFiredTime = 0f;

            if (Plugin.Config2.VerboseLogging.Value)
            {
                Unit target = member.AssignedTarget;
                Plugin.Logger.LogInfo(
                    $"[Attack] {aircraft.unitName} running in on " +
                    (target != null ? target.unitName : "(no target)"));
            }
        }

        public override void LeaveState()
        {
        }

        public override void UpdateState(Pilot pilot)
        {
        }

        public override void FixedUpdateState(Pilot pilot)
        {
            if (aircraft == null || aircraft.disabled) return;

            Unit target = member.AssignedTarget;

            // Target gone, or killed by someone else: say so and go back to the wing rather
            // than orbiting an empty piece of ground.
            if (target == null || target.disabled)
            {
                if (target != null) WingComms.Say(member, WingComms.Call.Splash, target.unitName);
                member.ClearAssignedTarget();
                member.Apply(WingOrder.Formation);
                return;
            }

            Fly(target);
            Shoot(target);
        }

        private void Fly(Unit target)
        {
            GlobalPosition targetPos = target.GlobalPosition();
            bool rotary = !(aircraft.autopilot is AutopilotPlane);
            float altitude = rotary ? RotaryAttackAltitude : AttackAltitude;

            // Aim above a surface target rather than at it. Aiming at the ground drives the
            // autopilot into the ground, and its terrain avoidance then fights the attack
            // run all the way in.
            bool surface = target.definition == null || target.definition.typeIdentity.air <= 0.5f;
            GlobalPosition aim = surface ? targetPos + Vector3.up * altitude : targetPos;

            if (rotary)
            {
                AircraftParameters p = aircraft.GetAircraftParameters();
                float agl = Mathf.Clamp(Mathf.Max(p.minimumRadarAlt, altitude), 25f, 3000f);

                aircraft.autopilot.AutoAim(
                    destination: aim,
                    altitudeHold: agl,
                    aimDirection: Vector3.zero,
                    targetVelocity: target.rb != null ? target.rb.velocity : Vector3.zero,
                    followTerrain: true);
                return;
            }

            controlInputs.throttle = 1f;

            aircraft.autopilot.AutoAim(
                destination: aim,
                aimVelocity: true,
                ignoreCollisions: false,
                runwayAlign: false,
                effort: 2f,
                bankAllowed: Mathf.Min(Plugin.Config2.PursuitBankDegrees.Value,
                                       FixedWingFormation.MaxSafeBank),
                followTerrain: false,
                altitudeHold: Mathf.Clamp(altitude, aircraft.maxRadius, 8000f),
                targetVelocity: target.rb != null ? target.rb.velocity : Vector3.zero);
        }

        private void Shoot(Unit target)
        {
            if (Time.timeSinceLevelLoad - lastFiredTime < WingWeapons.FireInterval(aircraft)) return;

            // The same weapon selection and validity checks the formation path uses, so an
            // attack run cannot dump the loadout at a target it has no business shooting.
            float range = RoeRules.EngageRange(RoeRules.Current);

            if (WingWeapons.EngageSpecific(aircraft, pilot, target, range))
                lastFiredTime = Time.timeSinceLevelLoad;
        }
    }
}
