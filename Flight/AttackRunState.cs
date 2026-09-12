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

        /// <summary>Whether a fixed-wing attacker has overflown the target and is extending away before
        /// turning back in or rejoining.</summary>
        private bool egress;

        /// <summary>Timestamp until which egress flight geometry is maintained.</summary>
        private float egressUntil;

        /// <summary>Timestamp of the last shot, used to enforce the shared firing interval.</summary>
        private float lastFiredTime;

        public AttackRunState(WingMember member) : base(member)
        {
            stateDisplayName = "attacking";
        }

        public override void EnterState(Pilot pilot)
        {
            BeginFlight(pilot);
            lastFiredTime = float.NegativeInfinity;
            egress = false;
            egressUntil = 0f;

            if (Plugin.Settings.VerboseLogging.Value)
            {
                Unit target = member.AssignedTarget;
                Plugin.LogVerbose(
                    $"[Attack] {aircraft.unitName} running in on " +
                    (target != null ? target.unitName : "(no target)"));
                if (target != null && aircraft.weaponStations != null)
                {
                    Vector3 offset = target.GlobalPosition() - aircraft.GlobalPosition();
                    foreach (WeaponStation station in aircraft.weaponStations)
                    {
                        if (station == null || station.Cargo || station.WeaponInfo == null) continue;
                        TargetRequirements req = station.WeaponInfo.targetRequirements;
                        Plugin.LogVerbose($"[AttackEnvelope] {aircraft.unitName} id={aircraft.persistentID} " +
                            $"weapon={station.WeaponInfo.shortName} ammo={station.Ammo} ready={station.Ready()} " +
                            $"safety={station.SafetyIsOn(aircraft)} salvo={station.SalvoInProgress} " +
                            $"distance={offset.magnitude:F0} range={req.minRange:F0}..{req.maxRange:F0} " +
                            $"launcherAgl={aircraft.radarAlt:F0} targetAgl={target.radarAlt:F0} targetAltitude={req.minAltitude:F0}..{req.maxAltitude:F0} " +
                            $"angle={Vector3.Angle(aircraft.transform.forward, offset):F1} limit={req.minAlignment:F1}");
                    }
                }
            }
        }

        public override void LeaveState()
        {
            // Native turrets continue firing after target assignment. An interrupted or completed run
            // must release the designation before another behaviour owns the aircraft.
            CombatFacade.Weapons.ClearTurretTargets(aircraft);
            egress = false;
            lastFiredTime = float.NegativeInfinity;
        }

        public override void UpdateState(Pilot pilot)
        {
        }

        public override void FixedUpdateState(Pilot pilot)
        {
            if (aircraft == null || aircraft.disabled) return;

            Unit target = member.AssignedTarget;

            if (target == null || target.disabled)
            {
                if (target != null) WingComms.Say(member, WingComms.Call.Splash, target.unitName);
                CompleteTask(WingOrder.Formation);
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
            if (Time.timeSinceLevelLoad - lastFiredTime < CombatFacade.Weapons.FireInterval(aircraft)) return;
            if (CombatFacade.Weapons.EngageSpecific(aircraft, pilot, target,
                CombatFacade.Roe.ExplicitOrderRange())) lastFiredTime = Time.timeSinceLevelLoad;
        }
    }
}
