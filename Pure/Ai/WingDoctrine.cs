using System;

namespace WingCommand
{
    /// <summary>Who an anti-missile store may protect. Off still breaks and expends countermeasures.</summary>
    public enum MissileGuard
    {
        Off,
        Self,
        Wing,
        Lead,
    }

    /// <summary>Press holds heading to kill a SARH missile. Break notches, beams, or flares.</summary>
    public enum MissileResponse
    {
        Break,
        Press,
    }

    /// <summary>Slot interval. Close also sticks to the slot and keeps the assigned echelon side.</summary>
    public enum FormationInterval
    {
        Close,
        Standard,
        Open,
    }

    /// <summary>Standing targets while the aircraft is holding a slot, orbit, or patrol.</summary>
    public enum TargetPolicy
    {
        Hold,
        Air,
        Ground,
        Both,
        Cover,
    }

    /// <summary>Standing weapon range. Explicit Attack and Splash always use the long range.</summary>
    public enum EngagementReach
    {
        Slot,
        Long,
    }

    /// <summary>Allowed standing target class. Cover does not use this; it picks one aircraft threat.</summary>
    public enum DoctrineAllow
    {
        None,
        AirOnly,
        GroundOnly,
        AirAndGround,
    }

    /// <summary>Which weapons a member may use (spec WMC rebuild R3): NoAirToGround keeps every store but air targets only.
    /// Jammers are always allowed; SARH missiles only while the member's radar is wanted on.</summary>
    public enum WeaponsPolicy
    {
        Auto,
        Missiles,
        Guns,
        NoAirToGround,
    }

    /// <summary>The member's radar: on, silent until engaged, or off (spec WMC rebuild R3).</summary>
    public enum RadarPolicy
    {
        On,
        Silent,
        Off,
    }

    /// <summary>One doctrine setting. Targets, Reach, Weapons and Radar may differ per aircraft
    /// (<see cref="MemberDoctrines"/>); the others are element-wide.</summary>
    internal enum DoctrineAxis : byte { Guard, Response, Interval, Spread, Targets, Reach, Weapons, Radar }

    /// <summary>Order in which a missile guard looks for a warned aircraft.</summary>
    public enum ProtecteeRank
    {
        Self,
        Leader,
        Wingman,
    }

