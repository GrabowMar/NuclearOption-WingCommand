using System;

namespace WingCommand
{
    /// <summary>
    /// Pure selection rules for cycling and automatically advancing pilots in the squadron roster.
    /// </summary>
    public static class PilotSelectionPolicy
    {
        /// <summary>
        /// Determine the next pilot index to select, preferring the next candidate that is free/available.
        /// If no candidates are free, advances cyclically by one (startIndex + 1) % totalCount.
        /// </summary>
        public static int NextIndex(int startIndex, int totalCount, Func<int, bool> isFree)
        {
            if (totalCount <= 0) return -1;
            if (startIndex < 0 || startIndex >= totalCount) startIndex = 0;

            for (int i = 1; i <= totalCount; i++)
            {
                int candidate = (startIndex + i) % totalCount;
                if (isFree != null && isFree(candidate))
                {
                    return candidate;
                }
            }

            return (startIndex + 1) % totalCount;
        }

        /// <summary>
        /// Step manually in a direction (e.g. -1 for previous, +1 for next), wrapping around.
        /// </summary>
        public static int CycleIndex(int currentIndex, int totalCount, int direction)
        {
            if (totalCount <= 0) return -1;
            if (currentIndex < 0) currentIndex = 0;
            return ((currentIndex + direction) % totalCount + totalCount) % totalCount;
        }
    }
}
