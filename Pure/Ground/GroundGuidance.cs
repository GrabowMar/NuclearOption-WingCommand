using System;

namespace WingCommand
{
    /// <summary>What ground guidance asks of the controller: a path curvature (1/m, + is a right turn), a speed, or a stop.</summary>
    internal struct GroundCommand
    {
        public float Curvature, Speed;
        public bool Stop;
    }

    /// <summary>Regulated pure pursuit along a taxi path (spec M3 §2.1).
    /// <list type="bullet">
    /// <item>Progress: the path segment the aircraft is on; it only moves forward.</item>
    /// <item>Curvature to the point one lookahead (max(<see cref="Lookahead"/>, 1.5 s of travel)) along the path:
    /// κ = 2·y/d², y the point's offset to the right of the nose, d its distance. A point behind the nose (a route that
    /// doubles back) turns the aircraft around at the tightest radius, <see cref="MinTurnRadius"/>, at walking pace.</item>
    /// <item>Speed: <see cref="TaxiSpeed"/>, cut for the curvature (√(TurnAccel/κ)), for each corner ahead within braking
    /// distance (a corner turning θ is flown at √(TurnAccel·lookahead/θ), reached by braking at
    /// <see cref="BrakeDecel"/>), and for the stop point (√(2·BrakeDecel·d)). Under a metre from the stop point it
    /// stops.</item>
    /// </list></summary>
    internal static class GroundGuidance
    {
        public static float Lookahead = 12f, LookaheadTime = 1.5f, TaxiSpeed = 12f, TurnAccel = 1.5f, BrakeDecel = 1.5f, StopRadius = 1f;
        public static float MinTurnRadius = 15f;

        public static GroundCommand Pursue(Vec3[] path, ref int progress, in AircraftState s, float stopDistance)
        {
            if (stopDistance < StopRadius || path == null || path.Length < 2) return new GroundCommand { Stop = true };
            Vec3 pos = s.Pos.Horizontal;
            Vec3 fwd = s.Fwd.Horizontal.SqrLength > 1e-4f ? s.Fwd.Horizontal.Normalized : Vec3.Forward;
            Vec3 right = Vec3.Cross(Vec3.Up, fwd);
            float speed = Math.Max(0f, Vec3.Dot(s.Vel, fwd));

            // Advance past segments whose end the aircraft has reached.
            while (progress < path.Length - 2 && Along(path[progress].Horizontal, path[progress + 1].Horizontal, pos) >= 1f) progress++;
            Vec3 a = path[progress].Horizontal, b = path[progress + 1].Horizontal;
            float t = Scalar.Clamp(Along(a, b, pos), 0f, 1f);

            float lookahead = Math.Max(Lookahead, LookaheadTime * speed);
            Vec3 target = Walk(path, progress, a + (b - a) * t, lookahead, out _);
            Vec3 toTarget = target - pos;
            float d2 = Math.Max(toTarget.SqrLength, 1e-2f);
            float lateral = Vec3.Dot(toTarget, right);
            float curvature = 2f * lateral / d2;
            if (Vec3.Dot(toTarget, fwd) < 0f)
                curvature = (lateral < 0f ? -1f : 1f) * Math.Max(Math.Abs(curvature), 1f / MinTurnRadius);

            float limit = Math.Min(TaxiSpeed, (float)Math.Sqrt(2f * BrakeDecel * stopDistance));
            if (Math.Abs(curvature) > 1e-4f) limit = Math.Min(limit, (float)Math.Sqrt(TurnAccel / Math.Abs(curvature)));
            // Corners ahead within braking distance of the taxi speed.
            float reach = TaxiSpeed * TaxiSpeed / (2f * BrakeDecel) + lookahead;
            float distance = (b - (a + (b - a) * t)).Length;
            for (int k = progress + 1; k < path.Length - 1 && distance <= reach; k++)
            {
                Vec3 inDir = (path[k].Horizontal - path[k - 1].Horizontal).Normalized;
                Vec3 outDir = (path[k + 1].Horizontal - path[k].Horizontal).Normalized;
                float theta = (float)Math.Acos(Scalar.Clamp(Vec3.Dot(inDir, outDir), -1f, 1f));
                if (theta > 0.05f)
                {
                    float corner = (float)Math.Sqrt(TurnAccel * lookahead / theta);
                    limit = Math.Min(limit, (float)Math.Sqrt(corner * corner + 2f * BrakeDecel * distance));
                }
                distance += (path[k + 1].Horizontal - path[k].Horizontal).Length;
            }
            return new GroundCommand { Curvature = curvature, Speed = limit };
        }

