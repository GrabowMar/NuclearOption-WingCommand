using System.Globalization;

namespace WingCommand
{
    /// <summary>The WING tab's element blocks (spec WMC program §4): rows in element order A–D, seats ascending inside each,
    /// and each block's header words.</summary>
    internal static class ElementGroups
    {
        /// <summary>Row indices sorted by element, then seat; returns how many.</summary>
        public static int Order(SnapshotMember[] rows, int count, int[] into)
        {
            for (int i = 0; i < count; i++) into[i] = i;
            // Insertion sort: stable, allocation-free, eight rows at most.
            for (int i = 1; i < count; i++)
            {
                int k = into[i], j = i - 1;
                while (j >= 0 && Before(rows[k], rows[into[j]]))
                {
                    into[j + 1] = into[j];
                    j--;
                }
                into[j + 1] = k;
            }
            return count;
        }

        private static bool Before(in SnapshotMember a, in SnapshotMember b) =>
            a.Element != b.Element ? a.Element < b.Element : a.Slot < b.Slot;

        /// <summary>"B · COBRA · 2 · ORBIT"; an element without its own name shows its letter once.</summary>
        public static string Header(int element, string name, int members, string task)
        {
            string letter = ElementRoster.Letter(element);
            string n = members.ToString(CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(name) || name == letter
                ? letter + " · " + n + " · " + task
                : letter + " · " + name + " · " + n + " · " + task;
        }
    }
}
