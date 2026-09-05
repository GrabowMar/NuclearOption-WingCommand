using UnityEngine;

namespace WingCommand
{
    /// <summary>Post-airborne pull-up used only when an actively controlled wingman is near terrain.</summary>
    internal sealed class TerrainAbortState : WingPilotState
    {
        private const float LookAhead = 800f;
        private const float ClimbBias = 250f;

        internal TerrainAbortState(WingMember member) : base(member)
        {
            stateDisplayName = "Pulling up";
        }

        public override void EnterState(Pilot pilot)
        {
            BindControls(pilot);
            HoverAssist.Release(aircraft);
        }

        public override void LeaveState() { }
        public override void UpdateState(Pilot pilot) { }

        public override void FixedUpdateState(Pilot pilot)
        {
            if (aircraft == null || aircraft.disabled) return;
            Vector3 forward = aircraft.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            GlobalPosition destination = aircraft.GlobalPosition() + forward * LookAhead + Vector3.up * ClimbBias;
            if (WingRegistry.IsRotary(aircraft))
            {
                aircraft.autopilot.AutoAim(destination, AutopilotMath.RotaryAgl(aircraft, aircraft.radarAlt + ClimbBias),
                    Vector3.zero, Vector3.zero, true);
                return;
            }

            if (controlInputs != null) controlInputs.throttle = 1f;
            aircraft.autopilot.AutoAim(destination, true, false, false, 2f,
                FormationControlRules.BankInput(12f, aircraft.radarAlt), false,
                AutopilotMath.CruiseHold(aircraft, aircraft.radarAlt + ClimbBias), Vector3.zero);
        }
    }
}