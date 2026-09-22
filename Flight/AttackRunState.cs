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

        /// <summary>Timestamp of the last shot, used to enforce the shared firing interval.</summary>
        private float lastFiredTime;

        private bool holdFire;

        public AttackRunState(WingMember member) : base(member)
        {
            stateDisplayName = "attacking";
        }

        public override void EnterState(Pilot pilot)
        {
            BeginFlight(pilot);
            lastFiredTime = float.NegativeInfinity;
            holdFire = false;

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
            holdFire = false;
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
            float distance = toTarget.magnitude;
            Vector3 los = distance > 1f ? toTarget / distance : aircraft.transform.forward;
            float forwardDot = Vector3.Dot(aircraft.transform.forward, los);
            Vector3 fromTarget = -los;
            float aspectDot = Vector3.Dot(target.transform.forward, fromTarget);

            AircraftParameters parms = aircraft.GetAircraftParameters();
            float corner = parms != null ? parms.cornerSpeed : 150f;
            float mySpeed = aircraft.rb != null ? aircraft.rb.velocity.magnitude : aircraft.speed;
            float tgtSpeed = target.rb != null ? target.rb.velocity.magnitude : 0f;
            bool leadPursuit = PersonnelFacade.Roster.HasPerk(aircraft, PilotPerk.LeadPursuit);
            bool energyFighter = PersonnelFacade.Roster.HasPerk(aircraft, PilotPerk.EnergyFighter);

            AttackRunAdvice advice = AttackRunGeometry.Evaluate(
                distance, forwardDot, aspectDot, mySpeed, tgtSpeed, corner,
                aircraft.radarAlt, surface, leadPursuit, energyFighter);
            holdFire = advice.HoldFire;

            if (advice.Shape == AttackRunShape.Overshoot)
            {
                Vector3 fwd = aircraft.transform.forward;
                GlobalPosition egressAim = aircraft.GlobalPosition() + (fwd + Vector3.up * 0.15f) * 4000f;
                controlInputs.throttle = advice.Throttle;
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

            Vector3 right = Vector3.Cross(Vector3.up, los);
            if (right.sqrMagnitude < 0.01f) right = aircraft.transform.right;
            right.Normalize();
            GlobalPosition dest = aim
                + los * advice.Along
                + right * advice.Right
                + Vector3.up * advice.Up;

            controlInputs.throttle = advice.Throttle;
            float holdAlt = surface ? altitude + advice.Up : targetPos.y + advice.Up;
            Vector3 targetVel = target.rb != null ? target.rb.velocity : Vector3.zero;
            if (leadPursuit) targetVel *= 1.25f;

            aircraft.autopilot.AutoAim(
                destination: dest,
                aimVelocity: true,
                ignoreCollisions: false,
                runwayAlign: false,
                effort: PilotPerks.DogfightEffort(leadPursuit),
                bankAllowed: AutopilotMath.PursuitBank(),
                followTerrain: advice.FollowTerrain,
                altitudeHold: AutopilotMath.CruiseHold(aircraft, holdAlt),
                targetVelocity: targetVel);
        }

        private void Shoot(Unit target)
        {
            if (holdFire) return;
            if (Time.timeSinceLevelLoad - lastFiredTime < CombatFacade.Weapons.FireInterval(aircraft)) return;
            if (CombatFacade.Weapons.EngageSpecific(aircraft, pilot, target,
                CombatFacade.Doctrine.ExplicitOrderRange())) lastFiredTime = Time.timeSinceLevelLoad;
        }
    }
}
