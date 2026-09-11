using UnityEngine;

namespace WingCommand
{
    /// <summary>Flies the explicit target attack independently of native autonomous target selection,
    /// which does not honour Pilot.SetPrimaryTarget. Runs in, fires, and returns when complete.</summary>
    internal class AttackRunState : WingPilotState
    {
        internal override bool RestartOnOrderChange => false;
        /// <summary>Run-in height above surface targets, in metres.</summary>
        private const float AttackAltitude = 900f;

        /// <summary>Lower surface-attack height for rotary aircraft.</summary>
        private const float RotaryAttackAltitude = 220f;

        /// <summary>Search radius around a destroyed Splash target; expend within the area rather than
        /// chasing survivors across the map.</summary>
        private const float SplashSweepRadius = 8000f;

        /// <summary>Timestamp of the last shot, used to enforce the shared firing interval.</summary>
        private float lastFiredTime;

        /// <summary>Last known target position for Splash follow-on searches.</summary>
        private GlobalPosition lastTargetPos;

        public AttackRunState(WingMember member) : base(member)
        {
            stateDisplayName = "attacking";
        }

        public override void EnterState(Pilot pilot)
        {
            BeginFlight(pilot);
            lastFiredTime = 0f;
            lastTargetPos = member.AssignedTarget != null
                ? member.AssignedTarget.GlobalPosition()
                : aircraft.GlobalPosition();

            if (Plugin.Settings.VerboseLogging.Value)
            {
                Unit target = member.AssignedTarget;
                Plugin.LogVerbose(
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

            // After target loss, Splash searches nearby while ordnance remains; other runs rejoin.
            if (target == null || target.disabled)
            {
                if (target != null) WingComms.Say(member, WingComms.Call.Splash, target.unitName);
                if (TryRollToNextExpendTarget()) return;
                FinishRun();
                return;
            }

            if (member.Order == WingOrder.FireForEffect &&
                !CombatFacade.Weapons.CanStillEngage(aircraft, target))
            {
                // Try nearby target classes before ending Splash; stores ineffective here may still
                // damage another contact.
                if (TryRollToNextExpendTarget()) return;
                FinishRun();
                return;
            }

            lastTargetPos = target.GlobalPosition();
            Fly(target);
            Shoot(target);
        }

        /// <summary>Rejoin after the run. Announce Winchester only when ammunition is empty; no-target
        /// completion is quiet.</summary>
        private void FinishRun()
        {
            WingComms.Say(member, member.Ammo <= 0
                ? WingComms.Call.OutOfAmmo
                : WingComms.Call.Expended);
            CompleteTask(WingOrder.Formation);
        }

        /// <summary>Retarget Splash near the last designation; return false for other orders or an empty
        /// sweep.</summary>
        private bool TryRollToNextExpendTarget()
        {
            if (member.Order != WingOrder.FireForEffect) return false;

            Unit next = CombatFacade.Weapons.NextExpendTarget(
                aircraft, lastTargetPos, SplashSweepRadius, member.AssignedTarget);
            if (next == null) return false;

            member.RetargetSplash(next);
            lastTargetPos = next.GlobalPosition();
            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.LogVerbose(
                    "[Attack] " + aircraft.unitName + " splash rolling onto " + next.unitName);
            return true;
        }

        private void Fly(Unit target)
        {
            GlobalPosition targetPos = target.GlobalPosition();
            bool rotary = WingRegistry.IsRotary(aircraft);
            float altitude = rotary ? RotaryAttackAltitude : AttackAltitude;
            float bombFloor = CombatFacade.Weapons.BombReleaseFloor(aircraft, target);
            if (bombFloor > 0f) altitude = Mathf.Max(altitude, bombFloor + 150f);

            // Aim above surface targets to avoid commanding flight into terrain.
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
            float interval = CombatFacade.Weapons.FireInterval(aircraft);
            if (Time.timeSinceLevelLoad - lastFiredTime < interval) return;

            // Reuse shared station validity checks. Explicit attacks are independent of incidental ROE;
            // Splash uses the weapon envelope alone as its range gate.
            float range = member.Order == WingOrder.FireForEffect
                ? float.MaxValue
                : CombatFacade.Roe.ExplicitOrderRange();

            bool fired = member.Order == WingOrder.FireForEffect
                ? CombatFacade.Weapons.EngageMassed(aircraft, pilot, target, range)
                : CombatFacade.Weapons.EngageSpecific(aircraft, pilot, target, range);
            if (fired) lastFiredTime = Time.timeSinceLevelLoad;
        }
    }
}
