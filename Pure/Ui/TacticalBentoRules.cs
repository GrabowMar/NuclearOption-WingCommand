using System;
using System.Collections.Generic;
using System.Globalization;

namespace WingCommand
{
    public enum BentoThreatLevel
    {
        Clear,
        Caution,
        Danger,
    }

    public readonly struct BentoRawStore
    {
        public readonly string Name;
        public readonly int Ammo;
        public readonly int Capacity;
        public readonly bool IsMissile;
        public readonly bool IsGun;
        public readonly bool IsJammer;
        public readonly bool IsBomb;

        public BentoRawStore(string name, int ammo, int capacity, bool isMissile, bool isGun, bool isJammer, bool isBomb)
        {
            Name = name ?? "";
            Ammo = ammo;
            Capacity = capacity;
            IsMissile = isMissile;
            IsGun = isGun;
            IsJammer = isJammer;
            IsBomb = isBomb;
        }
    }

    public readonly struct BentoTargetInfo
    {
        public readonly string Name;
        public readonly string UnitClass;
        public readonly float Distance;
        public readonly float ClosingSpeed;
        public readonly float Altitude;
        public readonly bool HasTarget;

        public BentoTargetInfo(string name, string unitClass, float distance, float closingSpeed, float altitude, bool hasTarget)
        {
            Name = name ?? "";
            UnitClass = unitClass ?? "";
            Distance = distance;
            ClosingSpeed = closingSpeed;
            Altitude = altitude;
            HasTarget = hasTarget;
        }

        public static BentoTargetInfo None => new BentoTargetInfo("", "", 0f, 0f, 0f, false);
    }

    public readonly struct BentoThreatInfo
    {
        public readonly BentoThreatLevel Level;
        public readonly string StatusText;

        public BentoThreatInfo(BentoThreatLevel level, string statusText)
        {
            Level = level;
            StatusText = statusText ?? "";
        }
    }

    public readonly struct BentoTelemetryInfo
    {
        public readonly float RadarAltitude;
        public readonly float SpeedKnots;
        public readonly float SlotErrorMeters;
        public readonly float IntegrityFraction;
        public readonly bool IsFlightLead;
        public readonly bool InFormation;

        public BentoTelemetryInfo(float radarAltitude, float speedKnots, float slotErrorMeters,
                                  float integrityFraction, bool isFlightLead, bool inFormation)
        {
            RadarAltitude = radarAltitude;
            SpeedKnots = speedKnots;
            SlotErrorMeters = slotErrorMeters;
            IntegrityFraction = integrityFraction;
            IsFlightLead = isFlightLead;
            InFormation = inFormation;
        }
    }

    /// <summary>
    /// Pure formatting and calculation rules for the Tactical Situational Bento.
    /// Engine-free, zero Unity dependencies.
    /// </summary>
    internal static class TacticalBentoRules
    {
        public static string FormatDistance(float meters)
        {
            if (float.IsNaN(meters) || float.IsInfinity(meters) || meters <= 0f) return "0 m";
            if (meters < 1000f)
                return Math.Round(meters).ToString(CultureInfo.InvariantCulture) + " m";
            float km = meters / 1000f;
            return km.ToString("0.0", CultureInfo.InvariantCulture) + " km";
        }

        public static string FormatClosingSpeed(float mps)
        {
            if (float.IsNaN(mps) || float.IsInfinity(mps) || Math.Abs(mps) < 0.5f) return "0 m/s";
            long rounded = (long)Math.Round(mps);
            return (rounded > 0 ? "+" : "") + rounded.ToString(CultureInfo.InvariantCulture) + " m/s";
        }

        public static string FormatAltitude(float meters)
        {
            if (float.IsNaN(meters) || float.IsInfinity(meters) || meters <= 0f) return "0 m";
            long rounded = (long)Math.Round(meters);
            return rounded.ToString("N0", CultureInfo.InvariantCulture) + " m";
        }

