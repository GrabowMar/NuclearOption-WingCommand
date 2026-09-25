using System.Collections.Generic;
using System.Globalization;

namespace WingCommand
{
    /// <summary>SUPPLY's ADOPT row (critique C4: taking command of friendly AI selected on the map is ADOPT; RECRUIT means
    /// pilots): shown only when something can join, priced, and asking before it spends (the label ends in "?").</summary>
    internal static class AdoptRow
    {
        public const int NoteChars = 38;

        public static bool Visible(int n, bool client) => n > 0 && !client;

        public static string Label(int n, float price, bool asking) => "ADOPT " + N(n) + " · " + Credits.Price(price) + (asking ? "?" : "");

        public static string Ask(int n, float price) => "Adopt " + N(n) + " aircraft for " + Credits.Price(price) + "? Press ADOPT again";

        /// <summary>What the selection holds beyond those that can join, and the first reason why not.</summary>
        public static string Note(int skipped, string reason)
        {
            if (skipped <= 0) return "FRIENDLY AI SELECTED ON THE MAP";
            string head = "+" + N(skipped) + " CANNOT JOIN";
            return string.IsNullOrEmpty(reason) ? head : WmcText.Cut(head + " · " + reason.ToUpperInvariant(), NoteChars);
        }

        /// <summary>The confirm gate's target: the exact aircraft, so a changed selection asks again (built on press only).</summary>
        public static string Key(IReadOnlyList<uint> ids) => string.Join(",", ids);

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
