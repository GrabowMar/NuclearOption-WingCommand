using System.Globalization;

namespace WingCommand
{
    /// <summary>What a field launch answers (spec M3 §3; WMC rebuild §SUPPLY): the type, the field and the pilot, and why the
    /// rest did not go when the launch was cut short.</summary>
    internal static class CallWords
    {
        public static string Launched(int n, string type, string field, string pilot, string cutShort) =>
            (n == 1 ? type : n.ToString(CultureInfo.InvariantCulture) + " × " + type) + " launching from " + field
            + (n == 1 && !string.IsNullOrEmpty(pilot) ? " · " + pilot : "")
            + (string.IsNullOrEmpty(cutShort) ? "" : " (then: " + cutShort + ")");

        public static string Refused(string type, string reason, string field = null) =>
            !string.IsNullOrEmpty(reason) ? "Cannot call " + type + ": " + reason : field + " could not launch " + type;
    }
}
