namespace WingCommand
{
    /// <summary>The WEAPONS doctrine axis (spec WMC rebuild R3; design wmc-rebuild/weapons-radar.md C4).</summary>
    internal static class WeaponsFilter
    {
        /// <summary>Null when the scope can fly <paramref name="policy"/>; GUNS or MISSILES need someone carrying one (an
        /// empty station list sends the game's AI home out of ammo).</summary>
        public static string Refusal(WeaponsPolicy policy, bool anyGun, bool anyMissile) =>
            policy == WeaponsPolicy.Guns && !anyGun ? "nobody in scope carries a gun"
            : policy == WeaponsPolicy.Missiles && !anyMissile ? "nobody in scope carries missiles"
            : null;
    }
}