        public static string FormatSpeed(float knots)
        {
            if (float.IsNaN(knots) || float.IsInfinity(knots) || knots <= 0f) return "0 kt";
            long rounded = (long)Math.Round(knots);
            return rounded.ToString(CultureInfo.InvariantCulture) + " kt";
        }

        public static string FormatSlotDeviation(float errorMeters, bool isFlightLead, bool inFormation)
        {
            if (isFlightLead) return "LEAD";
            if (!inFormation) return "INDEP";
            if (float.IsNaN(errorMeters) || float.IsInfinity(errorMeters) || errorMeters < 0f)
                return "0m (TIGHT)";

            long rounded = (long)Math.Round(errorMeters);
            if (rounded <= 25) return rounded + "m (TIGHT)";
            if (rounded <= 65) return rounded + "m (FORM)";
            return rounded + "m (WIDE)";
        }

        public static string FormatSingleFuel(float fuelFraction)
        {
            if (float.IsNaN(fuelFraction) || float.IsInfinity(fuelFraction) || fuelFraction <= 0f)
                return "FUEL: 0%";
            int pct = Math.Clamp((int)Math.Round(fuelFraction * 100f), 0, 100);
            return "FUEL: " + pct + "%";
        }

        public static string FormatFlightFuel(float minFuelFraction, float avgFuelFraction)
        {
            if (float.IsNaN(minFuelFraction) || float.IsInfinity(minFuelFraction) || minFuelFraction <= 0f)
                return "FUEL: 0% MIN";
            int minPct = Math.Clamp((int)Math.Round(minFuelFraction * 100f), 0, 100);
            return "FUEL: " + minPct + "% MIN";
        }


        public static BentoThreatInfo ResolveThreat(bool missileWarned, string missileSeeker,
                                                    float integrityFraction, bool isDefending)
        {
            if (missileWarned)
            {
                string seeker = string.IsNullOrWhiteSpace(missileSeeker) ? "" : " [" + missileSeeker.Trim() + "]";
                return new BentoThreatInfo(BentoThreatLevel.Danger, "EVADING MISSILE" + seeker);
            }

            if (isDefending)
            {
                return new BentoThreatInfo(BentoThreatLevel.Danger, "DEFENSIVE JINKING");
            }

            if (!float.IsNaN(integrityFraction) && integrityFraction < 0.50f)
            {
                int pct = Math.Max(0, (int)Math.Round(integrityFraction * 100f));
                return new BentoThreatInfo(BentoThreatLevel.Danger, "HULL CRITICAL " + pct + "%");
            }

            if (!float.IsNaN(integrityFraction) && integrityFraction < 0.85f)
            {
                int pct = (int)Math.Round(integrityFraction * 100f);
                return new BentoThreatInfo(BentoThreatLevel.Caution, "HULL DAMAGED " + pct + "%");
            }

            return new BentoThreatInfo(BentoThreatLevel.Clear, "THREAT: CLEAR");
        }

        /// <summary>
        /// Aggregate individual stores for a single wingman into up to 3 display lines.
        /// </summary>
        public static void FormatSingleMemberStores(IReadOnlyList<BentoRawStore> stores,
            out string line1, out string line2, out string line3, out bool isWinchester)
        {
            FormatSingleMemberStores(stores, out line1, out line2, out line3, out isWinchester, out _);
        }

