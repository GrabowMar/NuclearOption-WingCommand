namespace WingCommand
{
    /// <summary>The deep card's facts about one member, gathered on the host (spec WMC program §4): fuel and bingo, ammo,
    /// damage (detached parts), radar, current target, stores by station, the pilot. Only for the one selected member,
    /// at the panel's refresh rate.</summary>
    internal static class WmcDetail
    {
        /// <summary>The member's stores by station, each with its flight-pool class (TACTICAL's FLIGHT POOL gathers these for
        /// every scoped member at the panel's rate: no pilot text, nothing allocated).</summary>
        public static void Stores(WingMember m, ref MemberDetail d)
        {
            Aircraft a = m.Aircraft;
            d.StoreCount = 0;
            // Review R1 C1: a caller's detail without a stores array got a NullReferenceException that faulted the room.
            if (d.Stores == null) d.Stores = new StoreLine[DetailLines.MaxStores];
            if (a == null || a.disabled || a.weaponStations == null) return;
            foreach (WeaponStation s in a.weaponStations)
            {
                if (d.StoreCount >= d.Stores.Length) break;
                if (s == null || s.Cargo || s.WeaponInfo == null || s.WeaponInfo.hideInDisplay) continue;
                WeaponInfo w = s.WeaponInfo;
                string name = !string.IsNullOrEmpty(w.shortName) ? w.shortName : w.weaponName;
                d.Stores[d.StoreCount++] = new StoreLine
                {
                    Name = name, Ammo = s.Ammo, Full = s.FullAmmo,
                    Class = StoreClasses.Of(w.gun, w.jammer, w.missile, w.bomb || w.glideBomb, w.effectiveness.antiAir, w.effectiveness.antiSurface),
                };
            }
        }

        public static void Gather(WingMember m, ref MemberDetail d)
        {
            Aircraft a = m.Aircraft;
            bool alive = a != null && !a.disabled;
            d.Fuel = alive ? a.GetFuelLevel() : float.NaN;
            d.Ammo = alive ? WingService.AmmoFraction(a) : float.NaN;
            d.BingoSeconds = m.Bingo.SecondsToBingo;
            d.Damage = alive && a.partDamageTracker != null ? a.partDamageTracker.GetDetachedRatio() : float.NaN;
            d.Radar = alive && a.radar != null ? (a.radar.activated ? 1 : 0) : -1;
            Unit t = m.AssignedTarget != null ? m.AssignedTarget : m.StandingTarget;
            d.Target = t != null && !t.disabled ? (t.definition != null ? t.definition.unitName : t.unitName) : null;
            Stores(m, ref d);
            WingPilot p = a != null ? WingPilotRoster.Of(a) : null;
            d.Callsign = p?.Callsign;
            d.Rank = p != null ? WingPilotRoster.RankName(p.Rank) : null;
            d.Perks = null;
            if (p != null && p.Perks.Count > 0)
            {
                var names = new string[p.Perks.Count];
                for (int i = 0; i < names.Length; i++) names[i] = PilotPerks.Name(p.Perks[i]);
                d.Perks = string.Join(", ", names);
            }
        }
    }
}
