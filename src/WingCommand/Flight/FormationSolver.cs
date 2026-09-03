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
    /// Every shape is built from one primitive, <see cref="Place"/>: an <c>arm</c> length
    /// in slot-spacing units and a <c>sweep</c> angle that tilts the formation line from
    /// pure line-abreast (0°) to pure trail (90°), plus a signed side and a height in
    /// vertical-stack units. A <see cref="Spec"/> gives the seven regular shapes their
    /// arm-per-rank, sweep, symmetry and vertical law; Finger Four and Diamond place each
    /// slot by hand through the same primitive because their asymmetry is the point of
    /// them. Lateral and back therefore cannot drift apart — they are the two components of
    /// one arm — and no shape hard-codes a vertical offset out of the horizontal spacing,
    /// which is what made Ladder climb twice as fast as it should.
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
            switch (shape)
            {
                // Finger Four and Diamond are deliberately asymmetric — a real finger-four
                // is lead, close wingman, element lead across the formation, then that
                // lead's wingman wider still — so they place each slot by hand rather than
                // from a Spec. They still go through Place, so an arm and a sweep mean the
                // same thing for every shape.
                case FormationShape.FingerFour: return FingerFour(slot);
                case FormationShape.Diamond:    return Diamond(slot);
                default:                        return FromSpec(SpecFor(shape), slot);
            }
        }

        /// <summary>How a shape steps its slots vertically.</summary>
        private enum Vertical
        {
            /// <summary>One step up per rank: a stacked echelon, or a stepped-up wall.</summary>
            Step,

            /// <summary>Alternating up/down with a slow drift, so a long trail is not a staircase.</summary>
            Alternating,

            /// <summary>A large fixed climb per slot — the defining feature of the ladder.</summary>
            Ladder,
        }

        /// <summary>
        /// The parameters that describe one regular formation. <see cref="BaseArm"/> is the
        /// distance from the leader to the innermost slot in spacing units and grows
        /// linearly with rank; <see cref="SweepDeg"/> tilts the formation line from pure
        /// line-abreast (0°) to pure trail (90°); <see cref="Symmetric"/> alternates slots
        /// right and left into elements, or lays them all on one <see cref="Side"/>.
        /// </summary>
        private readonly struct Spec
        {
            public readonly float BaseArm;
            public readonly float SweepDeg;
            public readonly bool Symmetric;
            public readonly float Side;
            public readonly float StackStep;
            public readonly Vertical Stack;

            public Spec(float baseArm, float sweepDeg, bool symmetric, float side,
                        float stackStep, Vertical stack)
            {
                BaseArm = baseArm;
                SweepDeg = sweepDeg;
                Symmetric = symmetric;
                Side = side;
                StackStep = stackStep;
                Stack = stack;
            }
        }

        /// <summary>
        /// The dial settings for each regular shape. The numbers are tactical, not
        /// arbitrary: a fighting spread sits wide with barely any sweep, a vic splits the
        /// difference at forty-five degrees, a wall is a near-flat line, trail and ladder
        /// are dead astern, and an echelon stacks up one side at a shallow angle.
        /// </summary>
        private static Spec SpecFor(FormationShape shape)
        {
            switch (shape)
            {
                case FormationShape.EchelonLeft:
                    return new Spec(1.0f, 40f, symmetric: false, side: -1f, 0.20f, Vertical.Step);

                case FormationShape.LineAbreast:
                    return new Spec(1.0f, 0f, symmetric: true, side: 0f, 0.12f, Vertical.Step);

                case FormationShape.Trail:
                    return new Spec(0.95f, 90f, symmetric: false, side: 0f, 0.35f, Vertical.Alternating);

                case FormationShape.CombatSpread:
                    return new Spec(1.75f, 10f, symmetric: true, side: 0f, 0.16f, Vertical.Step);

                case FormationShape.Vic:
                    return new Spec(1.05f, 45f, symmetric: true, side: 0f, 0.20f, Vertical.Step);

                case FormationShape.Wall:
                    return new Spec(1.85f, 0f, symmetric: true, side: 0f, 0.12f, Vertical.Step);

                case FormationShape.Ladder:
                    return new Spec(0.95f, 90f, symmetric: false, side: 0f, 0f, Vertical.Ladder);

                case FormationShape.EchelonRight:
                default:
                    return new Spec(1.0f, 40f, symmetric: false, side: 1f, 0.20f, Vertical.Step);
            }
        }

        /// <summary>
        /// Resolve a <see cref="Spec"/> and a slot index to a concrete offset. Symmetric
        /// shapes count rank by element, so a pair shares an altitude and a distance;
        /// one-sided shapes count rank by slot, so each aircraft steps out and back from the
        /// one ahead of it.
        /// </summary>
        private static SlotShape FromSpec(Spec spec, int slot)
        {
            int pair = (slot + 1) / 2;                       // 1,1,2,2,3,3...
            int rank = spec.Symmetric ? pair : slot;
            float side = spec.Symmetric ? (slot % 2 == 1 ? 1f : -1f) : spec.Side;
            float arm = spec.BaseArm * rank;

            float height;
            switch (spec.Stack)
            {
                case Vertical.Alternating:
                    // Out of the wake of the one ahead without turning a long trail into a
                    // staircase hundreds of metres tall.
                    height = (slot % 2 == 1 ? spec.StackStep : -spec.StackStep)
                             + (slot - 1) * 0.08f;
                    break;

                case Vertical.Ladder:
                    height = LadderHeight * slot;
                    break;

                default:
                    height = spec.StackStep * (rank - 1);
                    break;
            }

            return Place(arm, spec.SweepDeg, side, height);
        }

        /// <summary>
        /// The one placement primitive. <paramref name="arm"/> is the distance from the
        /// leader along the formation line in spacing units; <paramref name="sweepDeg"/>
        /// rotates that line from the wingman's beam (0°, line abreast) to dead astern (90°,
        /// trail); <paramref name="side"/> is +1 to the leader's right, −1 to its left, 0 on
        /// the centreline. <paramref name="height"/> is already in vertical-stack units and
        /// passes straight through.
        /// </summary>
        private static SlotShape Place(float arm, float sweepDeg, float side, float height)
        {
            float sweep = sweepDeg * Mathf.Deg2Rad;
            return new SlotShape(side * arm * Mathf.Cos(sweep), arm * Mathf.Sin(sweep), height);
        }

        /// <summary>
        /// Finger four: standard right-hand arrangement. Flight leader at the point, flight
        /// wingman on the left, element lead on the right, and element wingman stepped back
        /// and wider still to the right. Exactly matches the fingertips of an outstretched hand.
        /// The first three slots are fixed; any beyond them form a second finger astern.
        /// </summary>
        private static SlotShape FingerFour(int slot)
        {
            switch (slot)
            {
                case 1: return Place(1.0f, 40f, -1f, 0f);
                case 2: return Place(1.15f, 40f, 1f, 0.05f);
                case 3: return Place(2.15f, 40f, 1f, 0.15f);
            }

            int extra = slot - 4;
            int group = extra / 4 + 1;
            int within = extra % 4;
            float back = group * 2.6f;
            float height = group * 0.4f;

            SlotShape lead;
            switch (within)
            {
                case 0:  lead = Place(0f, 90f, 0f, height); break;
                case 1:  lead = Place(1.0f, 40f, -1f, height); break;
                case 2:  lead = Place(1.15f, 40f, 1f, height + 0.05f); break;
                default: lead = Place(2.15f, 40f, 1f, height + 0.15f); break;
            }

            return new SlotShape(lead.Lateral, lead.Back + back, lead.Height);
        }

        /// <summary>
        /// Diamond: two on the wings at 45°, one in the slot astern. A geometrically exact
        /// rhombus where all four edges are equal in length (1.0 spacing units).
        /// </summary>
        private static SlotShape Diamond(int slot)
        {
            const float diamondSweep = 45f;
            float tailBack = 2f * Mathf.Sin(diamondSweep * Mathf.Deg2Rad); // ~1.4142 for 45°

            int group = (slot - 1) / 3;
            int within = (slot - 1) % 3;
            float back = group * (tailBack + 0.6f);
            float height = group * 0.4f;

            SlotShape point;
            switch (within)
            {
                case 0:  point = Place(1.0f, diamondSweep, 1f, height); break;
                case 1:  point = Place(1.0f, diamondSweep, -1f, height); break;
                default: point = Place(tailBack, 90f, 0f, height + 0.25f); break;
            }

            return new SlotShape(point.Lateral, point.Back + back, point.Height);
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
                // Airborne only. A hull sits under 300 metres permanently, so without the
                // autopilot test every trailing ship would be given a standing push into
                // the sky - which it cannot act on, and which corrupts the lateral
                // component of the push it can.
                if (self.autopilot != null && self.radarAlt < 300f && selfSlot > otherMember.Slot)
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
