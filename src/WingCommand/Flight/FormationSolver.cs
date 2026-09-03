using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Turns a leader's track plus a slot index into a world offset.
    ///
    /// Slot numbers live in <see cref="FormationLayout"/> — this file is the engine-facing
    /// half: metres, the velocity-plane frame a settled formation hangs off, and the
    /// Reynolds terms that keep rejoining aircraft out of each other. Icons, hulls and
    /// slot-picking still use the flattened frame; flight uses the banked one.
    /// </summary>
    internal static class FormationSolver
    {
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

            SlotLayout s = FormationLayout.Slot(shape, slot);
            return new Vector3(s.Lateral * spacing * lateralScale,
                               s.Height * stack,
                              -s.Back * spacing * backScale);
        }

        /// <summary>
        /// Flattened world offset: slots stay level with the horizon. Icons, hulls and
        /// slot-picking want this — a pitched leader must not lift a ship, and a formation
        /// glyph is a plan view.
        /// </summary>
        public static Vector3 WorldOffset(Vector3 leaderForward, Vector3 local) =>
            WorldOffset(leaderForward, local, bankDeg: 0f, velocityPlane: false);

        /// <summary>
        /// Rotate leader-local slot coordinates into a world offset.
        ///
        /// The flattened frame (the default) was the whole formation: every slot sat in the
        /// horizontal plane, so a climbing or rolling leader left its wingmen sliding
        /// sideways off the photograph. The velocity-plane frame hangs the same local
        /// offsets on the leader's track and then rolls them about that track by
        /// <paramref name="bankDeg"/>, which is what makes a diamond roll as one piece.
        /// Rejoin still uses the flattened frame — a banked slot two kilometres out is a
        /// destination through the ground.
        /// </summary>
        public static Vector3 WorldOffset(Vector3 track, Vector3 local, float bankDeg,
                                          bool velocityPlane)
        {
            Vector3 fwd = velocityPlane
                ? track
                : new Vector3(track.x, 0f, track.z);

            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.Cross(Vector3.forward, fwd);
                if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            }
            right.Normalize();
            Vector3 up = Vector3.Cross(fwd, right);

            if (velocityPlane && Mathf.Abs(bankDeg) > 0.05f)
            {
                Quaternion roll = Quaternion.AngleAxis(bankDeg, fwd);
                right = roll * right;
                up = roll * up;
            }
            else if (!velocityPlane)
            {
                up = Vector3.up;
            }

            return right * local.x + up * local.y + fwd * local.z;
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
            return FormationLayout.Slot(shape, slot).Lateral * spacing * lateralScale;
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
