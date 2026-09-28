namespace WingCommand
{
    /// <summary>What happens to a squadron pilot when its aircraft leaves the wing.</summary>
    internal enum PilotFate : byte { Home, Released, Down }

    internal static class PilotFates
    {
        /// <summary>Home (back in the reserve, which the game marks by disabling the aircraft) or released with the aircraft
        /// intact and the pilot seated and alive (dismissed, taken over): free again. Anything else — the aircraft destroyed
        /// with the pilot still aboard (review M3c C1: wing members never eject), the pilot dead or ejected — is down, for
        /// search and rescue to settle.</summary>
        public static PilotFate Of(bool home, bool aircraftDisabled, bool pilotMissing, bool dead, bool ejected) =>
            home ? PilotFate.Home
            : !aircraftDisabled && !pilotMissing && !dead && !ejected ? PilotFate.Released
            : PilotFate.Down;
    }
}