    /// <summary>Wing-wide standing behaviour. Orders and weapon preference stay on each aircraft.</summary>
    public readonly struct WingDoctrine : IEquatable<WingDoctrine>
    {
        public readonly MissileGuard Guard;
        public readonly MissileResponse Response;
        public readonly FormationInterval Interval;
        public readonly bool SpreadWhenThreatened;
        public readonly TargetPolicy Targets;
        public readonly EngagementReach Reach;
        public readonly WeaponsPolicy Weapons;
        public readonly RadarPolicy Radar;

        /// <summary>Every axis, always (R3 research: an optional weapons or radar argument would let a rebuild from the six
        /// old axes silently reset them).</summary>
        public WingDoctrine(MissileGuard guard, MissileResponse response, FormationInterval interval,
            bool spreadWhenThreatened, TargetPolicy targets, EngagementReach reach, WeaponsPolicy weapons, RadarPolicy radar)
        {
            Guard = guard;
            Response = guard == MissileGuard.Off ? MissileResponse.Break : response;
            Interval = interval;
            SpreadWhenThreatened = spreadWhenThreatened;
            Targets = targets;
            Reach = reach;
            Weapons = weapons;
            Radar = radar;
        }

        public static WingDoctrine Reserve => new WingDoctrine(
            MissileGuard.Wing, MissileResponse.Press, FormationInterval.Close, true,
            TargetPolicy.Hold, EngagementReach.Slot, WeaponsPolicy.Auto, RadarPolicy.On);

        public static WingDoctrine Escort => new WingDoctrine(
            MissileGuard.Lead, MissileResponse.Break, FormationInterval.Standard, true,
            TargetPolicy.Cover, EngagementReach.Slot, WeaponsPolicy.Auto, RadarPolicy.On);

        public static WingDoctrine Sweep => new WingDoctrine(
            MissileGuard.Wing, MissileResponse.Break, FormationInterval.Open, true,
            TargetPolicy.Both, EngagementReach.Long, WeaponsPolicy.Auto, RadarPolicy.On);

        /// <summary>RESERVE, ESCORT, SWEEP, or CUSTOM, from the six profile axes (a silent SWEEP is still SWEEP).</summary>
        public string PatternName
        {
            get
            {
                if (SameProfile(Reserve)) return "RESERVE";
                if (SameProfile(Escort)) return "ESCORT";
                if (SameProfile(Sweep)) return "SWEEP";
                return "CUSTOM";
            }
        }

        /// <summary>Reserve, then Escort, then Sweep. Custom advances to Reserve. A preset clears weapons and radar.</summary>
        public WingDoctrine NextPattern()
        {
            if (SameProfile(Reserve)) return Escort;
            if (SameProfile(Escort)) return Sweep;
            return Reserve;
        }

        private bool SameProfile(WingDoctrine p) =>
            Guard == p.Guard && Response == p.Response && Interval == p.Interval &&
            SpreadWhenThreatened == p.SpreadWhenThreatened && Targets == p.Targets && Reach == p.Reach;

        /// <summary><paramref name="axis"/>'s value as a byte (Spread: 1 spread, 0 not).</summary>
        internal byte Get(DoctrineAxis axis)
        {
            switch (axis)
            {
                case DoctrineAxis.Guard: return (byte)Guard;
                case DoctrineAxis.Response: return (byte)Response;
                case DoctrineAxis.Interval: return (byte)Interval;
                case DoctrineAxis.Spread: return (byte)(SpreadWhenThreatened ? 1 : 0);
                case DoctrineAxis.Targets: return (byte)Targets;
                case DoctrineAxis.Reach: return (byte)Reach;
                case DoctrineAxis.Weapons: return (byte)Weapons;
                default: return (byte)Radar;
            }
        }

        /// <summary>This doctrine with one axis changed.</summary>
        internal WingDoctrine With(DoctrineAxis axis, byte value) => new WingDoctrine(
            axis == DoctrineAxis.Guard ? (MissileGuard)value : Guard,
            axis == DoctrineAxis.Response ? (MissileResponse)value : Response,
            axis == DoctrineAxis.Interval ? (FormationInterval)value : Interval,
            axis == DoctrineAxis.Spread ? value != 0 : SpreadWhenThreatened,
            axis == DoctrineAxis.Targets ? (TargetPolicy)value : Targets,
            axis == DoctrineAxis.Reach ? (EngagementReach)value : Reach,
            axis == DoctrineAxis.Weapons ? (WeaponsPolicy)value : Weapons,
            axis == DoctrineAxis.Radar ? (RadarPolicy)value : Radar);

        /// <summary>The word an order carries for <paramref name="axis"/>'s value <paramref name="v"/> (null: no such
        /// value).</summary>
        internal static string ValueName(DoctrineAxis axis, byte v)
        {
            switch (axis)
            {
                case DoctrineAxis.Guard: return Name((MissileGuard)v);
                case DoctrineAxis.Response: return Name((MissileResponse)v);
                case DoctrineAxis.Interval: return Name((FormationInterval)v);
                case DoctrineAxis.Spread: return v == 0 ? "NoSpread" : v == 1 ? "Spread" : null;
                case DoctrineAxis.Targets: return Name((TargetPolicy)v);
                case DoctrineAxis.Reach: return Name((EngagementReach)v);
                case DoctrineAxis.Weapons: return Name((WeaponsPolicy)v);
                default: return Name((RadarPolicy)v);
            }
        }

        private static string Name<T>(T value) where T : struct => Enum.IsDefined(typeof(T), value) ? value.ToString() : null;

        /// <summary>A value word of <paramref name="axis"/> ("Silent", "NoSpread", "Std"); numbers and other axes' words
        /// are not.</summary>
        internal static bool TryAxisValue(DoctrineAxis axis, string text, out byte value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            bool ok;
            switch (axis)
            {
                case DoctrineAxis.Guard: ok = TryName(text, out MissileGuard g); value = (byte)g; break;
                case DoctrineAxis.Response: ok = TryName(text, out MissileResponse r); value = (byte)r; break;
                case DoctrineAxis.Interval: ok = TryInterval(text, out FormationInterval i) && Word(text); value = (byte)i; break;
                case DoctrineAxis.Spread: ok = TrySpread(text, out bool s); value = (byte)(s ? 1 : 0); break;
                case DoctrineAxis.Targets: ok = TryName(text, out TargetPolicy t); value = (byte)t; break;
                case DoctrineAxis.Reach: ok = TryName(text, out EngagementReach e); value = (byte)e; break;
                case DoctrineAxis.Weapons: ok = TryName(text, out WeaponsPolicy w); value = (byte)w; break;
                default: ok = TryName(text, out RadarPolicy p); value = (byte)p; break;
            }
            return ok;
        }

        /// <summary>A defined name of <typeparamref name="T"/> only (Enum.TryParse also takes any number).</summary>
        private static bool TryName<T>(string text, out T value) where T : struct =>
            TryToken(text, out value) && Word(text) && Enum.IsDefined(typeof(T), value);

        private static bool Word(string text)
        {
            string t = text.Trim();
            return t.Length > 0 && char.IsLetter(t[0]);
        }

        public static WingDoctrine FromLegacy(string roeName)
        {
            if (string.IsNullOrWhiteSpace(roeName)) return Reserve;
            if (roeName.Equals("Free", StringComparison.OrdinalIgnoreCase)) return Sweep;
            if (roeName.Equals("Tight", StringComparison.OrdinalIgnoreCase) ||
                roeName.Equals("Escort", StringComparison.OrdinalIgnoreCase))
                return Escort;
            return Reserve;
        }

        public static bool TryParse(string text, out WingDoctrine value)
        {
            value = Reserve;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string trimmed = text.Trim();
            if (trimmed.Equals("Reserve", StringComparison.OrdinalIgnoreCase))
            {
                value = Reserve;
                return true;
            }
            if (trimmed.Equals("Escort", StringComparison.OrdinalIgnoreCase))
            {
                value = Escort;
                return true;
            }
            if (trimmed.Equals("Sweep", StringComparison.OrdinalIgnoreCase))
            {
                value = Sweep;
                return true;
            }

            // Six values (0.9 and M7b lines: weapons and radar default) or eight.
            string[] parts = trimmed.Split(',');
            if (parts.Length != 6 && parts.Length != 8) return false;
            if (!TryToken(parts[0], out MissileGuard guard)) return false;
            if (!TryToken(parts[1], out MissileResponse response)) return false;
            if (!TryInterval(parts[2], out FormationInterval interval)) return false;
            if (!TrySpread(parts[3], out bool spread)) return false;
            if (!TryToken(parts[4], out TargetPolicy targets)) return false;
            if (!TryToken(parts[5], out EngagementReach reach)) return false;
            WeaponsPolicy weapons = WeaponsPolicy.Auto;
            RadarPolicy radar = RadarPolicy.On;
            if (parts.Length == 8 && (!TryName(parts[6], out weapons) || !TryName(parts[7], out radar))) return false;
            value = new WingDoctrine(guard, response, interval, spread, targets, reach, weapons, radar);
            return true;
        }

        /// <summary>A Doctrine line wins. Otherwise DefaultRoe Hold, Tight, Free, or Escort.</summary>
        public static WingDoctrine FromConfigText(string text)
        {
            string doctrine = null;
            string legacy = null;
            if (!string.IsNullOrEmpty(text))
            {
                string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string raw = line.Substring(eq + 1).Trim();
                    if (key.Equals("Doctrine", StringComparison.OrdinalIgnoreCase)) doctrine = raw;
                    else if (key.Equals("DefaultRoe", StringComparison.OrdinalIgnoreCase)) legacy = raw;
                }
            }

            if (!string.IsNullOrEmpty(doctrine) && TryParse(doctrine, out WingDoctrine parsed))
                return parsed;
            return FromLegacy(legacy);
        }

