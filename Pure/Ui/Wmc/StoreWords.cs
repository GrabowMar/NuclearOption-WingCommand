using System.Globalization;

namespace WingCommand
{
    /// <summary>A store's verdict in words (research loadout-rules §5): the popup's right-hand detail (≤ 15, 96 px at 10 px), the word a
    /// fitted row adds after its store when it will not fly ("Mk 82 ×4 · NOT YET"), the tooltip sentence, and the blocked station's
    /// line.</summary>
    internal static class StoreWords
    {
        public const int DetailChars = 15;

        public static string Detail(StoreVerdict v, StoreKind kind, int ammo, int rank)
        {
            if (v != StoreVerdict.Ok) return Short(v, rank);
            string word = Kind(kind), rounds = ammo > 0 ? "×" + N(ammo) : "";
            return word.Length == 0 ? rounds : rounds.Length == 0 ? word : word + " " + rounds;
        }

        public static string Short(StoreVerdict v, int rank)
        {
            switch (v)
            {
                case StoreVerdict.Missing: return "NOT INSTALLED";
                case StoreVerdict.NotOnStation: return "WRONG PYLON";
                case StoreVerdict.Disabled: return "SWITCHED OFF";
                case StoreVerdict.EventOnly: return "EVENT ONLY";
                case StoreVerdict.Restricted: return "RESTRICTED";
                case StoreVerdict.Blocked: return "BLOCKED";
                case StoreVerdict.NuclearNotYet: return "NOT YET";
                case StoreVerdict.NuclearRank: return "RANK " + N(rank);
                case StoreVerdict.NotFromShip: return "NOT FROM SHIP";
                case StoreVerdict.Warheads: return "WARHEADS";
                default: return "";
            }
        }

        public static string Why(StoreVerdict v, int rank)
        {
            switch (v)
            {
                case StoreVerdict.Missing: return "This build does not have that store; the station launches empty.";
                case StoreVerdict.NotOnStation: return "That store no longer fits this station; it launches empty.";
                case StoreVerdict.Disabled: return "The game has switched that store off.";
                case StoreVerdict.EventOnly: return "Event content: this mission does not allow it.";
                case StoreVerdict.Restricted: return "This mission restricts that store; the station launches empty.";
                case StoreVerdict.Blocked: return "Another station's store keeps this one empty.";
                case StoreVerdict.NuclearNotYet: return "Nuclear stores open with escalation; until then the station launches empty.";
                case StoreVerdict.NuclearRank: return "Nuclear stores need rank " + N(rank) + "; until then the station launches empty.";
                case StoreVerdict.NotFromShip: return "A ship-rearm store cannot launch from a carrier.";
                case StoreVerdict.Warheads: return "The field has too few warheads for this store.";
                default: return "";
            }
        }

        /// <summary>A fitted row's store: its name, and the verdict after it when it will not fly.</summary>
        public static string Row(string label, StoreVerdict v, int rank)
        {
            if (string.IsNullOrEmpty(label)) return "UNKNOWN STORE";
            if (v == StoreVerdict.Ok) return WmcText.Cut(label, LoadoutWords.StoreChars);
            string why = Short(v, rank);
            int room = LoadoutWords.StoreChars - 3 - why.Length;
            string name = label.Length <= room ? label : label.Substring(0, room).TrimEnd();
            return name + " · " + why;
        }

        public static string BlockedBy(string station) =>
            WmcText.Cut("BLOCKED BY " + (station ?? "").ToUpperInvariant(), LoadoutWords.StoreChars);

        public static string Kind(StoreKind kind)
        {
            switch (kind)
            {
                case StoreKind.AirToAir: return "A-A";
                case StoreKind.AirToGround: return "A-G";
                case StoreKind.Bomb: return "BOMB";
                case StoreKind.Ecm: return "ECM";
                case StoreKind.Cargo: return "CARGO";
                case StoreKind.MissileDefence: return "MSL DEF";
                default: return "";
            }
        }

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
