namespace WingCommand
{
    /// <summary>What an ordered ejection needs to know about one member (the engine fills it).</summary>
    internal struct EjectFacts
    {
        /// <summary>This machine simulates the aircraft (a client's call would only set a flag).</summary>
        public bool Host;
        public bool Released, PlayerFlown;
        /// <summary>The aircraft is gone or disabled.</summary>
        public bool Lost;
        public bool PilotDead;
        /// <summary>An ejection has started (the aircraft's flag or the pilot's).</summary>
        public bool Ejected;
        /// <summary>Under ground supervision (taxi, takeoff, climb-out, taxi-in): <see cref="Phase"/> says which.</summary>
        public bool Supervised;
        public GroundPhase Phase;
        /// <summary>In the game's landing for us.</summary>
        public bool Landing;
        /// <summary>A helicopter settled at its ground point.</summary>
        public bool SettledDown;
        public float RadarAlt, Speed;
    }

    /// <summary>When the wing may order a member out (design wmc-rebuild/eject.md). <see cref="Guarded"/> is the native
    /// guard's own predicate, so every case the guard would swallow is refused with a reason first.</summary>
    internal static class EjectRules
    {
        /// <summary>The game's <c>Aircraft.IsLanded</c>: below this height and speed an aircraft is on the ground.</summary>
        public static float LandedHeight = 5f, LandedSpeed = 2.5f;
        /// <summary>Slower than this the ejected airframe parks and goes back to the game's stock.</summary>
        public static float HoverSpeed = 2f;

        /// <summary>The eject guard blocks every ejection of an intact member under ground supervision or landing.</summary>
        public static bool Guarded(bool disabled, bool released, bool supervised, bool landing) =>
            !disabled && !released && (supervised || landing);

        /// <summary>Null when the member may eject; else why not (the first rule that holds).</summary>
        public static string Refusal(in EjectFacts f)
        {
            if (!f.Host) return "host only";
            if (f.Released || f.PlayerFlown) return "not flying for the wing";
            if (f.Lost) return "aircraft already lost";
            if (f.PilotDead) return "pilot dead";
            if (f.Ejected) return "already ejected";
            if (f.Supervised)
                return f.Phase == GroundPhase.ClimbOut || f.Phase == GroundPhase.LiftOff ? "still climbing out" : "on the ground, send it home";
            if (f.Landing) return "landing";
            if (f.SettledDown || (f.RadarAlt < LandedHeight && f.Speed < LandedSpeed)) return "on the ground";
            if (f.Speed < HoverSpeed) return "hovering, land it or send it home";
            return null;
        }
    }
}
