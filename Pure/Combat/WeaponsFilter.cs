namespace WingCommand
{
    /// <summary>The WEAPONS doctrine axis (spec WMC rebuild R3; design wmc-rebuild/weapons-radar.md C4): which stations a
    /// member may use and which targets it may choose. Jammers are always allowed; a radar-guided (SARH) missile only while
    /// the member's radar is wanted on, since the game lets one guide with the radar off.</summary>
    internal static class WeaponsFilter
    {
        /// <summary>Null when an aircraft can fly <paramref name="policy"/>; GUNS or MISSILES need one aboard (an empty station
        /// list sends the game's AI home out of ammo, so the order is refused instead).</summary>
        public static string Refusal(WeaponsPolicy policy, bool anyGun, bool anyMissile) =>
            policy == WeaponsPolicy.Guns && !anyGun ? "carries no gun"
            : policy == WeaponsPolicy.Missiles && !anyMissile ? "carries no missiles"
            : null;

        public static bool AllowsStation(WeaponsPolicy policy, StoreClass store, bool sarh, bool radarWanted)
        {
            if (store == StoreClass.Ecm) return true;
            if (sarh && !radarWanted) return false;
            switch (policy)
            {
                case WeaponsPolicy.Missiles: return store == StoreClass.AirMissile || store == StoreClass.StrikeMissile;
                case WeaponsPolicy.Guns: return store == StoreClass.Gun;
                default: return true;
            }
        }

        /// <summary>NO A-G keeps air targets only (a missile counts as air).</summary>
        public static bool AllowsTarget(WeaponsPolicy policy, bool air) => policy != WeaponsPolicy.NoAirToGround || air;

        /// <summary>Whether the member's stations need filtering at all (the target patch swaps the list only then).</summary>
        public static bool Restricts(WeaponsPolicy policy, bool radarWanted) => policy != WeaponsPolicy.Auto || !radarWanted;
    }
}