        public override string ToString()
        {
            if (Equals(Reserve)) return "Reserve";
            if (Equals(Escort)) return "Escort";
            if (Equals(Sweep)) return "Sweep";
            string spread = SpreadWhenThreatened ? "Spread" : "NoSpread";
            return Guard + "," + Response + "," + Interval + "," + spread + "," + Targets + "," + Reach + "," + Weapons + "," + Radar;
        }

        public bool Equals(WingDoctrine other) =>
            SameProfile(other) && Weapons == other.Weapons && Radar == other.Radar;

        public override bool Equals(object obj) => obj is WingDoctrine other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Guard;
                hash = (hash * 397) ^ (int)Response;
                hash = (hash * 397) ^ (int)Interval;
                hash = (hash * 397) ^ (SpreadWhenThreatened ? 1 : 0);
                hash = (hash * 397) ^ (int)Targets;
                hash = (hash * 397) ^ (int)Reach;
                hash = (hash * 397) ^ (int)Weapons;
                hash = (hash * 397) ^ (int)Radar;
                return hash;
            }
        }

        public static bool operator ==(WingDoctrine left, WingDoctrine right) => left.Equals(right);
        public static bool operator !=(WingDoctrine left, WingDoctrine right) => !left.Equals(right);

        private static bool TryToken<T>(string text, out T value) where T : struct
        {
            return Enum.TryParse(text.Trim(), ignoreCase: true, out value);
        }

        private static bool TryInterval(string text, out FormationInterval interval)
        {
            string token = text.Trim();
            if (token.Equals("Std", StringComparison.OrdinalIgnoreCase))
            {
                interval = FormationInterval.Standard;
                return true;
            }
            return TryToken(token, out interval);
        }

        private static bool TrySpread(string text, out bool spread)
        {
            string token = text.Trim();
            if (token.Equals("Spread", StringComparison.OrdinalIgnoreCase))
            {
                spread = true;
                return true;
            }
            if (token.Equals("NoSpread", StringComparison.OrdinalIgnoreCase))
            {
                spread = false;
                return true;
            }
            spread = false;
            return false;
        }
    }
}
