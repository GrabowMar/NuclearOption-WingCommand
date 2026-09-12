using UnityEngine;

namespace WingCommand
{
    /// <summary>Priority saturation, separate from Attack's approach, egress, and shot pacing.</summary>
    internal sealed class SplashState : WingPilotState
    {
        private readonly SplashSalvo salvo = new SplashSalvo();
        private int preparedRevision = -1;
        private float launchAltitude;

        public SplashState(WingMember member) : base(member) => stateDisplayName = "saturating";

        // Called when an airborne order is recorded, even if defence temporarily owns the aircraft.
        public void Prepare()
        {
            preparedRevision = member.OrderRevision;
            launchAltitude = member.Aircraft.radarAlt;
            salvo.Begin(member.Aircraft, member.Directive.Targets ?? new[] { member.AssignedTarget }, member.Slot);
            Plugin.LogVerbose($"[Splash] {member.Name} committed {salvo.StationCount} in-range stations");
        }

        public override void EnterState(Pilot pilot)
        {
            BeginFlight(pilot);
            if (preparedRevision != member.OrderRevision) Prepare();
            // Release immediately on receipt/resumption; completion waits for FixedUpdate to avoid
            // switching pilot states re-entrantly inside EnterState.
            salvo.Tick(aircraft, pilot, out _, out _);
        }

        public override void LeaveState()
        {
            CombatFacade.Weapons.ClearTurretTargets(aircraft);
            member.DefensiveController.StopCountermeasures();
        }
        public override void UpdateState(Pilot pilot) { }

        public override void FixedUpdateState(Pilot pilot)
        {
            if (aircraft == null || aircraft.disabled) return;
            member.DefensiveController.ServiceCountermeasures(pilot);
            if (!salvo.Tick(aircraft, pilot, out Unit target, out WeaponStation station))
            {
                Plugin.LogVerbose($"[Splash] {member.Name} complete: committed stores spent or targets unavailable");
                if (member.Ammo <= 0) WingComms.Say(member, WingComms.Call.OutOfAmmo);
                CompleteTask(WingOrder.Formation);
                return;
            }

            // Align for the committed stores at cruise power. No fixed 900 m attack dive, no
            // ingress to acquire short-range stores, and no flight-phase gate on firing.
            // Weapon target-altitude limits must never command the launcher's altitude.
            float altitude = launchAltitude;
            bool rotary = WingRegistry.IsRotary(aircraft);
            bool surface = target.definition == null || target.definition.typeIdentity.air <= 0.5f;
            GlobalPosition aim = target.GlobalPosition();
            if (surface) aim.y = aircraft.GlobalPosition().y;
            controlInputs.throttle = aircraft.GetAircraftParameters().cruiseThrottle;
            aircraft.autopilot.AutoAim(
                destination: aim,
                altitudeHold: rotary ? AutopilotMath.RotaryAgl(aircraft, altitude)
                                    : AutopilotMath.CruiseHold(aircraft, surface ? altitude : aim.y),
                aimVelocity: !rotary,
                ignoreCollisions: false,
                runwayAlign: false,
                effort: 2f,
                bankAllowed: AutopilotMath.PursuitBank(),
                followTerrain: true,
                targetVelocity: target.rb != null ? target.rb.velocity : Vector3.zero);
        }
    }
}
