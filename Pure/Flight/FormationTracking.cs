using System;

namespace WingCommand
{
    /// <summary>Engine-free continuous formation target motion.</summary>
    internal static class FormationTracking
    {
        public const float SlotResponseSeconds = 0.5f;

        // During rejoin, measure closure along rendezvous line of sight. Lateral and opposing-heading
        // joins are not captured along-track overshoots.
        public static float ApproachSpeed(float gapX, float gapZ,
            float ownVx, float ownVz, float slotVx, float slotVz,
            float braking, float aggression, float damping, float responseSeconds,
            float slotVy = 0f, float speedLead = 0f)
        {
            float distance = (float)Math.Sqrt(gapX * gapX + gapZ * gapZ);
            if (distance < 1f) return Math.Max(0f,
                (float)Math.Sqrt(slotVx * slotVx + slotVy * slotVy + slotVz * slotVz) + speedLead);
            float x = gapX / distance, z = gapZ / distance;
            float closing = (ownVx - slotVx) * x + (ownVz - slotVz) * z;
            float closure = Math.Max(0f, Math.Min(90f, FormationControlRules.RejoinClosure(
                distance, closing, braking, aggression, damping, 0.45f, 3f, 90f, responseSeconds)));
            float vx = slotVx + x * closure, vz = slotVz + z * closure;
            // Closure uses measured motion; engine lead survives rejoin blending and climbs retain
            // their vertical speed demand instead of throttling back to horizontal speed.
            return Math.Max(0f, (float)Math.Sqrt(vx * vx + slotVy * slotVy + vz * vz) + speedLead);
        }

        public static float QuietTurnRate(float rate, float deadband)
        {
            if (deadband <= 0f) return rate;
            float blend = Math.Max(0f, Math.Min(1f, (Math.Abs(rate) - deadband) / deadband));
            return rate * blend * blend * (3f - 2f * blend);
        }

        // Integrate constant-rate turn velocity instead of rotating velocity times duration, which
        // doubles shallow-turn lateral lead. Bound sweep to avoid predicted loops.
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

        // Accelerate tracking only for large manoeuvre error; retain quiet-flight filtering against
        // stick noise.
        public static float TrackResponse(float errorDegrees, float quietSeconds) =>
            ManeuverResponse(errorDegrees, quietSeconds, 0.10f, 2f, 12f);

        public static float BankResponse(float errorDegrees, float quietSeconds) =>
            ManeuverResponse(WrapDegrees(errorDegrees), quietSeconds, 0.12f, 8f, 45f);

        private static float ManeuverResponse(float error, float quiet, float fast,
            float begin, float full)
        {
            float blend = Math.Max(0f, Math.Min(1f, (Math.Abs(error) - begin) / (full - begin)));
            blend = blend * blend * (3f - 2f * blend);
            return quiet + (Math.Min(quiet, fast) - quiet) * blend;
        }

        // Weight horizontal information from normalized 3D velocity; near-vertical tracks cannot
        // reliably indicate yaw.
        public static float HorizontalTrackWeight(float x, float z)
        {
            float horizontal = (float)Math.Sqrt(x * x + z * z);
            float blend = Math.Max(0f, Math.Min(1f, (horizontal - 0.05f) / 0.15f));
            return blend * blend * (3f - 2f * blend);
        }

        public static float TrackTurnRate(float previousX, float previousZ,
            float currentX, float currentZ, float dt, float maximumRate)
        {
            if (dt <= 0f) return 0f;
            float confidence = Math.Min(HorizontalTrackWeight(previousX, previousZ),
                HorizontalTrackWeight(currentX, currentZ));
            double angle = Math.Atan2(previousZ * currentX - previousX * currentZ,
                previousX * currentX + previousZ * currentZ);
            float rate = (float)(angle / dt);
            return Math.Max(-maximumRate, Math.Min(maximumRate, rate)) * confidence;
        }

        // Cubic Hermite capture follows current velocity at departure and future slot velocity at
        // arrival. Bound tangents by gap to prevent loops.
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
            // Fade tangents outside the forward cone so rearward rendezvous can enter the heading
            // limiter instead of keeping preview ahead indefinitely.
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

        // Exact critically damped held-target response preserving continuous position and velocity
        // across attitude changes and geometry strides.
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
