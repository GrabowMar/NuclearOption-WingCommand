namespace WingCommand
{
    /// <summary>Live wing doctrine for station-keeping shots. Pure rules stay in WingDoctrineRules.</summary>
    internal static class DoctrineLive
    {
        public static WingDoctrine Current =>
            WingCommandManager.Instance?.Wing?.Doctrine ?? WingDoctrine.Reserve;

        public static bool MissileShotAvailable(Aircraft aircraft)
        {
            if (Current.Guard == MissileGuard.Off) return false;
            return WingWeapons.HasMissileDefence(aircraft) && Protectee(aircraft) != null;
        }

        public static Aircraft Protectee(Aircraft aircraft)
        {
            if (aircraft == null || Current.Guard == MissileGuard.Off) return null;
            var order = new ProtecteeRank[3];
            int count = WingDoctrineRules.CopyProtecteeOrder(Current.Guard, order);
            WingRegistry wing = WingCommandManager.Instance?.Wing;
            Aircraft leader = wing?.Leader;
            for (int i = 0; i < count; i++)
            {
                switch (order[i])
                {
                    case ProtecteeRank.Self:
                        if (Warned(aircraft)) return aircraft;
                        break;
                    case ProtecteeRank.Leader:
                        if (leader != null && Warned(leader)) return leader;
                        break;
                    default:
                        if (wing == null) break;
                        for (int m = 0; m < wing.Members.Count; m++)
                        {
                            WingMember member = wing.Members[m];
                            Aircraft other = member != null ? member.Aircraft : null;
                            if (other == null || other == aircraft) continue;
                            if (Warned(other)) return other;
                        }
                        break;
                }
            }
            return null;
        }

        /// <summary>Cover's aircraft threat: leader first, then self. Other target policies do not use this.</summary>
        public static Unit PriorityTarget(Aircraft aircraft, Aircraft leader, float range)
        {
            if (Current.Targets != TargetPolicy.Cover) return null;
            Unit target = WingWeapons.NearestThreatTo(leader, range);
            return target ?? WingWeapons.NearestThreatTo(aircraft, range);
        }

        private static bool Warned(Aircraft aircraft)
        {
            MissileWarning warning = aircraft != null ? aircraft.GetMissileWarningSystem() : null;
            return warning != null && warning.IsWarning();
        }
    }
}
