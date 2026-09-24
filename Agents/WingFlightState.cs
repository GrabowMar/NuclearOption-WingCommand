using UnityEngine;

namespace WingCommand
{
    /// <summary>The single pilot state that hosts a wingman's flight stack (spec §2.1). Entering binds the
    /// controls, turns flight assist on, raises the gear and seeds every loop from the aircraft; a helicopter also gets
    /// auto-hover off and a neutral pusher, a tiltwing keeps customAxis1 for the game's auto-tilt. Each physics tick
    /// hands the member to <see cref="WingService.StepMember"/>. It never enters native taxi or takeoff.</summary>
    internal sealed class WingFlightState : PilotBaseState
    {
        private readonly WingMember member;

        public WingFlightState(WingMember member)
        {
            this.member = member;
            stateDisplayName = "wing command";
        }

        public override void EnterState(Pilot pilot)
        {
            this.pilot = pilot;
            aircraft = pilot.aircraft;
            controlInputs = aircraft.GetInputs();
            aircraft.SetFlightAssist(true);
            // On the ground (a field launch) the gear stays down until the climb-out.
            if (!member.OnGround && aircraft.gearState != LandingGear.GearState.LockedRetracted) aircraft.SetGear(false);
            switch (member.Profile.Class)
            {
                case AirframeClass.Rotary:
                    aircraft.GetControlsFilter()?.SetAutoHover(false);
                    controlInputs.customAxis1 = RotaryController.AuxNeutral;
                    break;
                case AirframeClass.FixedWing:
                    controlInputs.customAxis1 = 1f;
                    break;
            }
            // A native state may have flown the aircraft for minutes: the sensor starts afresh (review M5a C1).
            member.Sensor.Restart();
            member.Last = member.Sensor.Read(aircraft, Time.fixedDeltaTime);
            member.Brain.Track(member.Last, EngineSticks.ToPure(ControlWriter.Read(controlInputs)), member.Profile);
        }

        public override void UpdateState(Pilot pilot)
        {
        }

        public override void FixedUpdateState(Pilot pilot) => WingService.Instance?.StepMember(member);

        public override void LeaveState()
        {
        }
    }
}
