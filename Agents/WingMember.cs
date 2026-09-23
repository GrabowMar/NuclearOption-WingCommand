namespace WingCommand
{
    /// <summary>One wingman in the engine: its aircraft and pilot, its Pure brain (which lives as long as the
    /// member), its sensor and profile, and the pilot state that flies it.</summary>
    internal sealed class WingMember
    {
        public readonly Aircraft Aircraft;
        public readonly Pilot Pilot;
        public readonly FormationPilot Brain;
        public readonly AircraftSensor Sensor = new AircraftSensor();
        public readonly AirframeProfile Profile;
        public readonly FaultGuard Faults = new FaultGuard();
        public WingFlightState State;
        /// <summary>Stable for the member's life in the wing (slots renumber when a member ahead leaves).</summary>
        public int Id;
        public AircraftState Last;
        public float NearFloorY = float.NaN;
        public float NoFbwSeconds;
        public bool Released;

        public WingMember(Aircraft aircraft, int slot, AirframeProfile profile)
        {
            Aircraft = aircraft;
            Pilot = aircraft.pilots[0];
            Brain = new FormationPilot(slot);
            Profile = profile;
        }

        public int Number => Brain.Slot + 2;

        public bool Alive => Aircraft != null && !Aircraft.disabled && Pilot != null && !Pilot.dead && !Pilot.ejected;
    }
}
