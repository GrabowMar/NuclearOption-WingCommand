using System;
using System.Numerics;

namespace WingCommand
{
    /// <summary>Shared receding-horizon rendezvous for steering and speed demand.</summary>
    internal static class FormationIntercept
    {
        internal readonly struct Plan
        {
            public readonly Vector2 Gap, ArrivalVelocity;
            public readonly float Seconds;
            public Plan(Vector2 gap, Vector2 arrivalVelocity, float seconds)
            { Gap = gap; ArrivalVelocity = arrivalVelocity; Seconds = seconds; }
        }

        public static Plan Solve(Vector2 toSlot, Vector2 leaderVelocity, Vector2 slotOffset,
            Vector2 slotVelocity, float ownSpeed, float maximumSpeed, float turnRate)
        {
            float distance = toSlot.Length();
            float shortLead = Math.Min(6f, distance / Math.Max(50f, ownSpeed));
            // Allow long straight-flight prediction but cap turn extrapolation at 45 degrees.
            float horizon = Math.Min(45f, 0.7853982f / Math.Max(0.001f, Math.Abs(turnRate)));
            float intercept = InterceptSeconds(toSlot, leaderVelocity,
                Math.Max(ownSpeed, maximumSpeed * 0.9f), horizon);
            float blend = LongRangeBlend(distance);
            float seconds = shortLead + (intercept - shortLead) * blend;
            seconds = Math.Min(seconds, horizon);
            var travel = FormationTracking.Arc(leaderVelocity.X, 0f, leaderVelocity.Y, turnRate, seconds);
            float sweep = FormationTracking.Sweep(turnRate, seconds);
            Vector2 offset = Rotate(slotOffset, sweep);
            return new Plan(toSlot + new Vector2(travel.x, travel.z) + offset - slotOffset,
                Rotate(slotVelocity, sweep), seconds);
        }

        // Solve the earliest positive intercept time with stable double-precision quadratic arithmetic,
        // including equal-speed cases.
        internal static float InterceptSeconds(Vector2 gap, Vector2 velocity, float speed, float horizon)
        {
            double a = (double)velocity.X * velocity.X + (double)velocity.Y * velocity.Y - (double)speed * speed;
            double b = 2d * ((double)gap.X * velocity.X + (double)gap.Y * velocity.Y);
            double c = (double)gap.X * gap.X + (double)gap.Y * gap.Y;
            if (c < 1d) return 0f;
            double time = double.PositiveInfinity;
            if (Math.Abs(a) < 0.001d)
            {
                if (b < -0.001d) time = -c / b;
            }
            else
            {
                double discriminant = b * b - 4d * a * c;
                if (discriminant >= 0d)
                {
                    double root = Math.Sqrt(discriminant);
                    double q = -0.5d * (b + (b >= 0d ? root : -root));
                    double t1 = q / a, t2 = Math.Abs(q) > 1e-9d ? c / q : double.PositiveInfinity;
                    if (t1 >= 0d) time = t1;
                    if (t2 >= 0d) time = Math.Min(time, t2);
                }
            }
            // Bound prediction for unreachable faster leaders; never emit infinite or rearward
            // extrapolation.
            return (float)Math.Max(0d, Math.Min(Math.Max(0f, horizon), time));
        }

        internal static float LongRangeBlend(float distance)
        {
            float t = Math.Max(0f, Math.Min(1f, (distance - 600f) / 1800f));
            return t * t * (3f - 2f * t);
        }

        private static Vector2 Rotate(Vector2 v, float angle)
        {
            float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
            return new Vector2(v.X * c + v.Y * s, v.Y * c - v.X * s);
        }
    }
}
