namespace WingCommand
{
    /// <summary>Soft weapon and target-class preference, independent of ROE permission. Missing, empty,
    /// unready, or invalid preferred stores fall back to effective alternatives.</summary>
    internal enum WingWeaponPreference
    {
        /// <summary>Choose the most effective valid ready station.</summary>
        Auto,

        /// <summary>Prefer aircraft targets and anti-air stores.</summary>
        AirToAir,

        /// <summary>Prefer surface targets and anti-surface stores.</summary>
        AirToGround,

        /// <summary>Bias effective short-range stores to preserve stand-off munitions.</summary>
        ShortRange,
    }

    /// <summary>Shared weapon-preference UI text.</summary>
    internal static class WingWeaponPreferences
    {
        /// <summary>Preference selector order.</summary>
        public static readonly WingWeaponPreference[] All =
        {
            WingWeaponPreference.Auto,
            WingWeaponPreference.AirToAir,
            WingWeaponPreference.AirToGround,
            WingWeaponPreference.ShortRange,
        };

        /// <summary>Tactical selector button label.</summary>
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

        /// <summary>Compact preference text for roster and docked HUD.</summary>
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

        /// <summary>Explanation beneath the preference selector.</summary>
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
