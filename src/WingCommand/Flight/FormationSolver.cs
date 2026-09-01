using System.Collections.Generic;
using System.Text;
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
        /// Cheap startup invariant check for every shape and supported slot. Geometry errors
        /// otherwise appear only in flight as two aircraft assigned the same piece of sky.
        /// </summary>
        public static bool ValidateGeometry(int maxSlots, out string problem)
        {
            var report = new StringBuilder();

            foreach (FormationShape shape in FormationShapes.All)
            {
                var slots = new List<Vector3>();
                for (int slot = 1; slot <= maxSlots; slot++)
                {
                    Vector3 point = SlotCoordinates(slot, shape, 1f, 1f);
                    if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) ||
                        float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z))
                    {
                        report.Append(shape).Append(" slot ").Append(slot).Append(" is not finite; ");
                        continue;
                    }

                    for (int previous = 0; previous < slots.Count; previous++)
                    {
                        if ((slots[previous] - point).sqrMagnitude >= 0.2f * 0.2f) continue;
                        report.Append(shape).Append(" slots ").Append(previous + 1)
                              .Append(" and ").Append(slot).Append(" overlap; ");
                    }
                    slots.Add(point);
                }
            }

            problem = report.ToString();
            return problem.Length == 0;
        }

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
            float elementStep = (pair - 1) * 0.25f;

            switch (shape)
            {
                case FormationShape.EchelonLeft:
                    return new SlotShape(-slot, 0.72f * slot, (slot - 1) * 0.25f);

                case FormationShape.LineAbreast:
                    // A small aft step keeps a many-ship line from looking ruler-perfect and
                    // prevents every pair trying to cross the exact same plane during rejoin.
                    return new SlotShape(side * pair, 0.12f * (pair - 1), elementStep);

                case FormationShape.Trail:
                    // Alternating stack keeps each aircraft out of the wake of the one ahead
                    // without turning a long trail into a staircase hundreds of metres tall.
                    return new SlotShape(0f, 0.9f * slot,
                        (slot % 2 == 1 ? 0.35f : -0.35f) + (slot - 1) * 0.08f);

                // Line astern with the vertical step the defining feature: each rung sits in
                // clear air above the one ahead instead of in its wake.
                case FormationShape.Ladder:
                    return new SlotShape(side * 0.12f * pair, 0.9f * slot,
                        LadderHeight * slot);

                case FormationShape.CombatSpread:
                    return new SlotShape(side * 1.75f * pair,
                        0.55f + 0.25f * (pair - 1), elementStep);

                // Symmetric V: wingmen splayed evenly either side and stepped back. Both
                // members of a pair share the same altitude, so the rung sits level.
                case FormationShape.Vic:
                    return new SlotShape(side * pair, 0.85f * pair, elementStep);

                case FormationShape.Wall:
                    return new SlotShape(side * 1.8f * pair, 0.1f * (pair - 1), elementStep);

                case FormationShape.FingerFour:
                    return FingerFour(slot);

                case FormationShape.Diamond:
                    return Diamond(slot);

                case FormationShape.EchelonRight:
                default:
                    return new SlotShape(slot, 0.72f * slot, (slot - 1) * 0.25f);
            }
        }

        /// <summary>
        /// Finger four: lead plus right and left wingmen, then an element lead astern. The
        /// three wingmen are fixed; any beyond that fall into a second element behind, so a
        /// larger wing still has somewhere sensible to sit rather than stacking on a slot.
        /// </summary>
        private static SlotShape FingerFour(int slot)
        {
            // A real finger-four is deliberately asymmetric: lead, close wingman, element
            // lead across the formation, then that lead's wingman farther out. Extra members
            // form another four behind it rather than collapsing into a centreline trail.
            switch (slot)
            {
                case 1: return new SlotShape(0.9f, 0.65f, 0f);
                case 2: return new SlotShape(-1.4f, 1.05f, 0f);
                case 3: return new SlotShape(-2.25f, 1.75f, 0f);
            }

            int extra = slot - 4;
            int group = extra / 4 + 1;
            int within = extra % 4;
            float back = group * 3f;
            float height = group * 0.5f;

            switch (within)
            {
                case 0: return new SlotShape(0f, back, height);
                case 1: return new SlotShape(0.9f, back + 0.65f, height);
                case 2: return new SlotShape(-1.4f, back + 1.05f, height);
                default: return new SlotShape(-2.25f, back + 1.75f, height);
            }
        }

        /// <summary>Diamond: two on the wings, one in the slot astern.</summary>
        private static SlotShape Diamond(int slot)
        {
            int group = (slot - 1) / 3;
            int within = (slot - 1) % 3;
            float back = group * 2f;
            float height = group * 0.5f;

            switch (within)
            {
                case 0: return new SlotShape(1f, back + 0.8f, height);
                case 1: return new SlotShape(-1f, back + 0.8f, height);
                default: return new SlotShape(0f, back + 1.6f, height + 0.25f);
            }
        }

        /// <param name="leaderForward">Leader forward vector; flattened internally.</param>
        /// <param name="slot">1-based slot index. Slot 0 is the leader itself.</param>
        public static Vector3 SlotOffset(
            Vector3 leaderForward, int slot, FormationShape shape, float spacing, float stack,
            float lateralScale = 1f, float backScale = 1f)
        {
            return WorldOffset(leaderForward,
                SlotCoordinates(slot, shape, spacing, stack, lateralScale, backScale));
        }

        /// <summary>
        /// Slot in leader-local metres: X right, Y up, Z forward (therefore negative aft).
        /// Keeping the transition in this frame lets shapes ease between one another while
        /// the whole formation still rotates immediately with the leader's heading.
        /// </summary>
        public static Vector3 SlotCoordinates(int slot, FormationShape shape, float spacing,
                                              float stack, float lateralScale = 1f,
                                              float backScale = 1f)
        {
            if (slot <= 0) return Vector3.zero;

            SlotShape s = Shape(shape, slot);
            return new Vector3(s.Lateral * spacing * lateralScale,
                               s.Height * stack,
                              -s.Back * spacing * backScale);
        }

        /// <summary>Rotate leader-local slot coordinates into a level world offset.</summary>
        public static Vector3 WorldOffset(Vector3 leaderForward, Vector3 local)
        {

            Vector3 fwd = new Vector3(leaderForward.x, 0f, leaderForward.z);
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            return right * local.x + Vector3.up * local.y + fwd * local.z;
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
        public static float SlotLateral(int slot, FormationShape shape, float spacing,
                                        float lateralScale = 1f)
        {
            if (slot <= 0) return 0f;
            return Shape(shape, slot).Lateral * spacing * lateralScale;
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
            int selfSlot = 0;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].Aircraft == self) { selfSlot = members[i].Slot; break; }
            }

            for (int i = 0; i < members.Count; i++)
            {
                WingMember otherMember = members[i];
                Aircraft other = otherMember.Aircraft;
                if (other == null || other == self || other.disabled) continue;

                Vector3 relativePosition = other.transform.position - selfPos;
                Vector3 relativeVelocity = other.rb != null && self.rb != null
                    ? other.rb.velocity - self.rb.velocity
                    : Vector3.zero;

                // Protect not only the separation now, but the closest approach in the next
                // few seconds. Rejoining aircraft can still be far apart while already on a
                // collision course; waiting until they are close is too late for a jet.
                float timeToClosest = 0f;
                float relativeSpeedSq = relativeVelocity.sqrMagnitude;
                if (relativeSpeedSq > 1f)
                    timeToClosest = Mathf.Clamp(
                        -Vector3.Dot(relativePosition, relativeVelocity) / relativeSpeedSq,
                        0f, 4f);

                Vector3 closest = relativePosition + relativeVelocity * timeToClosest;
                float distSq = closest.sqrMagnitude;
                if (distSq > radiusSq || distSq < 1f) continue;

                Vector3 away = -closest;
                if (away.sqrMagnitude < 1f) away = -relativePosition;

                // At low altitude the trailing/later element deconflicts high, matching the
                // real tactical priority: preserve terrain awareness for the lead element
                // and use the vertical for the aircraft responsible for separation.
                if (self.radarAlt < 300f && selfSlot > otherMember.Slot)
                    away += Vector3.up * radius * 0.45f;

                // Bounded inverse square: urgent at a close predicted pass, but never large
                // enough to fling a slot destination across the formation in one frame.
                float urgency = Mathf.Min(radiusSq / Mathf.Max(distSq, 1f), 4f);
                urgency *= 1f + (4f - timeToClosest) * 0.15f;
                push += away.normalized * urgency;
            }

            return push * strength;
        }
    }
}