        /// <summary>
        /// Aggregate individual stores for a single wingman into up to 3 display lines, plus total ammo.
        /// </summary>
        public static void FormatSingleMemberStores(IReadOnlyList<BentoRawStore> stores,
            out string line1, out string line2, out string line3, out bool isWinchester, out int totalAmmo)
        {
            line1 = "—";
            line2 = "—";
            line3 = "—";
            isWinchester = true;
            totalAmmo = 0;

            if (stores == null || stores.Count == 0)
            {
                line1 = "NO STORES MOUNTED";
                return;
            }

            // Group stores by name
            var names = new List<string>();
            var counts = new List<int>();
            var stations = new List<int>();
            var isGunList = new List<bool>();
            var isMissileList = new List<bool>();

            int totalOrdnance = 0;

            for (int i = 0; i < stores.Count; i++)
            {
                BentoRawStore store = stores[i];
                string name = CleanStoreName(store.Name);
                int idx = names.IndexOf(name);
                if (idx < 0)
                {
                    names.Add(name);
                    counts.Add(Math.Max(0, store.Ammo));
                    stations.Add(1);
                    isGunList.Add(store.IsGun);
                    isMissileList.Add(store.IsMissile || store.IsBomb);
                }
                else
                {
                    counts[idx] += Math.Max(0, store.Ammo);
                    stations[idx]++;
                }

                if (store.IsMissile || store.IsBomb || store.IsGun)
                    totalOrdnance += Math.Max(0, store.Ammo);
            }

            totalAmmo = totalOrdnance;
            isWinchester = totalOrdnance == 0;

            // Separate into missiles/guided, bombs/secondary, and gun/pods
            var displayLines = new List<string>();

            for (int i = 0; i < names.Count; i++)
            {
                string prefix = stations[i] > 1 ? stations[i] + "x " : "";
                string itemStr = prefix + names[i] + " [" + counts[i] + "]";
                displayLines.Add(itemStr);
            }

            if (displayLines.Count > 0) line1 = displayLines[0];
            if (displayLines.Count > 1) line2 = displayLines[1];
            if (displayLines.Count > 2)
            {
                if (displayLines.Count > 3)
                {
                    line3 = displayLines[2] + " · +" + (displayLines.Count - 3) + " MORE";
                }
                else
                {
                    line3 = displayLines[2];
                }
            }
        }

        public static void FormatFlightStores(IReadOnlyList<BentoRawStore> allStores,
            out string line1, out string line2, out string line3, out bool isWinchester)
        {
            FormatFlightStores(allStores, out line1, out line2, out line3, out isWinchester, out _);
        }

        /// <summary>
        /// Aggregate pool across multiple selected wingmen, plus total ammo.
        /// </summary>
        public static void FormatFlightStores(IReadOnlyList<BentoRawStore> allStores,
            out string line1, out string line2, out string line3, out bool isWinchester, out int totalAmmo)
        {
            line1 = "—";
            line2 = "—";
            line3 = "—";
            isWinchester = true;
            totalAmmo = 0;

            if (allStores == null || allStores.Count == 0)
            {
                line1 = "NO ACTIVE WEAPONS";
                return;
            }

            int missiles = 0;
            int bombs = 0;
            int gunRounds = 0;
            int jammers = 0;

            for (int i = 0; i < allStores.Count; i++)
            {
                BentoRawStore s = allStores[i];
                if (s.IsMissile) missiles += Math.Max(0, s.Ammo);
                else if (s.IsBomb) bombs += Math.Max(0, s.Ammo);
                else if (s.IsGun) gunRounds += Math.Max(0, s.Ammo);
                if (s.IsJammer) jammers++;
            }

            totalAmmo = missiles + bombs + gunRounds;
            isWinchester = totalAmmo == 0;

            line1 = "MISSILES : " + missiles + " READY";
            line2 = "STRIKE   : " + bombs + " BOMBS / AGMs";
            line3 = "CANNON   : " + gunRounds.ToString("N0", CultureInfo.InvariantCulture) + " RDS" +
                    (jammers > 0 ? " · " + jammers + "x ECM" : "");
        }


        public static string FormatFlightPosture(int engaging, int inFormation, int defending, int other)
        {
            int total = engaging + inFormation + defending + other;
            if (total == 0) return "POSTURE: STANDBY";
            return "POSTURE: " + engaging + " ATK · " + inFormation + " FORM · " + defending + " DEF" +
                   (other > 0 ? " · " + other + " RTB/REFIT" : "");
        }

        private static string CleanStoreName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "STORE";
            string s = raw.Replace("(Clone)", "").Replace("_", " ").Trim().ToUpperInvariant();
            if (s.Length > 16) s = s.Substring(0, 16);
            return s;
        }
    }
}
