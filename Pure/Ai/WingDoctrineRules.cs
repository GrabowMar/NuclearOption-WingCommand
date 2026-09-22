namespace WingCommand
{
    /// <summary>Engine-free consequences of a wing doctrine. Callers supply the live aircraft.</summary>
    internal static class WingDoctrineRules
    {
        public static float SpacingScale(FormationInterval interval)
        {
            switch (interval)
            {
                case FormationInterval.Open: return WingTuning.IntervalOpen;
                case FormationInterval.Standard: return WingTuning.IntervalStandard;
                default: return WingTuning.IntervalClose;
            }
        }

        public static bool StickyTrack(FormationInterval interval) =>
            interval == FormationInterval.Close;

        public static bool EchelonSwap(FormationInterval interval) =>
            interval != FormationInterval.Close;

        public static float EngageRange(EngagementReach reach) =>
            reach == EngagementReach.Long ? WingTuning.ReachLongMetres : WingTuning.ReachSlotMetres;

        public static float ExplicitOrderRange() =>
            WingTuning.ReachLongMetres > WingTuning.ReachSlotMetres
                ? WingTuning.ReachLongMetres
                : WingTuning.ReachSlotMetres;

        public static DoctrineAllow StandingAllow(TargetPolicy targets)
        {
            switch (targets)
            {
                case TargetPolicy.Air: return DoctrineAllow.AirOnly;
                case TargetPolicy.Ground: return DoctrineAllow.GroundOnly;
                case TargetPolicy.Both: return DoctrineAllow.AirAndGround;
                default: return DoctrineAllow.None;
            }
        }

        public static int CopyProtecteeOrder(MissileGuard guard, ProtecteeRank[] dest)
        {
            if (dest == null || dest.Length == 0 || guard == MissileGuard.Off) return 0;
            int count = 0;
            if (guard == MissileGuard.Lead) Add(ProtecteeRank.Leader);
            Add(ProtecteeRank.Self);
            if (guard != MissileGuard.Self) Add(ProtecteeRank.Leader);
            if (guard != MissileGuard.Self) Add(ProtecteeRank.Wingman);
            return count;

            void Add(ProtecteeRank rank)
            {
                if (count >= dest.Length) return;
                for (int i = 0; i < count; i++)
                    if (dest[i] == rank) return;
                dest[count++] = rank;
            }
        }

        public static bool PreferInterception(MissileResponse response, MissileGuard guard, string seeker,
            bool armed, float impactSeconds, bool hasCover, float playerDistance, float leashRadius)
        {
            if (guard == MissileGuard.Off || response != MissileResponse.Press) return false;
            return seeker == "SARH" && armed && impactSeconds > 3f &&
                !(hasCover && playerDistance >= 0f && leashRadius > 0f && playerDistance <= leashRadius);
        }

        /// <summary>Multiply spacing by the threat scale only when the pilot asked the formation to open.</summary>
        public static float AppliedSpacingScale(FormationInterval interval, bool spreadWhenThreatened, float threatScale)
        {
            float intervalScale = SpacingScale(interval);
            if (spreadWhenThreatened && threatScale > 1.001f && threatScale > intervalScale)
                return threatScale;
            return intervalScale;
        }
    }
}
