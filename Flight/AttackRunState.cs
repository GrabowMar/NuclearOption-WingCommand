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

        /// <summary>Whether a fixed-wing attacker has overflown the target and is extending away before
        /// turning back in or rejoining.</summary>
        private bool egress;

        /// <summary>Timestamp until which egress flight geometry is maintained.</summary>
        private float egressUntil;

        /// <summary>Timestamp of the last shot, used to enforce the shared firing interval.</summary>
        private float lastFiredTime;
        private int splashTargetIndex;

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
            egress = false;
            egressUntil = 0f;
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
            // Native turrets continue firing after target assignment. An interrupted or completed run
            // must release the designation before another behaviour owns the aircraft.
            CombatFacade.Weapons.ClearTurretTargets(aircraft);
            egress = false;
            lastFiredTime = 0f;
            splashTargetIndex = 0;
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
                if (TryRollToNextExpendTarget())
                {
                    egress = false;
                    return;
                }
                FinishRun();
                return;
            }

            if (member.Order == WingOrder.FireForEffect &&
                !CombatFacade.Weapons.CanStillEngage(aircraft, target))
            {
                // Try nearby target classes before ending Splash; stores ineffective here may still
                // damage another contact.
                if (TryRollToNextExpendTarget())
                {
                    egress = false;
                    return;
                }
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
            if (member.Ammo <= 0)
                WingComms.Say(member, WingComms.Call.OutOfAmmo);
            CompleteTask(WingOrder.Formation);
        }

        /// <summary>Retarget Splash near the last designation; return false for other orders or an empty
        /// sweep.</summary>
        private bool TryRollToNextExpendTarget()
        {
            if (member.Order != WingOrder.FireForEffect) return false;

            Unit next = null;
            var selected = member.Directive.Targets;
            if (selected != null)
            {
                foreach (Unit candidate in selected)
                    if (candidate != member.AssignedTarget &&
                        CombatFacade.Weapons.CanStillEngage(aircraft, candidate))
                    {
                        next = candidate;
                        break;
                    }
            }
            else
                next = CombatFacade.Weapons.NextExpendTarget(
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

            Vector3 toTarget = targetPos - aircraft.GlobalPosition();
            float distanceSq = toTarget.sqrMagnitude;
            float forwardDot = Vector3.Dot(aircraft.transform.forward, toTarget.normalized);

            // Break-off / egress check: if we overflew the target (target is behind us) or passed inside
            // close range with a high aspect angle, commit to an egress leg rather than stalling or pulling high-G inverted.
            if (!egress)
            {
                if ((distanceSq < 800f * 800f && forwardDot < 0.2f) ||
                    (forwardDot < -0.1f && distanceSq < 2500f * 2500f))
                {
                    egress = true;
                    egressUntil = Time.timeSinceLevelLoad + 6f;
                }
            }
            else
            {
                if (Time.timeSinceLevelLoad > egressUntil || distanceSq > 3500f * 3500f)
                {
                    egress = false;
                }
            }

            if (egress)
            {
                // Fly straight-ahead climbing egress along current forward vector with terrain following active
                Vector3 fwd = aircraft.transform.forward;
                GlobalPosition egressAim = aircraft.GlobalPosition() + (fwd + Vector3.up * 0.15f) * 4000f;
                controlInputs.throttle = 1f;
                aircraft.autopilot.AutoAim(
                    destination: egressAim,
                    aimVelocity: true,
                    ignoreCollisions: false,
                    runwayAlign: false,
                    effort: 1.5f,
                    bankAllowed: AutopilotMath.PursuitBank(),
                    followTerrain: true,
                    altitudeHold: Mathf.Max(aircraft.radarAlt, altitude),
                    targetVelocity: Vector3.zero);
                return;
            }

            controlInputs.throttle = 1f;

            float holdAlt = surface ? altitude : targetPos.y;

            // Follow terrain during approach so wingmen never clip terrain. Disable terrain following
            // only when actively diving at a surface target within delivery range to avoid premature pullup.
            bool divingAttack = surface && forwardDot > 0.85f && distanceSq < 2500f * 2500f;
            bool followTerrain = !divingAttack;

            aircraft.autopilot.AutoAim(
                destination: aim,
                aimVelocity: true,
                ignoreCollisions: false,
                runwayAlign: false,
                effort: 2f,
                bankAllowed: AutopilotMath.PursuitBank(),
                followTerrain: followTerrain,
                altitudeHold: AutopilotMath.CruiseHold(aircraft, holdAlt),
                targetVelocity: target.rb != null ? target.rb.velocity : Vector3.zero);
        }

        private void Shoot(Unit target)
        {
            if (egress) return;

            float interval = member.Order == WingOrder.FireForEffect
                ? WingTuning.SplashFireInterval
                : CombatFacade.Weapons.FireInterval(aircraft);
            // A turret designation is accepted before its actual shot. Give native aiming/lock time
            // before switching targets, as with a measured attack.
            if (aircraft.weaponManager?.currentWeaponStation?.HasTurret() == true)
                interval = Mathf.Max(interval, CombatFacade.Weapons.FireInterval(aircraft));
            if (Time.timeSinceLevelLoad - lastFiredTime < interval) return;

            var selected = member.Directive.Targets;
            if (member.Order == WingOrder.FireForEffect && selected != null && selected.Count > 0)
            {
                for (int attempt = 0; attempt < selected.Count; attempt++)
                {
                    int index = (splashTargetIndex + attempt) % selected.Count;
                    if (!CombatFacade.Weapons.EngageMassed(aircraft, pilot, selected[index], float.MaxValue))
                        continue;
                    splashTargetIndex = (index + 1) % selected.Count;
                    lastFiredTime = Time.timeSinceLevelLoad;
                    break;
                }
                return;
            }

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
