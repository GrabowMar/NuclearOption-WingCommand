using System;

namespace WingCommand
{
    /// <summary>The room's nine notches (spec WMC program §6): labels, keyboard navigation over the enabled ones, and a notch
    /// width that fits the room.</summary>
    internal static class RoomNotches
    {
        public const int Count = 9;
        public const int Tactical = 0, Packages = 1, Supply = 2, Loadout = 3, Squadron = 4, Studio = 5, Shapes = 6, Settings = 7, Debrief = 8;

        public static readonly string[] Labels =
            { "TACTICAL", "PACKAGES", "SUPPLY", "LOADOUT", "SQUADRON", "STUDIO", "SHAPES", "SETTINGS", "DEBRIEF" };

        /// <summary>What an unbuilt notch will hold (its disabled tooltip).</summary>
        public static readonly string[] Pending =
        {
            "The theatre map, elements and the deep member card.",
            "Packages and the timeline arrive in a later update.",
            "The shop (airframes, stock, deliveries) arrives in a later update.",
            "Loadout templates arrive in a later update.",
            "The squadron roster and records arrive in a later update.",
            "The pilot studio arrives in a later update.",
            "The formation shape editor arrives in a later update.",
            "Wing Command settings arrive in a later update.",
            "The after-action debrief arrives in a later update.",
        };

        private static readonly string[] keys = { "CTRL 1", "CTRL 2", "CTRL 3", "CTRL 4", "CTRL 5", "CTRL 6", "CTRL 7", "CTRL 8", "CTRL 9" };

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

        /// <summary>The notch for Ctrl+<paramref name="digit"/> (1-9), or -1.</summary>
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
