namespace WingCommand
{
    /// <summary>
    /// Base for every pilot state this mod installs on a wingman.
    ///
    /// It exists only to remove duplication: each state was carrying its own
    /// <see cref="WingMember"/> field and repeating the same six lines of flight setup at
    /// the top of <c>EnterState</c>. That block had already drifted - different comments,
    /// slightly different ordering - so folding it here also stops it drifting further.
    /// </summary>
    internal abstract class WingPilotState : PilotBaseState
    {
        /// <summary>The squadron member this state is flying. Shared by every subclass.</summary>
        protected readonly WingMember member;

        protected WingPilotState(WingMember member)
        {
            this.member = member;
        }

        /// <summary>
        /// Bind the state to its pilot and take the controls. The minimum every state does;
        /// used directly only by states that then configure the gear themselves.
        /// </summary>
        protected void BindControls(Pilot pilot)
        {
            base.pilot = pilot;
            aircraft = pilot.aircraft;
            controlInputs = aircraft.GetInputs();
            aircraft.SetFlightAssist(enabled: true);
        }

        /// <summary>
        /// The standard "we are flying now" setup: bind the controls, drop any hover
        /// configuration a previous state left behind and retract the gear if it is down.
        /// </summary>
        /// <param name="releaseHover">
        /// False for a state that needs the hover regime kept (a cargo let-down).
        /// </param>
        protected void BeginFlight(Pilot pilot, bool releaseHover = true)
        {
            BindControls(pilot);

            // A rotary or thrust-vectoring wingman arriving from a hover cannot make cruise
            // speed until its nozzles/rotor are back to forward flight.
            if (releaseHover) HoverAssist.Release(aircraft);

            // Retract the gear whenever it is not already up. A freshly spawned helicopter
            // can still be Uninitialized here, which is what used to leave it flying with
            // the gear hanging out.
            // A ship or a ground vehicle has no gear to retract, and asking for it moves a
            // state the vehicle's own mod may be using for something else entirely.
            if (aircraft.autopilot != null &&
                aircraft.gearState != LandingGear.GearState.LockedRetracted)
                aircraft.SetGear(deployed: false);

        }
    }
}
