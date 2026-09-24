namespace WingCommand
{
    /// <summary>Hands a recovering member to the game's own landing (spec M3 §4, native §B3/§B6) and back. Planes get
    /// <c>AIPilotLandingState</c>, helicopters and tiltwings <c>AIHeloLandingState</c>, created as the game does when
    /// missing. The <see cref="SwitchStateGuard"/> turns every way out of those states into ours.</summary>
    internal static class NativeLandingBridge
    {
        public static bool Begin(WingMember m)
        {
            Pilot p = m.Pilot;
            PilotBaseState landing;
            if (p.pilotType == Pilot.PilotType.Plane) landing = p.AILandingState ?? (p.AILandingState = new AIPilotLandingState());
            else if (p.pilotType == Pilot.PilotType.Helo || p.pilotType == Pilot.PilotType.Tiltwing)
                landing = p.AIHeloLandingState ?? (p.AIHeloLandingState = new AIHeloLandingState());
            else return false;
            p.SwitchState(landing);
            return ReferenceEquals(p.currentState, landing);
        }

        /// <summary>The pilot is flying one of the game's landing states.</summary>
        public static bool Landing(Pilot p) =>
            p != null && p.currentState != null &&
            (ReferenceEquals(p.currentState, p.AILandingState) || ReferenceEquals(p.currentState, p.AIHeloLandingState));

        /// <summary>Takes the member back from the game's landing (it is overdue): our state flies it again.</summary>
        public static void TakeBack(WingMember m) => m.Pilot.SwitchState(m.State);

        /// <summary>Off the runway's landing list (the game leaves it there when it hands over at low speed, native §B3),
        /// so native departures are not held by a landing that is over.</summary>
        public static void Deregister(Airbase airbase, Aircraft a)
        {
            if (airbase?.runways == null) return;
            foreach (Airbase.Runway r in airbase.runways)
                if (r != null) r.DeregisterLanding(a);
        }
    }
}
