namespace WingCommand
{
    /// <summary>WMC states as stylesheet classes (spec WMC program §2): the rail carries the state, the level class the
    /// fuel/ammo band, and each has a word so colour is never the only carrier.</summary>
    internal static class WmcStyle
    {
        public const float Low = 0.35f, Critical = 0.15f;

        public static string Rail(string state)
        {
            switch (state)
            {
                case "FIGHT":
                case "DEFEND": return "danger";
                case "RTB":
                case "BEHIND":
                case "JOIN": return "armed";
                case "SLOT":
                case "HOLD":
                case "TRAIL": return "ready";
                case "GROUND":
                case "LANDED": return "locked";
                default: return "info";
            }
        }

        public static string Level(float fraction) =>
            float.IsNaN(fraction) ? "" : fraction < Critical ? "bad" : fraction < Low ? "warn" : "ok";

        /// <summary>Row fill keys (review P1 I1): hover lifts an unselected row; a selected row keeps its own fill under the
        /// pointer, and the selection also shows as a mark before the name, never by colour alone.</summary>
        public static string RowRest(bool selected) => selected ? "selected" : "rest";

        public static string RowHover(bool selected) => selected ? "selected" : "hover";

        public static string SelectedMark(bool selected) => selected ? "› " : "";

        public static string LevelWord(float fraction) =>
            float.IsNaN(fraction) ? "" : fraction < Critical ? "CRIT" : fraction < Low ? "LOW" : "";
    }
}
