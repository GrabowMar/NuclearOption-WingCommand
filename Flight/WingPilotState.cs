namespace WingCommand
{
    /// <summary>
    /// Shared pilot binding and flight setup for the wingman autopilot states.
    /// </summary>
    internal abstract class WingPilotState : PilotBaseState
    {
        /// <summary>The squadron member this state is flying. Shared by every subclass.</summary>
        protected readonly WingMember member;
        internal int OrderRevision { get; private set; }
        // A new payload normally starts a new task. Controllers that can safely
        // retarget in place opt out; future phase-based states are safe by default.
        internal virtual bool RestartOnOrderChange => true;
        internal void AcceptOrderRevision(int revision) => OrderRevision = revision;

        protected void CompleteTask(WingOrder order) =>
            member.CompleteFrom(this, WingDirective.Simple(order));
        protected void CompleteTask(WingDirective directive) => member.CompleteFrom(this, directive);

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
            AcceptOrderRevision(member.OrderRevision);
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
