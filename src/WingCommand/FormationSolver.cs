using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Pure geometry: turns a leader's position/heading plus a slot index into a world
    /// position. Kept free of game types so the shapes can be reasoned about (and changed)
    /// without touching flight logic.
    ///
    /// Every shape is a <see cref="SlotShape"/> in two independent units: lateral and
    /// back are multiples of the horizontal slot spacing, height is a multiple of the
    /// vertical stack. The world offset multiplies those once at the end. One place
    /// describes a shape, so <see cref="SlotOffset"/> and <see cref="SlotLateral"/> can no
    /// longer drift apart, and no shape hard-codes a vertical offset out of the horizontal
    /// spacing — which is what made Ladder climb twice as fast as it should.
    /// </summary>
    internal static class FormationSolver
    {
        /// <summary>How many vertical stacks each Ladder rung steps up. The vertical step is the point of the shape.</summary>
        private const float LadderHeight = 2f;

        /// <summary>
        /// A slot's position relative to the leader in formation units. <see cref="Lateral"/>
        /// is positive to the leader's right, <see cref="Back"/> is positive behind it, and
        /// <see cref="Height"/> is positive up. Lateral and back are spacing units, height is
        /// stack units.
        /// </summary>
        private struct SlotShape
        {
            public readonly float Lateral;
            public readonly float Back;
            public readonly float Height;

            public SlotShape(float lateral, float back, float height)
            {
                Lateral = lateral;
                Back = back;
                Height = height;
            }
        }

        /// <summary>The shape of one slot. The single source of truth for a formation's geometry.</summary>
        private static SlotShape Shape(FormationShape shape, int slot)
        {
            int pair = (slot + 1) / 2;               // 1,1,2,2,3,3...
            float side = (slot % 2 == 1) ? 1f : -1f; // odd slots right, even slots left

            switch (shape)
            {
                case FormationShape.EchelonLeft:
                    return new SlotShape(-slot, 0.6f * slot, slot);

                case FormationShape.LineAbreast:
                    return new SlotShape(side * pair, 0f, 0f);

                case FormationShape.Trail:
                    return new SlotShape(0f, slot, slot);

                // Line astern with the vertical step the defining feature: each rung sits in
                // clear air above the one ahead instead of in its wake.
                case FormationShape.Ladder:
                    return new SlotShape(0f, slot, LadderHeight * slot);

                case FormationShape.CombatSpread:
                    return new SlotShape(side * 1.5f * pair, 0.25f * pair, 0f);

                // Symmetric V: wingmen splayed evenly either side and stepped back. Both
                // members of a pair share the same altitude, so the rung sits level.
                case FormationShape.Vic:
                    return new SlotShape(side * pair, pair, pair);

                case FormationShape.Wall:
                    return new SlotShape(side * 2.2f * pair, 0f, 0f);

                case FormationShape.FingerFour:
                    return FingerFour(slot);

                case FormationShape.Diamond:
                    return Diamond(slot);

                case FormationShape.EchelonRight:
                default:
                    return new SlotShape(slot, 0.6f * slot, slot);
            }
        }

        /// <summary>
        /// Finger four: lead plus right and left wingmen, then an element lead astern. The
        /// three wingmen are fixed; any beyond that fall into a second element behind, so a
        /// larger wing still has somewhere sensible to sit rather than stacking on a slot.
        /// </summary>
        private static SlotShape FingerFour(int slot)
        {
            switch (slot)
            {
                case 1: return new SlotShape(1f, 0.7f, 0f);      // right wing
                case 2: return new SlotShape(-1.2f, 0.9f, 0f);  // left wing
                case 3: return new SlotShape(0f, 2f, 1f);       // element lead astern
                case 4: return new SlotShape(1.2f, 2.7f, 1f);   // second element, right
                case 5: return new SlotShape(-1.2f, 2.9f, 1f);  // second element, left
                default: return new SlotShape(0f, 2f + (slot - 3) * 1.2f, slot - 2); // trail out
            }
        }

        /// <summary>Diamond: two on the wings, one in the slot astern.</summary>
        private static SlotShape Diamond(int slot)
        {
            switch (slot)
            {
                case 1: return new SlotShape(1f, 0.8f, 0f);      // right wing
                case 2: return new SlotShape(-1f, 0.8f, 0f);     // left wing
                case 3: return new SlotShape(0f, 1.6f, 1f);      // tail astern
                default: return new SlotShape(0f, 1.6f + (slot - 3) * 1.2f, slot - 2); // trail out
            }
        }

        /// <param name="leaderForward">Leader forward vector; flattened internally.</param>
        /// <param name="slot">1-based slot index. Slot 0 is the leader itself.</param>
        public static Vector3 SlotOffset(
            Vector3 leaderForward, int slot, FormationShape shape, float spacing, float stack)
        {
            if (slot <= 0) return Vector3.zero;

            Vector3 fwd = new Vector3(leaderForward.x, 0f, leaderForward.z);
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            SlotShape s = Shape(shape, slot);

            return right * (s.Lateral * spacing)
                 - fwd * (s.Back * spacing)
                 + Vector3.up * (s.Height * stack);
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
            return Shape(shape, slot).Lateral * spacing;
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
