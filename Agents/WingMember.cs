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
        /// <summary>Set while the member is on the ground after a field launch (taxi, lineup, roll, climb-out); the
        /// formation brain takes over when it is done.</summary>
        public GroundPilot Ground;
        /// <summary>Set while the member goes home (RTB, Refit, bingo) until it is back in the reserve or airborne again.</summary>
        public RecoveryPilot Recovery;
        /// <summary>A recovery asked for while it could not turn round (lining up, rolling): it starts once airborne.</summary>
        public RecoveryIntent PendingRecovery;
        public bool HasPendingRecovery, ReserveNow;
        public readonly BingoMonitor Bingo = new BingoMonitor();
        public float BingoClock, BingoFieldAt;
        /// <summary>Diagnostics: when the member last came to a stop on the ground, and whether that stop was logged.</summary>
        public float StoppedSince = float.NaN;
        /// <summary>Still on the game's landing list of this field after touchdown (until off the runway).</summary>
        public Airbase ListedAt;
        public float ListedUntil;
        /// <summary>The eject guard blocked the game's landing state ejecting it: take it back.</summary>
        public bool EjectBlocked;
        /// <summary>A helicopter down on a pad keeps its place in the pad's landing queue.</summary>
        public bool PadHeld;
        /// <summary>The ground pilot's last output (the ground trace).</summary>
        public ControlOutput GroundOutput;
        public bool StopLogged;
        public Airbase BingoField;
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
            Brain = new FormationPilot(slot, profile.Class);
            Profile = profile;
        }

        public int Number => Brain.Slot + 2;

        public bool OnGround => Ground != null && !Ground.Done;

        public bool Alive => Aircraft != null && !Aircraft.disabled && Pilot != null && !Pilot.dead && !Pilot.ejected;
    }
}
