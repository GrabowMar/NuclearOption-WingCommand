using System.Globalization;

namespace WingCommand
{
    /// <summary>INSPECT's words (spec bezel v2 §5 INSPECT): the aircraft's title, its pilot line, the member chips and the line a
    /// page with nothing to inspect shows.</summary>
    internal static class InspectWords
    {
        public static string Chip(int slot) => WingRows.Number(slot);

        public static string Title(int slot, string callsign, string type)
        {
            string s = WingRows.Number(slot);
            if (!string.IsNullOrEmpty(callsign)) s += " " + callsign;
            if (!string.IsNullOrEmpty(type)) s += " · " + type;
            return s;
        }

        public static string Pilot(string callsign, string rank, int xp, int kills, string perks)
        {
            if (string.IsNullOrEmpty(callsign)) return "NO PILOT RECORD";
            string s = callsign + " · " + (rank ?? WmcText.Unknown) + " · XP " + xp.ToString(CultureInfo.InvariantCulture) + " · "
                + kills.ToString(CultureInfo.InvariantCulture) + (kills == 1 ? " KILL" : " KILLS");
            return string.IsNullOrEmpty(perks) ? s : s + " · " + perks;
        }

        public static string Empty(bool client) => client
            ? "Wingman details come from the host."
            : "NO AIRCRAFT · INSPECT › ON TACTICAL, OR A MEMBER CHIP ABOVE";
    }
}
