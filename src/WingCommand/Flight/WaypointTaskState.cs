using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Flies a wingman to one tactical-map point. The member owns a queue of these points;
    /// this state only handles the current leg and hands completion back to the member so
    /// Shift-click routes can advance without losing their final ROE behavior.
    /// </summary>
    internal sealed class WaypointTaskState : WingPilotState
    {
        private const float ArrivalRadius = 140f;
        private const float FixedCruiseAltitude = 700f;
        private const float RotaryCruiseAltitude = 180f;

        private GlobalPosition targetPoint;

        public WaypointTaskState(WingMember member) : base(member)
        {
            stateDisplayName = "moving to waypoint";
        }

        public void SetDestination(GlobalPosition point)
        {
            targetPoint = point;
        }

        public override void EnterState(Pilot pilot)
        {
            BeginFlight(pilot);
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

            if (!WingRegistry.IsRotary(aircraft))
            {
                controlInputs.throttle = 1f;
                aircraft.autopilot.AutoAim(
                    destination: targetPoint + Vector3.up * FixedCruiseAltitude,
                    aimVelocity: true,
                    ignoreCollisions: false,
                    runwayAlign: false,
                    effort: 1.8f,
                    bankAllowed: AutopilotMath.PursuitBank(),
                    followTerrain: true,
                    altitudeHold: AutopilotMath.CruiseHold(aircraft, FixedCruiseAltitude),
                    targetVelocity: Vector3.zero);
                return;
            }

            aircraft.autopilot.AutoAim(
                destination: targetPoint + Vector3.up * RotaryCruiseAltitude,
                altitudeHold: AutopilotMath.RotaryAgl(aircraft, RotaryCruiseAltitude),
                aimDirection: Vector3.zero,
                targetVelocity: Vector3.zero,
                followTerrain: true);
        }
    }
}
