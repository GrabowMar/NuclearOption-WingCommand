using System.Globalization;

namespace WingCommand
{
    /// <summary>SUPPLY step 1, PILOT &amp; CREW (spec WMC rebuild §SUPPLY): the card shows the pilot the next launch seats (the
    /// roster's Upcoming), the stepper walks the free pilots only, and with nobody free a new pilot is drafted at launch — never
    /// a blocker.</summary>
    internal static class PilotPick
    {
        public const int CallsignChars = 14, LineChars = 34;

        /// <summary>The card's place in the free list: −1 with nobody free, the first free pilot when the selection is not on it.</summary>
        public static int Index(int selected, int free) => free <= 0 ? -1 : selected < 0 || selected >= free ? 0 : selected;

        /// <summary>The next place, wrapping both ways (−1 with nobody free).</summary>
        public static int Step(int i, int n, int dir) => n <= 0 ? -1 : ((i + dir) % n + n) % n;

        public static string Counter(int i, int n) => n <= 0 || i < 0 ? WmcText.Unknown : N(i + 1) + "/" + N(n);

        /// <summary>The step header's chip.</summary>
        public static string State(int free) => free > 0 ? N(free) + " FREE" : "NEW PILOT";

        /// <summary>"HATCH · O. Bae": the callsign (≤ 14), then as much of the name as fits the line.</summary>
        public static string NameLine(string callsign, string name)
        {
            if (string.IsNullOrEmpty(callsign)) return "NEW PILOT";
            string c = WmcText.Cut(callsign, CallsignChars);
            if (string.IsNullOrEmpty(name)) return c;
            string n = WmcText.Cut(name, LineChars - c.Length - 3);
            return n.Length == 0 ? c : c + " · " + n;
        }

        public static string RankLine(string rank, int xp) =>
            WmcText.Cut(string.IsNullOrEmpty(rank) ? "ROOKIE" : rank.ToUpperInvariant(), 20) + " · XP " + N(xp);

        public static string Status(bool hasPilot) => hasPilot ? "READY" : "DRAFTED AT LAUNCH";

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
