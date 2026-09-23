namespace WingCommand
{
    /// <summary>What the member's mind looks at when choosing a behaviour.</summary>
    internal struct MindInput
    {
        public float Sigma, SlotError, Spacing, LeaderSpeed, LoadedMinimum;
        public bool LeaderFlying, LeaderLost;
    }

    /// <summary>Chooses the member's behaviour at 5 Hz. Every change has hysteresis and a 2 s minimum dwell,
    /// except leader loss, which switches immediately.
    /// <list type="bullet">
    /// <item>Rejoin → StationKeep: σ has been 1 for 3 s.</item>
    /// <item>StationKeep → Rejoin: slot error above 2.5 spacings for 2 s.</item>
    /// <item>Either → HoldOverhead: the leader is not flying, or has been slower than 1.15 × the member's loaded
    /// minimum for 3 s (a ratio, so slow airframes follow a slow leader of their own type).</item>
    /// <item>Any → HoldOverhead: the leader is lost (immediate).</item>
    /// <item>HoldOverhead → Rejoin: the leader has been faster than 1.3 × the loaded minimum for 3 s.</item>
    /// </list>
    /// Persistence timers run every tick; decisions are taken every 0.2 s.</summary>
    internal sealed class PilotMind
    {
        public static float DecisionPeriod = 0.2f, MinDwell = 2f;
        public static float CaptureSigma = 0.99f, CaptureSeconds = 3f;
        public static float LostSlotSpacings = 2.5f, LostSlotSeconds = 2f;
        public static float SlowFactor = 1.15f, FastFactor = 1.3f, LeaderSpeedSeconds = 3f;

        private Persistence captured, lostSlot, slow, fast;
        private float sinceDecision;

        public BehaviourId Current { get; private set; } = BehaviourId.Rejoin;
        public float Dwell { get; private set; }

        public bool Tick(in MindInput m, float dt, out BehaviourId from, out TransitionReason reason)
        {
            from = Current;
            reason = TransitionReason.None;
            Dwell += dt;
            sinceDecision += dt;
            bool isCaptured = captured.Update(m.Sigma >= CaptureSigma, CaptureSeconds, dt);
            bool isLost = lostSlot.Update(m.SlotError > LostSlotSpacings * m.Spacing, LostSlotSeconds, dt);
            bool isSlow = slow.Update(m.LeaderSpeed < m.LoadedMinimum * SlowFactor, LeaderSpeedSeconds, dt);
            bool isFast = fast.Update(m.LeaderSpeed > m.LoadedMinimum * FastFactor, LeaderSpeedSeconds, dt);

            if (m.LeaderLost)
                return Current != BehaviourId.HoldOverhead && Switch(BehaviourId.HoldOverhead, TransitionReason.LeaderLost, out reason);
            if (sinceDecision < DecisionPeriod - 1e-6f) return false;
            sinceDecision = 0f;
            if (Dwell < MinDwell - 1e-6f) return false;

            switch (Current)
            {
                case BehaviourId.HoldOverhead:
                    return m.LeaderFlying && isFast && Switch(BehaviourId.Rejoin, TransitionReason.LeaderRecovered, out reason);
                default:
                    if (!m.LeaderFlying) return Switch(BehaviourId.HoldOverhead, TransitionReason.LeaderNotFlying, out reason);
                    if (isSlow) return Switch(BehaviourId.HoldOverhead, TransitionReason.LeaderSlow, out reason);
                    if (Current == BehaviourId.Rejoin && isCaptured)
                        return Switch(BehaviourId.StationKeep, TransitionReason.Captured, out reason);
                    if (Current == BehaviourId.StationKeep && isLost)
                        return Switch(BehaviourId.Rejoin, TransitionReason.LostSlot, out reason);
                    return false;
            }
        }

        /// <summary>Switch now, ignoring dwell (a player command). Returns false when already there.</summary>
        public bool Force(BehaviourId to)
        {
            if (Current == to) return false;
            Current = to;
            Dwell = 0f;
            captured = default;
            lostSlot = default;
            return true;
        }

        private bool Switch(BehaviourId to, TransitionReason why, out TransitionReason reason)
        {
            reason = why;
            Current = to;
            Dwell = 0f;
            return true;
        }
    }
}
