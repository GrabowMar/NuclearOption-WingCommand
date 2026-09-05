namespace WingCommand
{
    /// <summary>
    /// What a wingman reaches for first.
    ///
    /// This is a different question from <see cref="WingRoe"/>, and the two are easy to
    /// confuse. Rules of engagement decide <b>whether</b> a wingman may shoot at something;
    /// this decides <b>which of its own weapons</b> it prefers to shoot it with, and which
    /// kind of contact it prefers to look for when both are permitted and available.
    ///
    /// Every value is a bias, never a restriction. A preference that could leave a wingman
    /// holding fire with a perfectly good alternative station would be a worse control than
    /// no control at all, so an unavailable, empty or out-of-range preferred store simply
    /// falls through to the same effectiveness ranking the mod has always used.
    /// </summary>
    internal enum WingWeaponPreference
    {
        /// <summary>Stock behaviour: the most effective ready station for the target.</summary>
        Auto,

        /// <summary>Hunt aircraft first, and reach for the anti-air stores.</summary>
        AirToAir,

        /// <summary>Hunt surface contacts first, and reach for the anti-surface stores.</summary>
        AirToGround,

        /// <summary>
        /// Use the shortest-ranged store that will do the job — the gun and rocket end of
        /// the loadout — so standoff missiles are kept for something that needs them.
        /// </summary>
        ShortRange,
    }

    /// <summary>Player-facing names for <see cref="WingWeaponPreference"/>.</summary>
    internal static class WingWeaponPreferences
    {
        /// <summary>The order the selector shows them in.</summary>
        public static readonly WingWeaponPreference[] All =
        {
            WingWeaponPreference.Auto,
            WingWeaponPreference.AirToAir,
            WingWeaponPreference.AirToGround,
            WingWeaponPreference.ShortRange,
        };

        /// <summary>Button label on the Tactical tab.</summary>
        public static string Label(WingWeaponPreference preference)
        {
            switch (preference)
            {
                case WingWeaponPreference.AirToAir:    return "A-A";
                case WingWeaponPreference.AirToGround: return "A-G";
                case WingWeaponPreference.ShortRange:  return "GUNS";
                default:                               return "AUTO";
            }
        }

        /// <summary>Compact form for the roster and the docked HUD strip.</summary>
        public static string ShortLabel(WingWeaponPreference preference)
        {
            switch (preference)
            {
                case WingWeaponPreference.AirToAir:    return "AA";
                case WingWeaponPreference.AirToGround: return "AG";
                case WingWeaponPreference.ShortRange:  return "GUN";
                default:                               return "AUT";
            }
        }

        /// <summary>The one-line explanation shown under the selector.</summary>
        public static string Hint(WingWeaponPreference preference)
        {
            switch (preference)
            {
                case WingWeaponPreference.AirToAir:
                    return "Prefers hostile aircraft and anti-air stores.";
                case WingWeaponPreference.AirToGround:
                    return "Prefers surface contacts and anti-surface stores.";
                case WingWeaponPreference.ShortRange:
                    return "Prefers close-in stores, saving standoff weapons.";
                default:
                    return "Picks the most effective ready station for the target.";
            }
        }
    }
}
