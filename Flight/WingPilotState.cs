namespace WingCommand
{
 /// <summary>Shared pilot binding and flight configuration for wing states.</summary>
    internal abstract class WingPilotState : PilotBaseState
    {
     /// <summary>Member controlled by this state.</summary>
        protected readonly WingMember member;
        internal int OrderRevision { get; private set; }
        // Restart on payload changes unless a controller explicitly supports safe in-place retargeting.
        internal virtual bool RestartOnOrderChange => true;
        internal void AcceptOrderRevision(int revision) => OrderRevision = revision;

        protected void CompleteTask(WingOrder order) =>
            member.CompleteFrom(this, WingDirective.Simple(order));
        protected void CompleteTask(WingDirective directive) => member.CompleteFrom(this, directive);

        protected WingPilotState(WingMember member)
        {
            this.member = member;
        }

     /// <summary>Bind pilot controls without changing gear or hover configuration.</summary>
        protected void BindControls(Pilot pilot)
        {
            base.pilot = pilot;
            AcceptOrderRevision(member.OrderRevision);
            aircraft = pilot.aircraft;
            controlInputs = aircraft.GetInputs();
            aircraft.SetFlightAssist(enabled: true);
        }

     /// <summary>Bind controls, optionally release hover, and retract aircraft gear.</summary> <param
     /// name="releaseHover">False to retain hover, such as during cargo descent.</param>
        protected void BeginFlight(Pilot pilot, bool releaseHover = true)
        {
            BindControls(pilot);

            // Restore forward flight before asking a hovering airframe for cruise speed.
            if (releaseHover) HoverAssist.Release(aircraft);

            // Retract any non-retracted aircraft gear, including uninitialised spawns. Exclude surface
            // units whose mods may repurpose gear state.
            if (aircraft.autopilot != null &&
                aircraft.gearState != LandingGear.GearState.LockedRetracted)
                aircraft.SetGear(deployed: false);

        }
    }
}