        /// <summary>Distance along the path from a point on segment <paramref name="segment"/> to its end.</summary>
        public static float Remaining(Vec3[] path, int segment, Vec3 at)
        {
            float d = (path[segment + 1].Horizontal - at.Horizontal).Length;
            for (int k = segment + 1; k < path.Length - 1; k++) d += (path[k + 1] - path[k]).Horizontal.Length;
            return d;
        }

        private static float Along(Vec3 a, Vec3 b, Vec3 p)
        {
            Vec3 ab = b - a;
            float len2 = ab.SqrLength;
            return len2 < 1e-6f ? 1f : Vec3.Dot(p - a, ab) / len2;
        }

        private static Vec3 Walk(Vec3[] path, int segment, Vec3 from, float distance, out int endSegment)
        {
            Vec3 at = from;
            for (int k = segment; k < path.Length - 1; k++)
            {
                Vec3 next = path[k + 1].Horizontal;
                float leg = (next - at).Length;
                if (leg >= distance)
                {
                    endSegment = k;
                    return at + (next - at) * (distance / Math.Max(leg, 1e-4f));
                }
                distance -= leg;
                at = next;
            }
            endSegment = path.Length - 2;
            // Past the end: extend along the last segment so the nose stays on the line.
            Vec3 last = (path[path.Length - 1] - path[path.Length - 2]).Horizontal.Normalized;
            return at + last * distance;
        }
    }

    /// <summary>Ground stick, throttle and brake (spec M3 §2.1): the nose wheel from the bicycle model
    /// (yaw = atan(κ·wheelbase) / steering lock, the lock's sign included), taxi speed by a PI on throttle (up to
    /// <see cref="ThrottleMax"/>), and brakes with the throttle closed when over speed by more than
    /// <see cref="OverspeedBand"/> or told to stop.</summary>
    internal sealed class GroundController
    {
        public static float SpeedKp = 0.15f, SpeedKi = 0.05f, BrakeGain = 0.3f, ThrottleMax = 0.6f, OverspeedBand = 1f;

        private float integrator;

        public ControlOutput Step(in GroundCommand c, in AircraftState s, AirframeProfile p, float dt)
        {
            float lock_ = Math.Abs(p.SteerLockDeg) > 1f ? p.SteerLockDeg : 45f;
            float yaw = Scalar.Clamp((float)Math.Atan(c.Curvature * p.WheelbaseM) * Scalar.Rad2Deg / lock_, -1f, 1f);
            Vec3 fwd = s.Fwd.Horizontal.SqrLength > 1e-4f ? s.Fwd.Horizontal.Normalized : Vec3.Forward;
            float speed = Vec3.Dot(s.Vel, fwd);
            if (c.Stop)
            {
                integrator = 0f;
                return new ControlOutput { Yaw = yaw, Brake = 1f };
            }
            float error = c.Speed - speed;
            if (error < -OverspeedBand)
            {
                integrator = Math.Max(0f, integrator - SpeedKi * dt);
                return new ControlOutput { Yaw = yaw, Brake = Scalar.Clamp01(BrakeGain * -error) };
            }
            integrator = Scalar.Clamp(integrator + SpeedKi * error * dt, 0f, ThrottleMax);
            return new ControlOutput { Yaw = yaw, Throttle = Scalar.Clamp(SpeedKp * error + integrator, 0f, ThrottleMax) };
        }

        /// <summary>Taking over (a spawn, or a native state): the integrator restarts.</summary>
        public void Reset() => integrator = 0f;
    }
}
