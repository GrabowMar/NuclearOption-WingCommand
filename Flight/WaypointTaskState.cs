using UnityEngine;

namespace WingCommand
{
    /// <summary>Flies the current map waypoint; the member owns route advancement and terminal-order
    /// behavior.</summary>
    internal sealed class WaypointTaskState : WingPilotState
    {
        internal override bool RestartOnOrderChange => false;
        private const float ArrivalRadius = 140f;

        private float CruiseAltitude =>
            member.Order == WingOrder.MoveToPoint
                ? member.ResolvedMoveAltitude
                : (WingRegistry.IsRotary(aircraft)
                    ? WingTuning.MoveAltitudeRotary
                    : WingTuning.MoveAltitudeFixed);

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
                member.CompleteWaypoint(this);
                return;
            }

            float cruise = CruiseAltitude;
            bool moving = member.Order == WingOrder.MoveToPoint;
            float speedFrac = moving ? member.ResolvedMoveSpeed : 1f;
            Vector3 lead = Vector3.zero;
            if (delta.sqrMagnitude > 1f)
            {
                float maxSpeed = aircraft.GetAircraftParameters().maxSpeed;
                lead = delta.normalized * (maxSpeed * speedFrac);
            }

            if (!WingRegistry.IsRotary(aircraft))
            {
                controlInputs.throttle = speedFrac;
                aircraft.autopilot.AutoAim(
                    destination: targetPoint + Vector3.up * cruise,
                    aimVelocity: true,
                    ignoreCollisions: false,
                    runwayAlign: false,
                    effort: moving ? WingTuning.MoveEffort : 1.8f,
                    bankAllowed: moving
                        ? Mathf.Min(WingTuning.MoveBank, FixedWingFormation.MaxSafeBank)
                        : AutopilotMath.PursuitBank(),
                    followTerrain: !moving,
                    altitudeHold: AutopilotMath.CruiseHold(aircraft, cruise),
                    targetVelocity: lead);
                return;
            }

            aircraft.autopilot.AutoAim(
                // Non-terrain rotary AutoAim adds altitudeHold to destination.y itself.
                destination: moving ? targetPoint : targetPoint + Vector3.up * cruise,
                altitudeHold: AutopilotMath.RotaryAgl(aircraft, cruise),
                aimDirection: Vector3.zero,
                targetVelocity: lead,
                followTerrain: !moving);
        }
    }
}
