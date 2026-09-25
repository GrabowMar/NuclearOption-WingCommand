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
        /// <summary>Fighting in the game's combat state, supervised (spec M5 §2), and the target it was told to attack.</summary>
        public bool Engaged;
        public Unit AssignedTarget;
        /// <summary>How long the game's combat state has had no target.</summary>
        public float NoTargetClock;
        /// <summary>How long the member has been unable to attack its assigned target (<see cref="TargetAllocator"/>).</summary>
        public float TargetLost;
        /// <summary>The missile the defence last classified (spec M5 §7.1), and how.</summary>
        public Missile ThreatMissile;
        /// <summary>Standing fire from the slot (spec M5 §8): the cadence and the last target.</summary>
        public readonly FireCadence Cadence = new FireCadence();
        /// <summary>A helicopter landing here or down (spec M4 §5); null when flying with the wing.</summary>
        public SettlePilot Settle;
        /// <summary>The seated pilot's combat perks as modifiers (spec M5 §11).</summary>
        public PerkEffects Perks = PerkEffects.None;
        /// <summary>The member's radio voice, dealt once when it joins (review M7d I1: slots renumber, voices must not).</summary>
        public int Voice;
        /// <summary>What the settle is for once down (spec M4 §7); null: hold until Take Off.</summary>
        public SettleJob Job;
        /// <summary>The downed pilot a rescue settle waits for.</summary>
        public Unit RescueTarget;
        public Unit StandingTarget;
        public MissileSeeker ThreatSeeker;
        /// <summary>A serial per new missile (a new one picks its own notch side), the last seeker type classified and the
        /// game's countermeasure choice for it.</summary>
        public int ThreatSerial;
        /// <summary>The game's landing mode last seen (diagnostics) and when it was read.</summary>
        public string LandingMode = "";
        public float LandingModeClock;
        public string ThreatSeekerType, ThreatChoice;
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
        /// <summary>The hull from its parts' hit points, sampled at 1 Hz (spec WMC rebuild R3: DAMAGED, INSPECT).</summary>
        public readonly DamageWatch Damage = new DamageWatch();
        /// <summary>Out of weapons in formation, called once (a field: updated in place).</summary>
        public WinchesterLatch Winchester;
        /// <summary>The RADAR doctrine's last toggle (a field: updated in place).</summary>
        public RadarGate RadarGate;

        public WingMember(Aircraft aircraft, int slot, AirframeProfile profile)
        {
            Aircraft = aircraft;
            Pilot = aircraft.pilots[0];
            Brain = new FormationPilot(slot, profile.Class);
            Profile = profile;
        }

        /// <summary>The member's place in the wing (#n = seat + 2), stable across element moves; seats compact when a member
        /// leaves (spec WMC program §3.3). <see cref="FormationPilot.Slot"/> is its place in its element's shape.</summary>
        public int Seat;
        public int Number => Seat + 2;

        public bool OnGround => Ground != null && !Ground.Done;

        /// <summary>Flying for the wing: an ejection that has started (the aircraft's flag) ends it at once.</summary>
        public bool Alive => Aircraft != null && !Aircraft.disabled && !Aircraft.HasEjected() && Pilot != null && !Pilot.dead && !Pilot.ejected;
    }
}
