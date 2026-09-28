namespace WingCommand
{
    /// <summary>Where a squadron pilot is, as WING says it (spec WMC rebuild §WING: state named in words, not only colour).</summary>
    internal enum PilotStatus : byte { Free, Inbound, Flying, Downed, Rescue, Missing, LocalSar, Captured, Kia }

    /// <summary>One status per pilot for the rows, the dossier stamp, the tiles and the alert, by one precedence: KIA, captured, a local
    /// search, a helicopter on the way, downed, missing, flying, a launch holding the seat, free.</summary>
    internal static class PilotStatuses
    {
        public static PilotStatus Of(bool lost, PilotRecoveryStatus recovery, bool flying, bool reserved, bool localSar, bool rescueGoing)
        {
            if (lost) return PilotStatus.Kia;
            if (recovery == PilotRecoveryStatus.Captured) return PilotStatus.Captured;
            if (localSar) return PilotStatus.LocalSar;
            if (recovery == PilotRecoveryStatus.Downed) return rescueGoing ? PilotStatus.Rescue : PilotStatus.Downed;
            if (recovery == PilotRecoveryStatus.Missing) return PilotStatus.Missing;
            if (flying) return PilotStatus.Flying;
            return reserved ? PilotStatus.Inbound : PilotStatus.Free;
        }

        /// <summary>Out of action for this mission: the LOST tile counts these.</summary>
        public static bool Lost(PilotStatus s) => s >= PilotStatus.Downed;
    }
}
