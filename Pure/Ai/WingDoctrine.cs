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

        public WingDoctrine(MissileGuard guard, MissileResponse response, FormationInterval interval,
            bool spreadWhenThreatened, TargetPolicy targets, EngagementReach reach)
        {
            Guard = guard;
            Response = guard == MissileGuard.Off ? MissileResponse.Break : response;
            Interval = interval;
            SpreadWhenThreatened = spreadWhenThreatened;
            Targets = targets;
            Reach = reach;
        }

        public static WingDoctrine Reserve => new WingDoctrine(
            MissileGuard.Wing, MissileResponse.Press, FormationInterval.Close, true,
            TargetPolicy.Hold, EngagementReach.Slot);

        public static WingDoctrine Escort => new WingDoctrine(
            MissileGuard.Lead, MissileResponse.Break, FormationInterval.Standard, true,
            TargetPolicy.Cover, EngagementReach.Slot);

        public static WingDoctrine Sweep => new WingDoctrine(
            MissileGuard.Wing, MissileResponse.Break, FormationInterval.Open, true,
            TargetPolicy.Both, EngagementReach.Long);

        /// <summary>RESERVE, ESCORT, SWEEP, or CUSTOM.</summary>
        public string PatternName
        {
            get
            {
                if (Equals(Reserve)) return "RESERVE";
                if (Equals(Escort)) return "ESCORT";
                if (Equals(Sweep)) return "SWEEP";
                return "CUSTOM";
            }
        }

        /// <summary>Reserve, then Escort, then Sweep. Custom advances to Reserve.</summary>
        public WingDoctrine NextPattern()
        {
            if (Equals(Reserve)) return Escort;
            if (Equals(Escort)) return Sweep;
            return Reserve;
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

            string[] parts = trimmed.Split(',');
            if (parts.Length != 6) return false;
            if (!TryToken(parts[0], out MissileGuard guard)) return false;
            if (!TryToken(parts[1], out MissileResponse response)) return false;
            if (!TryInterval(parts[2], out FormationInterval interval)) return false;
            if (!TrySpread(parts[3], out bool spread)) return false;
            if (!TryToken(parts[4], out TargetPolicy targets)) return false;
            if (!TryToken(parts[5], out EngagementReach reach)) return false;
            value = new WingDoctrine(guard, response, interval, spread, targets, reach);
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
            return Guard + "," + Response + "," + Interval + "," + spread + "," + Targets + "," + Reach;
        }

        public bool Equals(WingDoctrine other) =>
            Guard == other.Guard && Response == other.Response && Interval == other.Interval &&
            SpreadWhenThreatened == other.SpreadWhenThreatened && Targets == other.Targets &&
            Reach == other.Reach;

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
