namespace WingCommand
{
    /// <summary>A squadron pilot's status as WING and the room's SQUADRON read it (PilotStatuses.Of over the roster, the wing and search
    /// and rescue), and the number it reads with: the member it flies, or the helicopter going for it.</summary>
    internal static class WmcPilots
    {
        public static PilotStatus StatusOf(WingPilot p, WingService wing, out int number)
        {
            WingMember m = MemberOf(p, wing);
            number = m != null ? m.Number : 0;
            if (p == null) return PilotStatus.Free;
            bool down = p.RecoveryStatus == PilotRecoveryStatus.Downed || p.RecoveryStatus == PilotRecoveryStatus.Missing;
            bool local = down && WingSearchAndRescue.LocalRecoveryRemaining(p) >= 0f;
            int rescuer = p.RecoveryStatus == PilotRecoveryStatus.Downed && wing != null ? wing.RescuerNumber(p) : 0;
            PilotStatus s = PilotStatuses.Of(p.Lost, p.RecoveryStatus, WingPilotRoster.IsFlying(p), WingPilotRoster.IsReserved(p), local, rescuer > 0);
            if (s == PilotStatus.Rescue) number = rescuer;
            return s;
        }

        /// <summary>The wing member this pilot flies, or null.</summary>
        public static WingMember MemberOf(WingPilot p, WingService wing)
        {
            if (wing == null || p == null) return null;
            foreach (WingMember m in wing.Members)
                if (!m.Released && m.Aircraft != null && ReferenceEquals(WingPilotRoster.Of(m.Aircraft), p)) return m;
            return null;
        }
    }
}
