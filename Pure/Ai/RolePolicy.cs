namespace WingCommand
{
    /// <summary>What a member flies in a mixed wing: its formation slot, high cover over an anchor too slow for it, or
    /// trail behind an anchor too fast for it.</summary>
    internal enum Role : byte { Slot, HighCover, Trail }

    internal enum RoleReason : byte { None, AnchorSlow, AnchorFast, AnchorRecovered }

    /// <summary>The member's speed envelope against the anchor's speed. MinSpeed is the loaded minimum (0 for a
    /// helicopter); TopSpeed is what it can sustain in formation (a jet's usable maximum, a helicopter's cruise).</summary>
    internal struct RoleInput
    {
        public float AnchorSpeed, MinSpeed, TopSpeed;
    }

    /// <summary>Chooses each member's role (spec M2 §5.1), with hysteresis and a minimum dwell.
    /// <list type="bullet">
    /// <item>Slot → HighCover: the anchor slower than 1.15 × MinSpeed for 3 s (jets over a helicopter or a slow
    /// leader). Back to Slot above 1.3 × MinSpeed for 3 s. Ratios, so a slow airframe follows a slow leader of its own
    /// type; a helicopter (MinSpeed 0) never takes high cover.</item>
    /// <item>Slot → Trail: the anchor faster than TopSpeed for 5 s (helicopters behind jets). Back to Slot below
    /// 0.85 × TopSpeed for 5 s.</item>
    /// <item>At most one change per <see cref="MinDwell"/>. The behaviour change it causes is what the wing log records.</item>
    /// </list></summary>
    internal sealed class RolePolicy
    {
        public static float SlowFactor = 1.15f, RecoverFactor = 1.3f, SlowSeconds = 3f;
        public static float FastSeconds = 5f, BackFactor = 0.85f, MinDwell = 2f;

        private Persistence slow, recovered, fast, back;
        private float dwell = MinDwell;

        public Role Current { get; private set; } = Role.Slot;

        public bool Tick(in RoleInput m, float dt, out RoleReason reason)
        {
            reason = RoleReason.None;
            dwell += dt;
            bool isSlow = slow.Update(m.MinSpeed > 0f && m.AnchorSpeed < SlowFactor * m.MinSpeed, SlowSeconds, dt);
            bool isRecovered = recovered.Update(m.AnchorSpeed > RecoverFactor * m.MinSpeed, SlowSeconds, dt);
            bool isFast = fast.Update(m.TopSpeed > 0f && m.AnchorSpeed > m.TopSpeed, FastSeconds, dt);
            bool isBack = back.Update(m.AnchorSpeed < BackFactor * m.TopSpeed, FastSeconds, dt);
            if (dwell < MinDwell - 1e-6f) return false;
            switch (Current)
            {
                case Role.Slot:
                    if (isSlow) return Switch(Role.HighCover, RoleReason.AnchorSlow, out reason);
                    return isFast && Switch(Role.Trail, RoleReason.AnchorFast, out reason);
                case Role.HighCover:
                    return isRecovered && Switch(Role.Slot, RoleReason.AnchorRecovered, out reason);
                default:
                    return isBack && Switch(Role.Slot, RoleReason.AnchorRecovered, out reason);
            }
        }

        private bool Switch(Role to, RoleReason why, out RoleReason reason)
        {
            reason = why;
            Current = to;
            dwell = 0f;
            return true;
        }
    }
}
