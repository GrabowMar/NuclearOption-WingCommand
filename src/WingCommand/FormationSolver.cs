using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Pure geometry: turns a leader's position/heading plus a slot index into a world
    /// position. Kept free of game types so the shapes can be reasoned about (and changed)
    /// without touching flight logic.
    /// </summary>
    internal static class FormationSolver
    {
        /// <param name="leaderPos">Leader position in world space.</param>
        /// <param name="leaderForward">Leader forward vector; flattened internally.</param>
        /// <param name="slot">1-based slot index. Slot 0 is the leader itself.</param>
        public static Vector3 SlotOffset(
            Vector3 leaderForward, int slot, FormationShape shape, float spacing, float stack)
        {
            if (slot <= 0) return Vector3.zero;

            Vector3 fwd = new Vector3(leaderForward.x, 0f, leaderForward.z);
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();

            // Right-hand perpendicular in the horizontal plane.
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);

            // Alternating shapes place odd slots right, even slots left.
            int pair = (slot + 1) / 2;          // 1,1,2,2,3,3...
            float side = (slot % 2 == 1) ? 1f : -1f;

            Vector3 offset;
            switch (shape)
            {
                case FormationShape.EchelonLeft:
                    offset = right * (-spacing * slot) + fwd * (-spacing * 0.6f * slot);
                    break;

                case FormationShape.LineAbreast:
                    offset = right * (side * spacing * pair);
                    break;

                case FormationShape.Trail:
                    offset = fwd * (-spacing * slot);
                    break;

                case FormationShape.CombatSpread:
                    offset = right * (side * spacing * 1.5f * pair) + fwd * (-spacing * 0.25f * pair);
                    break;

                case FormationShape.EchelonRight:
                default:
                    offset = right * (spacing * slot) + fwd * (-spacing * 0.6f * slot);
                    break;
            }

            // Vertical stagger keeps wingmen out of the leader's wake and out of each other.
            float vertical = (shape == FormationShape.LineAbreast || shape == FormationShape.CombatSpread)
                ? stack * side * pair
                : stack * slot;

            offset.y += vertical;
            return offset;
        }
    }
}
