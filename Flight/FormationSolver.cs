using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Engine-facing slot geometry and separation using FormationLayout indices. Flight can use a
    /// banked velocity frame; icons, surface units, and slot allocation use a flattened frame.</summary>
    internal static class FormationSolver
    {
        /// <summary>Extend lateral, downward, and aft bounds for the shared terrain bank limit.</summary>
        internal static void IncludeBankFootprint(ref Vector3 footprint, Vector3 local)
        {
            footprint.x = Mathf.Max(footprint.x, Mathf.Abs(local.x));
            footprint.y = Mathf.Max(footprint.y, -local.y);
            footprint.z = Mathf.Max(footprint.z, -local.z);
        }

        // Use one shape-wide spacing scale chosen by the most cautious member; per-slot scales can
        // reverse ordering or overlap slots.
        internal static float SharedFlightSpacing(IReadOnlyList<WingMember> members, Aircraft leader)
        {
            float scale = 0.85f;
            bool found = false;
            if (members != null)
                foreach (WingMember member in members)
                    if (member != null && member.Alive && !member.DeliveryPending && member.Leader == leader)
                    {
                        scale = WingFlightProfile.CombineSpacing(scale, member.FlightProfile.SpacingScale);
                        found = true;
                    }
            return found ? scale : 1f;
        }

        /// <summary>Validate supported shapes and slots at startup to catch overlapping assignments before
        /// flight.</summary>
        public static bool ValidateGeometry(int maxSlots, out string problem)
        {
            var report = new StringBuilder();

            foreach (FormationShape shape in FormationShapes.All)
            {
                // Include the leader and zero-stack geometry so surface and terrain-flattened
                // formations remain separated.
                for (int turn = 0; turn <= 1; turn++)
                {
                    var slots = new Vector3[System.Math.Max(0, maxSlots) + 1];
                    float lateralScale = turn == 0 ? 1f : FormationLayout.TurnLateralScale;
                    float backScale = turn == 0 ? 1f : FormationLayout.TurnBackScale;
                    for (int slot = 1; slot <= maxSlots; slot++)
                    {
                        Vector3 point = SlotCoordinates(slot, shape, 1f, 0f, lateralScale, backScale);
                        slots[slot] = point;
                        if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) ||
                            float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z))
                        {
                            report.Append(shape).Append(" slot ").Append(slot).Append(" is not finite; ");
                            continue;
                        }

                        for (int previous = 0; previous < slot; previous++)
                        {
                            float minimum = FormationLayout.MinimumPlanarSeparation;
                            if ((slots[previous] - point).sqrMagnitude >= minimum * minimum) continue;
                            report.Append(shape).Append(" slots ").Append(previous)
                                  .Append(" and ").Append(slot).Append(" lack horizontal clearance")
                                  .Append(turn == 0 ? "; " : " in a turn; ");
                        }
                    }
                }
            }

            problem = report.ToString();
            return problem.Length == 0;
        }

        /// <param name="leaderForward">Leader direction, flattened internally.</param> <param
        /// name="slot">Follower index starting at 1; 0 denotes the leader.</param>
        public static Vector3 SlotOffset(
            Vector3 leaderForward, int slot, FormationShape shape, float spacing, float stack,
            float lateralScale = 1f, float backScale = 1f)
        {
            return WorldOffset(leaderForward,
                SlotCoordinates(slot, shape, spacing, stack, lateralScale, backScale));
        }

        /// <summary>Local slot coordinates in metres: X right, Y up, Z forward. Ease shapes here while
        /// allowing the whole frame to follow leader heading.</summary>
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

        /// <summary>Flattened world offset for icons, surface units, and slot allocation; leader pitch
        /// cannot lift these slots.</summary>
        public static Vector3 WorldOffset(Vector3 leaderForward, Vector3 local) =>
            WorldOffset(leaderForward, local, bankDeg: 0f, velocityPlane: false);

        /// <summary>Transform local slots using either a flat frame or the leader's velocity plane rolled
        /// by bankDeg. Distant rejoin stays flat to avoid banked targets below terrain.</summary>
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

        /// <summary>Push laterally out of the corridor ahead of the leader to prevent rejoin paths
        /// crossing its aircraft. Return zero outside the corridor.</summary> <param
        /// name="lookAhead">Protected corridor length.</param> <param name="corridorRadius">Protected
        /// corridor half-width.</param>
        public static Vector3 AvoidLeaderPath(Aircraft self, Aircraft leader,
                                              float lookAhead, float corridorRadius, float strength)
        {
            if (self == null || leader == null || lookAhead <= 0f || corridorRadius <= 0f)
                return Vector3.zero;

            // Align the protected corridor with actual travel direction, not sideslipping nose
            // direction.
            Vector3 forward = leader.rb != null && leader.rb.velocity.sqrMagnitude > 25f
                ? leader.rb.velocity.normalized : leader.transform.forward;
            Vector3 toSelf = self.transform.position - leader.transform.position;

            // Protect only the forward corridor; formation slots lie aft.
            float ahead = Vector3.Dot(toSelf, forward);
            if (ahead <= 0f || ahead > lookAhead) return Vector3.zero;

            Vector3 lateral = toSelf - forward * ahead;
            float offCentre = lateral.magnitude;
            if (offCentre > corridorRadius) return Vector3.zero;

            // Increase lateral push near the centreline and leader.
            Vector3 escape = offCentre > 0.1f
                ? lateral / offCentre
                : WorldOffset(forward, Vector3.left, bankDeg: 0f, velocityPlane: true);

            if (self.autopilot != null && self.radarAlt < 250f && escape.y < 0f)
            {
                escape.y = 0f;
                if (escape.sqrMagnitude < 0.0001f)
                    escape = WorldOffset(forward, Vector3.left, bankDeg: 0f, velocityPlane: true);
                escape.Normalize();
            }

            float urgency = (1f - offCentre / corridorRadius) * (1f - ahead / lookAhead);
            return escape * (strength * urgency);
        }

        /// <summary>Inverse-square repulsion from nearby wing members, protecting arbitrary converging
        /// rejoin paths.</summary>
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
                if (members[i] != null && members[i].Aircraft == self)
                { selfSlot = members[i].Slot; break; }
            }

            for (int i = 0; i < members.Count; i++)
            {
                WingMember otherMember = members[i];
                if (otherMember == null) continue;
                Aircraft other = otherMember.Aircraft;
                if (other == null || other == self || other.disabled) continue;

                Vector3 relativePosition = other.transform.position - selfPos;
                Vector3 relativeVelocity = other.rb != null && self.rb != null
                    ? other.rb.velocity - self.rb.velocity
                    : Vector3.zero;

                // Consider predicted closest approach so fast converging aircraft separate before they
                // are already too close.
                float timeToClosest = 0f;
                float relativeSpeedSq = relativeVelocity.sqrMagnitude;
                if (relativeSpeedSq > 1f)
                    timeToClosest = Mathf.Clamp(
                        -Vector3.Dot(relativePosition, relativeVelocity) / relativeSpeedSq,
                        0f, 4f);

                Vector3 closest = relativePosition + relativeVelocity * timeToClosest;
                float distSq = closest.sqrMagnitude;
                if (!FormationControlRules.CollisionThreat(distSq, radius)) continue;

                int pairOrder = selfSlot != otherMember.Slot
                    ? selfSlot.CompareTo(otherMember.Slot)
                    : self.GetInstanceID().CompareTo(other.GetInstanceID());
                FormationControlRules.EscapeDirection(closest.x, closest.y, closest.z,
                    relativeVelocity.x, relativeVelocity.z, pairOrder,
                    out float escapeX, out float escapeY, out float escapeZ);
                Vector3 away = new Vector3(escapeX, escapeY, escapeZ);

                // Never push an airborne member downward near terrain.
                if (self.autopilot != null && self.radarAlt < 250f && away.y < 0f)
                    away.y = 0f;

                // Near terrain, later airborne slots separate upward while the lead retains terrain
                // awareness. Exclude surface units from vertical steering.
                if (self.autopilot != null && self.radarAlt < 300f && selfSlot > otherMember.Slot)
                    away += Vector3.up * 0.45f;

                // Cap inverse-square urgency so a close predicted pass cannot fling the slot across the
                // formation.
                float urgency = Mathf.Min(radiusSq / Mathf.Max(distSq, 1f), 4f);
                urgency *= 1f + (4f - timeToClosest) * 0.15f;
                urgency *= Mathf.Clamp01(1f - Mathf.Sqrt(distSq) / radius);
                push += away.normalized * urgency;
            }

            return push * strength;
        }
    }
}
