namespace WingCommand
{
    /// <summary>Pull up near terrain only after this airborne wingman is under mod control.</summary>
    internal sealed class TerrainAbortState : WingPilotState
    {
        internal TerrainAbortState(WingMember member) : base(member)
        {
            stateDisplayName = "Recovering flight";
        }

        public override void EnterState(Pilot pilot)
        {
            BindControls(pilot);
            HoverAssist.Release(aircraft);
        }

        public override void LeaveState() => member.DefensiveController.StopCountermeasures();
        public override void UpdateState(Pilot pilot) { }

        public override void FixedUpdateState(Pilot pilot)
        {
            if (aircraft == null || aircraft.disabled) return;
            member.DefensiveController.ServiceCountermeasures(pilot);
            AutopilotMath.RecoverFlight(aircraft, controlInputs);
        }
    }
}
