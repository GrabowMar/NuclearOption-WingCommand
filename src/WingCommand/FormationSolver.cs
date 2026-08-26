using System.Collections.Generic;
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

                // Two elements rather than one line: lead plus wingman, then a second pair
                // stepped further out and further back. The classic fighter formation,
                // because every aircraft can see the others and nobody is directly astern.
                case FormationShape.FingerFour:
                    switch (slot)
                    {
                        case 1:  offset = right * spacing + fwd * (-spacing * 0.7f); break;
                        case 2:  offset = right * (-spacing * 1.2f) + fwd * (-spacing * 0.9f); break;
                        default: offset = right * (-spacing * 2.2f) + fwd * (-spacing * 1.7f); break;
                    }
                    break;

                // Symmetric V, wingmen splayed evenly either side and stepped back.
                case FormationShape.Vic:
                    offset = right * (side * spacing * pair) + fwd * (-spacing * pair);
                    break;

                // Two on the wings, one in the slot astern. A display formation: tight,
                // pretty, and completely impractical in a fight.
                case FormationShape.Diamond:
                    switch (slot)
                    {
                        case 1:  offset = right * spacing + fwd * (-spacing * 0.8f); break;
                        case 2:  offset = right * -spacing + fwd * (-spacing * 0.8f); break;
                        default: offset = fwd * (-spacing * 1.6f); break;
                    }
                    break;

                // Line astern with pronounced vertical separation, so each aircraft sits in
                // clear air above the one ahead rather than in its wake.
                case FormationShape.Ladder:
                    offset = fwd * (-spacing * slot) + Vector3.up * (spacing * 0.25f * slot);
                    break;

                // Line abreast at wide spacing — mutual support with room to manoeuvre.
                case FormationShape.Wall:
                    offset = right * (side * spacing * 2.2f * pair);
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

        /// <summary>
        /// The signed lateral component of a slot, in metres: positive to the leader's
        /// right, negative to its left.
        ///
        /// Turn compensation needs this separately from the full offset. In a turn a
        /// formation flies concentric arcs about a common centre, so a wingman on the
        /// outside must cover more ground than the leader and one on the inside less. The
        /// correction is proportional to how far off the centreline the slot sits.
        /// </summary>
        public static float SlotLateral(int slot, FormationShape shape, float spacing)
        {
            if (slot <= 0) return 0f;

            int pair = (slot + 1) / 2;
            float side = (slot % 2 == 1) ? 1f : -1f;

            switch (shape)
            {
                case FormationShape.EchelonLeft:  return -spacing * slot;
                case FormationShape.LineAbreast:  return side * spacing * pair;
                case FormationShape.Trail:        return 0f;
                case FormationShape.Ladder:       return 0f;
                case FormationShape.CombatSpread: return side * spacing * 1.5f * pair;
                case FormationShape.Vic:          return side * spacing * pair;
                case FormationShape.Wall:         return side * spacing * 2.2f * pair;

                case FormationShape.FingerFour:
                    switch (slot)
                    {
                        case 1:  return spacing;
                        case 2:  return -spacing * 1.2f;
                        default: return -spacing * 2.2f;
                    }

                case FormationShape.Diamond:
                    switch (slot)
                    {
                        case 1:  return spacing;
                        case 2:  return -spacing;
                        default: return 0f;
                    }

                case FormationShape.EchelonRight:
                default:                          return spacing * slot;
            }
        }

        /// <summary>
        /// Reynolds' leader-following keep-out: steer clear of the airspace directly ahead
        /// of the leader.
        ///
        /// A wingman rejoining from in front converges on a slot that lies behind the
        /// leader, and the straight path to it goes through the leader. Nothing else in the
        /// controller prevents that, so this is what stops mid-airs on rejoin.
        ///
        /// Returns a lateral push, or zero when the wingman is not in the way.
        /// </summary>
        /// <param name="lookAhead">Length of the protected corridor ahead of the leader.</param>
        /// <param name="corridorRadius">Half-width of that corridor.</param>
        public static Vector3 AvoidLeaderPath(Aircraft self, Aircraft leader,
                                              float lookAhead, float corridorRadius, float strength)
        {
            if (self == null || leader == null) return Vector3.zero;

            Vector3 forward = leader.transform.forward;
            Vector3 toSelf = self.transform.position - leader.transform.position;

            // Only the corridor *ahead* of the leader matters; behind is where slots live.
            float ahead = Vector3.Dot(toSelf, forward);
            if (ahead <= 0f || ahead > lookAhead) return Vector3.zero;

            Vector3 lateral = toSelf - forward * ahead;
            float offCentre = lateral.magnitude;
            if (offCentre > corridorRadius) return Vector3.zero;

            // Push sideways out of the corridor, hardest on the centreline and closest in.
            Vector3 escape = offCentre > 0.1f
                ? lateral / offCentre
                : Vector3.Cross(forward, Vector3.up).normalized;

            float urgency = (1f - offCentre / corridorRadius) * (1f - ahead / lookAhead);
            return escape * (strength * urgency);
        }

        /// <summary>
        /// Reynolds separation: a repulsion vector pushing an aircraft away from nearby
        /// wing members, weighted by inverse square distance.
        ///
        /// Slots are far enough apart on paper, but during a rejoin several wingmen
        /// converge on the leader from arbitrary angles and nothing else keeps them out of
        /// one another's way.
        /// </summary>
        public static Vector3 Separation(Aircraft self, IReadOnlyList<WingMember> members,
                                         float radius, float strength)
        {
            if (self == null || members == null || radius <= 0f) return Vector3.zero;

            Vector3 push = Vector3.zero;
            float radiusSq = radius * radius;
            Vector3 selfPos = self.transform.position;

            for (int i = 0; i < members.Count; i++)
            {
                Aircraft other = members[i].Aircraft;
                if (other == null || other == self || other.disabled) continue;

                Vector3 away = selfPos - other.transform.position;
                float distSq = away.sqrMagnitude;
                if (distSq > radiusSq || distSq < 1f) continue;

                // Inverse square, so the push only grows sharply when genuinely close.
                push += away.normalized * (radiusSq / distSq);
            }

            return push * strength;
        }
    }
}
