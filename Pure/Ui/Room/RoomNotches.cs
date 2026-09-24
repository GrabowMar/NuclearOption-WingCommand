using System;

namespace WingCommand
{
    /// <summary>The planning room's four notches (spec WMC rebuild §Big panel): PLAN · BEHAVIOUR · SQUADRON · WORKSHOP — labels,
    /// keyboard navigation over the enabled ones, and a notch width that fits the room.</summary>
    internal static class RoomNotches
    {
        public const int Count = 4;
        public const int Plan = 0, Behaviour = 1, Squadron = 2, Workshop = 3;

        public static readonly string[] Labels = { "PLAN", "BEHAVIOUR", "SQUADRON", "WORKSHOP" };

        /// <summary>What a notch holds (its tooltip; an unbuilt notch's disabled tooltip).</summary>
        public static readonly string[] Pending =
        {
            "The theatre map, elements, task plans and the deep aircraft card.",
            "Behaviour profiles, tuning and reaction rules. Arrives in a later update.",
            "Pilot records and the pilot studio. Arrives in a later update.",
            "The formation shape editor and Wing Command settings. Arrives in a later update.",
        };

        private static readonly string[] keys = { "CTRL 1", "CTRL 2", "CTRL 3", "CTRL 4" };

        public static string Key(int i) => i >= 0 && i < Count ? keys[i] : "";

        public static int Next(int current, int dir, bool[] enabled)
        {
            for (int step = 1; step <= Count; step++)
            {
                int i = ((current + dir * step) % Count + Count) % Count;
                if (enabled[i]) return i;
            }
            return current;
        }

        /// <summary>The notch for Ctrl+<paramref name="digit"/> (1-4), or -1.</summary>
        public static int ForDigit(int digit, bool[] enabled)
        {
            int i = digit - 1;
            return i >= 0 && i < Count && enabled[i] ? i : -1;
        }

        public static float Width(float available, int count, float max, float gap)
        {
            if (!(available > 0f) || count <= 0) return 0f;
            return Math.Min(max, (available - gap * (count - 1)) / count);
        }
    }
}
