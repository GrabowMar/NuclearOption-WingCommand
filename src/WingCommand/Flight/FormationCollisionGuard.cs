using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    internal static class FormationCollisionGuard
    {
        public static bool TryAvoid(Aircraft self, Aircraft leader, IReadOnlyList<WingMember> members,
            float spacing, out Vector3 escape, out Aircraft threat, out float miss)
        {
            escape = Vector3.zero;
            threat = null;
            miss = 0f;
            if (self == null || self.rb == null) return false;
            float strongest = 0f;
            int count = members != null ? members.Count : 0;
            for (int i = -1; i < count; i++)
            {
                Aircraft other = i < 0 ? leader : members[i].Aircraft;
                if (other == null || other == self || other.disabled || other.rb == null) continue;
                Vector3 relative = other.transform.position - self.transform.position;
                Vector3 velocity = other.rb.velocity - self.rb.velocity;
                float physicalRadius = Mathf.Max(WingTuning.CollisionMinimumRadius,
                    LaunchSafety.Clearance(HangarLaunchSafety.Size(self.definition), HangarLaunchSafety.Size(other.definition)));
                // The predictive buffer must not repel a steady, valid compressed
                // formation forever. Physical clearance remains an absolute minimum.
                float radius = Mathf.Max(physicalRadius, Mathf.Min(spacing * 0.85f, relative.magnitude * 0.75f));
                float score = FormationCollision.Threat(relative.x, relative.y, relative.z,
                    velocity.x, velocity.y, velocity.z, radius, out float time, out float predictedMiss);
                if (score <= strongest) continue;
                strongest = score;
                Vector3 closest = relative + velocity * time;
                FormationControlRules.EscapeDirection(closest.x, closest.y, closest.z,
                    velocity.x, velocity.z, self.GetInstanceID().CompareTo(other.GetInstanceID()),
                    out float x, out float y, out float z);
                escape = new Vector3(x, y, z);
                if (self.radarAlt < 250f) escape.y = Mathf.Max(0f, escape.y);
                // A fore/aft collision needs a lateral escape, not an aim point
                // farther down the same collision course.
                Vector3 forward = self.rb.velocity.sqrMagnitude > 1f ? self.rb.velocity.normalized : self.transform.forward;
                escape = Vector3.ProjectOnPlane(escape, forward);
                if (escape.sqrMagnitude < 0.01f)
                {
                    Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                    escape = right * (self.GetInstanceID() < other.GetInstanceID() ? -1f : 1f);
                }
                escape.Normalize();
                threat = other;
                miss = predictedMiss;
            }
            return threat != null;
        }
    }
}
