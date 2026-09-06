using System;

namespace WingCommand
{
    /// <summary>Continuous motion for formation targets, independent of the engine.</summary>
    internal static class FormationTracking
    {
        public const float SlotResponseSeconds = 0.5f;

        // Outside the slot, closure belongs to the line of sight to the rendezvous,
        // not the leader's forward axis. A lateral join needs both forward speed
        // and lateral closure; an ahead/opposite-heading join must not be mistaken
        // for an already captured aircraft that merely needs to reduce throttle.
        public static float ApproachSpeed(float gapX, float gapZ,
            float ownVx, float ownVz, float slotVx, float slotVz,
            float braking, float aggression, float damping, float responseSeconds)
        {
            float distance = (float)Math.Sqrt(gapX * gapX + gapZ * gapZ);
            if (distance < 1f) return (float)Math.Sqrt(slotVx * slotVx + slotVz * slotVz);
            float x = gapX / distance, z = gapZ / distance;
            float closing = (ownVx - slotVx) * x + (ownVz - slotVz) * z;
            float closure = Math.Max(0f, Math.Min(90f, FormationControlRules.RejoinClosure(
                distance, closing, braking, aggression, damping, 0.45f, 3f, 90f, responseSeconds)));
            float vx = slotVx + x * closure, vz = slotVz + z * closure;
            return (float)Math.Sqrt(vx * vx + vz * vz);
        }

        public static float QuietTurnRate(float rate, float deadband)
        {
            if (deadband <= 0f) return rate;
            float blend = Math.Max(0f, Math.Min(1f, (Math.Abs(rate) - deadband) / deadband));
            return rate * blend * blend * (3f - 2f * blend);
        }

        // Integrate velocity around a constant-rate turn. Rotating velocity * time
        // instead doubles the lateral lead for a shallow turn. Bound the sweep so
        // a long intercept never predicts a loop back through the leader.
        public static float Sweep(float turnRate, float seconds) =>
            Math.Max(-(float)Math.PI / 2f, Math.Min((float)Math.PI / 2f,
                turnRate * Math.Max(0f, seconds)));

        public static (float x, float y, float z) Arc(
            float vx, float vy, float vz, float turnRate, float seconds)
        {
            double time = Math.Max(0f, seconds);
            double angle = Sweep(turnRate, seconds);
            double squared = angle * angle;
            double sinc = Math.Abs(angle) < 0.001d
                ? 1d - squared / 6d + squared * squared / 120d : Math.Sin(angle) / angle;
            double cosc = Math.Abs(angle) < 0.001d
                ? angle * (0.5d - squared / 24d + squared * squared / 720d)
                : (1d - Math.Cos(angle)) / angle;
            return ((float)(time * (vx * sinc + vz * cosc)),
                    (float)(time * vy),
                    (float)(time * (vz * sinc - vx * cosc)));
        }

        public static (float x, float y, float z) FutureSlotOffset(
            float vx, float vy, float vz, float offsetX, float offsetY, float offsetZ,
            float turnRate, float seconds)
        {
            var travel = Arc(vx, vy, vz, turnRate, seconds);
            double angle = Sweep(turnRate, seconds);
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            return (travel.x + (float)(offsetX * cos + offsetZ * sin),
                    travel.y + offsetY,
                    travel.z + (float)(offsetZ * cos - offsetX * sin));
        }

        public static float WrapDegrees(float degrees)
        {
            float result = (degrees + 180f) % 360f;
            return (result < 0f ? result + 360f : result) - 180f;
        }

        public static float SmoothBank(float bank, float observed, float responseSeconds, float dt) =>
            WrapDegrees(bank + WrapDegrees(observed - bank) *
                (1f - (float)Math.Exp(-Math.Max(0f, dt) / Math.Max(0.001f, responseSeconds))));

        // Cubic Hermite approach: leave along the follower's current velocity and
        // arrive along the slot's future velocity. Tangents cannot exceed the gap,
        // preventing loops when a prediction horizon is long or the gap is small.
        public static (float x, float z) Capture(
            float targetX, float targetZ, float ownVx, float ownVz,
            float slotVx, float slotVz, float seconds, float previewSeconds)
        {
            double time = Math.Max(0.001f, seconds);
            double t = Math.Max(0d, Math.Min(1d, previewSeconds / time));
            double distance = Math.Sqrt((double)targetX * targetX + (double)targetZ * targetZ);
            double ownScale = Math.Min(time, distance / Math.Max(1d,
                Math.Sqrt((double)ownVx * ownVx + (double)ownVz * ownVz)));
            double slotScale = Math.Min(time, distance / Math.Max(1d,
                Math.Sqrt((double)slotVx * slotVx + (double)slotVz * slotVz)));
            // When the rendezvous is behind, a forward departure tangent can keep
            // a receding-horizon preview ahead forever. Fade the tangents outside
            // the forward cone so the heading limiter can first turn toward it.
            double alignment = ((double)targetX * ownVx + (double)targetZ * ownVz) /
                Math.Max(1d, distance * Math.Sqrt((double)ownVx * ownVx + (double)ownVz * ownVz));
            double tangentBlend = Math.Max(0d, Math.Min(1d, alignment * 4d));
            tangentBlend *= tangentBlend * (3d - 2d * tangentBlend);
            ownScale *= tangentBlend;
            slotScale *= tangentBlend;
            double h10 = t * (1d - t) * (1d - t);
            double h01 = t * t * (3d - 2d * t);
            double h11 = t * t * (t - 1d);
            return ((float)(h10 * ownVx * ownScale + h01 * targetX + h11 * slotVx * slotScale),
                    (float)(h10 * ownVz * ownScale + h01 * targetZ + h11 * slotVz * slotScale));
        }

        // Exact critically damped response for a held sample. Unlike differentiating
        // a freshly rotated slot, this keeps position and velocity continuous across
        // attitude changes and behaves the same at different geometry tick strides.
        public static void DampedAxis(float position, float velocity, float target,
            float responseSeconds, float maxSpeed, float dt, out float nextPosition, out float nextVelocity)
        {
            if (dt <= 0f) { nextPosition = position; nextVelocity = velocity; return; }
            double response = Math.Max(0.001f, responseSeconds);
            double omega = 2d / response;
            double maxChange = Math.Max(0f, maxSpeed) * response;
            double change = Math.Max(-maxChange, Math.Min(maxChange, position - target));
            double effectiveTarget = position - change;
            double c = velocity + omega * change;
            double decay = Math.Exp(-omega * dt);
            nextPosition = (float)(effectiveTarget + (change + c * dt) * decay);
            nextVelocity = (float)((velocity - omega * c * dt) * decay);
        }
    }
}
