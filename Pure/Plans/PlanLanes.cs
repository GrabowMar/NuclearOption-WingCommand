using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Which aircraft a plan lane's step goes to and keeps (review 2 of the plan runner). A lane starts as its element and
    /// then follows the aircraft its steps went to; the element's letter is only a shortcut while it holds exactly those aircraft.</summary>
    internal static class PlanLanes
    {
        /// <summary>The step goes to element <paramref name="element"/> only while it is exactly the lane's live aircraft: a letter
        /// reused by other aircraft [1], or element A holding another lane's recovering jets [4], sends by id. A lane B-D merged into
        /// A, or whose element is gone, goes by id; one with nothing captured yet is its element.</summary>
        public static bool ByElement(int lane, int element, bool inUse, IReadOnlyList<uint> laneIds, IReadOnlyList<uint> elementIds)
        {
            if (!inUse || (element == 0 && lane != 0)) return false;
            if (laneIds.Count == 0) return true;
            if (laneIds.Count != elementIds.Count) return false;
            foreach (uint id in laneIds)
                if (!Contains(elementIds, id)) return false;
            return true;
        }

        /// <summary>After an accepted step, the lane takes element <paramref name="result"/>'s members as its aircraft — not after an
        /// RTB or REFIT (they may leave the element while they recover), not when the order named no element (an ATTACK [3]), and not
        /// for lanes B-D when the result is element A (a FORM UP merged it [2]).</summary>
        public static bool Recapture(int lane, int result, bool inUse, PlanKind kind) =>
            kind != PlanKind.Rtb && kind != PlanKind.Refit && result >= 0 && inUse && (lane == 0 || result != 0);

        /// <summary><paramref name="id"/> flies for another lane that still has steps (<paramref name="active"/>) [4].</summary>
        public static bool OwnedElsewhere(int lane, uint id, IReadOnlyList<uint>[] laneIds, bool[] active)
        {
            for (int l = 0; l < laneIds.Length; l++)
                if (l != lane && active[l] && Contains(laneIds[l], id)) return true;
            return false;
        }

        /// <summary>A player's order to aircraft <paramref name="id"/> (in element <paramref name="idElement"/>) reaches the lane: one of
        /// its aircraft, or — before the lane has captured any — one in its element [4].</summary>
        public static bool Reaches(IReadOnlyList<uint> laneIds, int laneElement, uint id, int idElement) =>
            laneIds.Count > 0 ? Contains(laneIds, id) : idElement == laneElement;

        private static bool Contains(IReadOnlyList<uint> ids, uint id)
        {
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] == id) return true;
            return false;
        }
    }
}
