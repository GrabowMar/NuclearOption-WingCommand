using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Flies a wingman to one tactical-map point. The member owns a queue of these points;
    /// this state only handles the current leg and hands completion back to the member so
    /// Shift-click routes can advance without losing their final ROE behavior.
    /// </summary>
    internal sealed class WaypointTaskState : PilotBaseState
    {
        private const float ArrivalRadius = 140f;
        private const float FixedCruiseAltitude = 700f;
        private const float RotaryCruiseAltitude = 180f;

        private readonly WingMember member;
        private GlobalPosition targetPoint;

        public WaypointTaskState(WingMember member)
        {
            this.member = member;
            stateDisplayName = "moving to waypoint";
        }

        public void SetDestination(GlobalPosition point)
        {
            targetPoint = point;
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

            Vector3 delta = targetPoint - aircraft.GlobalPosition();
            delta.y = 0f;
            float arrival = Mathf.Max(ArrivalRadius, aircraft.speed * 1.5f);
            if (delta.sqrMagnitude <= arrival * arrival)
            {
                member.CompleteWaypoint();
                return;
            }

            if (aircraft.autopilot is AutopilotPlane)
            {
                controlInputs.throttle = 1f;
                aircraft.autopilot.AutoAim(
                    destination: targetPoint + Vector3.up * FixedCruiseAltitude,
                    aimVelocity: true,
                    ignoreCollisions: false,
                    runwayAlign: false,
                    effort: 1.8f,
                    bankAllowed: Mathf.Min(Plugin.Config2.PursuitBankDegrees.Value,
                                           FixedWingFormation.MaxSafeBank),
                    followTerrain: true,
                    altitudeHold: Mathf.Clamp(FixedCruiseAltitude, aircraft.maxRadius, 8000f),
                    targetVelocity: Vector3.zero);
                return;
            }

            AircraftParameters p = aircraft.GetAircraftParameters();
            float agl = Mathf.Clamp(Mathf.Max(p.minimumRadarAlt, RotaryCruiseAltitude), 25f, 3000f);
            aircraft.autopilot.AutoAim(
                destination: targetPoint + Vector3.up * RotaryCruiseAltitude,
                altitudeHold: agl,
                aimDirection: Vector3.zero,
                targetVelocity: Vector3.zero,
                followTerrain: true);
        }
    }
}
