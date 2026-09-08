using System;

namespace WingCommand
{
    /// <summary>Engine-free flight and roster toggle rules.</summary>
    internal static class SelectionTogglePolicy
    {
        public static bool ShouldDeselectAll(bool isAllMode, int selectedCount, int totalCount)
        {
            if (isAllMode) return true;
            return totalCount > 0 && selectedCount >= totalCount;
        }

        public static bool ShouldDeselectMemberOnClick(bool isExplicitMode, int selectedCount, bool isMemberSelected)
        {
            return isExplicitMode && selectedCount == 1 && isMemberSelected;
        }
    }

    /// <summary>Keep tactical member interaction independent of weapon-target selection.</summary>
    internal static class MapSelectionPolicy
    {
        public static bool DeferToMouseClick(bool controllerSource, bool mouseGestureActive,
                                            bool pointerOverIcon)
        {
            return controllerSource && mouseGestureActive && pointerOverIcon;
        }

        public static bool IconReceivesPointer(bool isPlayerAircraft, bool nativeSelected,
                                               bool isWingMember, bool tacticalCommandsActive)
        {
            if (isPlayerAircraft) return false;
            return !nativeSelected || (isWingMember && tacticalCommandsActive);
        }
    }

    /// <summary>Roster pilot cycling and automatic selection advancement.</summary>
    public static class PilotSelectionPolicy
    {
        /// <summary>Choose the next free index with wraparound; if none are free, advance one index
        /// cyclically.</summary>
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

        /// <summary>Step selection in either direction with wraparound.</summary>
        public static int CycleIndex(int currentIndex, int totalCount, int direction)
        {
            if (totalCount <= 0) return -1;
            if (currentIndex < 0) currentIndex = 0;
            return ((currentIndex + direction) % totalCount + totalCount) % totalCount;
        }
    }
}
