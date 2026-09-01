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
    internal class AttackRunState : WingPilotState
    {
        /// <summary>Height held above a surface target while running in, in metres.</summary>
        private const float AttackAltitude = 900f;

        /// <summary>Rotary aircraft attack from much lower.</summary>
        private const float RotaryAttackAltitude = 220f;

        /// <summary>
        /// Seconds between firing attempts while expending. Short enough to read as a
        /// salvo, long enough that a station's own salvo finishes before the next attempt.
        /// </summary>
        private const float MassedFireInterval = 0.8f;

        /// <summary>Seconds between firing attempts, matching the formation path.</summary>
        private float lastFiredTime;

        /// <summary>
        /// True while flying a Splash 'Em run rather than a measured attack. The flying
        /// is identical - the difference is entirely in how hard the weapons are worked.
        /// </summary>
        private bool massed;

        public AttackRunState(WingMember member) : base(member)
        {
            stateDisplayName = "attacking";
        }

        /// <summary>Choose between a measured attack and an expending one. Call before entering.</summary>
        public void SetMassed(bool value)
        {
            massed = value;
            stateDisplayName = value ? "splash 'em" : "attacking";
        }

        public override void EnterState(Pilot pilot)
        {
            BeginFlight(pilot);
            lastFiredTime = 0f;

            if (Plugin.Config2.VerboseLogging.Value)
            {
                Unit target = member.AssignedTarget;
                Plugin.Logger.LogInfo(
                    $"[Attack] {aircraft.unitName} running in on " +
                    (target != null ? target.unitName : "(no target)") +
                    (massed ? " (splash 'em)" : ""));
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

            // An expending run ends when there is nothing left aboard that could hurt this
            // target. A measured attack does not need the check - it keeps its station in
            // reserve and the bingo/Winchester pass sends it home - but Splash 'Em is
            // meant to run itself dry, and without this it would then circle a survivor it
            // could no longer touch.
            if (massed && !WingWeapons.CanStillEngage(aircraft, target))
            {
                WingComms.Say(member, WingComms.Call.Expended);
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
            bool rotary = WingRegistry.IsRotary(aircraft);
            float altitude = rotary ? RotaryAttackAltitude : AttackAltitude;

            // Aim above a surface target rather than at it. Aiming at the ground drives the
            // autopilot into the ground, and its terrain avoidance then fights the attack
            // run all the way in.
            bool surface = target.definition == null || target.definition.typeIdentity.air <= 0.5f;
            GlobalPosition aim = surface ? targetPos + Vector3.up * altitude : targetPos;

            if (rotary)
            {
                aircraft.autopilot.AutoAim(
                    destination: aim,
                    altitudeHold: AutopilotMath.RotaryAgl(aircraft, altitude),
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
                bankAllowed: AutopilotMath.PursuitBank(),
                followTerrain: false,
                altitudeHold: AutopilotMath.CruiseHold(aircraft, altitude),
                targetVelocity: target.rb != null ? target.rb.velocity : Vector3.zero);
        }

        private void Shoot(Unit target)
        {
            float interval = massed ? MassedFireInterval : WingWeapons.FireInterval(aircraft);
            if (Time.timeSinceLevelLoad - lastFiredTime < interval) return;

            // The same weapon selection and validity checks the formation path uses, so an
            // attack run cannot dump the loadout at a target it has no business shooting.
            // Splash 'Em keeps those checks and drops only the wing-wide concurrency
            // cap and the long cooldown between launches.
            // A designated target is an explicit weapons authorization. The ROE still owns
            // incidental fire elsewhere, but cannot shorten or veto this order.
            float range = RoeRules.ExplicitOrderRange();

            bool fired = massed
                ? WingWeapons.EngageMassed(aircraft, pilot, target, range)
                : WingWeapons.EngageSpecific(aircraft, pilot, target, range);

            if (fired) lastFiredTime = Time.timeSinceLevelLoad;
        }
    }
}
