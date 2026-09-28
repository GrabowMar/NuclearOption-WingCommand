using System.Text;

namespace WingCommand
{
    /// <summary>The words of a TACTICAL element card (spec WMC program §6): "B · VIPER · PATROL · 2" and the members' numbers.
    /// Element A flies on the player, so its name reads WING.</summary>
    internal static class ElementCard
    {
        public static string Header(int element, string name, string taskWord, int members)
        {
            string letter = ElementRoster.Letter(element);
            string shown = element == 0 ? "WING" : string.IsNullOrEmpty(name) ? letter : name;
            return letter + " · " + shown + " · " + (string.IsNullOrEmpty(taskWord) ? WmcText.Unknown : taskWord) + " · " + members;
        }

        public static string Members(SnapshotMember[] rows, int count, int element)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < count && rows != null && i < rows.Length; i++)
            {
                if (rows[i].Element != element) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(WingRows.Number(rows[i].Slot));
            }
            return sb.Length > 0 ? sb.ToString() : WmcText.Unknown;
        }
    }
}
