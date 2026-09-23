using System;

namespace WingCommand
{
    /// <summary>Slot geometry around the leader. A slot is (right, aft, up) metres.
    /// <list type="bullet">
    /// <item>The level frame keeps the slot on the leader's horizontal heading axes, which gives concentric
    /// arcs in a turn.</item>
    /// <item>The rolled frame carries it on the leader's flight-path axes rotated by the frame bank, so close
    /// slots stay on the wing line.</item>
    /// </list>
    /// The roll-follow weight w falls from 1 to 0 as the lateral offset grows from 60 to 180 m. In between,
    /// the frame is partly pitched toward the flight path and rolled by w·bank (a rotation, so the slot
    /// keeps its distance from the leader). Velocity and
    /// acceleration are central differences of the same prediction, taken leader-relative, so position,
    /// velocity and acceleration stay consistent and precise at map scale.</summary>
    internal static class TurnFrame
    {
        public const float RollFollowNear = 60f, RollFollowFar = 180f;
        public const float DiffStep = 0.1f;

        public static float RollFollowWeight(float lateralM, float overrideWeight) =>
            overrideWeight >= 0f
                ? Scalar.Clamp01(overrideWeight)
                : 1f - Scalar.SmoothStep(RollFollowNear, RollFollowFar, Math.Abs(lateralM));

        /// <summary>World offset of a slot from a leader flying along <paramref name="velocity"/>.
        /// <paramref name="track"/> is used when the velocity is (near) vertical.</summary>
        public static Vec3 Offset(Vec3 velocity, Vec3 track, float bankDeg, float right, float aft, float up, float w)
        {
            Vec3 t = velocity.Horizontal;
            t = t.SqrLength > 1f ? t.Normalized : track;
            Vec3 c = Vec3.Cross(Vec3.Up, t);
            if (w <= 0f) return c * right - t * aft + Vec3.Up * up;

            // A partial roll-follow is a partial rotation, not a chord: the forward axis tilts from the
            // horizontal track toward the flight path and the frame rolls by w·bank, so the slot keeps its
            // distance from the leader at every w.
            Vec3 path = velocity.SqrLength > 1f ? velocity.Normalized : t;
            Vec3 f = w >= 1f ? path : Vec3.Lerp(t, path, w).Normalized;
            if (f.SqrLength < 0.5f) f = t;
            Vec3 r = Vec3.Cross(Vec3.Up, f);
            r = r.SqrLength > 1e-4f ? r.Normalized : c;
            Vec3 u = Vec3.Cross(f, r);
            float phi = Scalar.Clamp01(w) * bankDeg * Scalar.Deg2Rad;
            float cb = (float)Math.Cos(phi), sb = (float)Math.Sin(phi);
            return (r * cb - u * sb) * right - f * aft + (u * cb + r * sb) * up;
        }

        /// <summary>Slot reference now, with velocity and acceleration from central differences over the
        /// leader's constant-turn prediction and the frame bank's constant-rate prediction.</summary>
        public static RefState Evaluate(in LeaderEstimate leader, float frameBankDeg, float frameBankRateDps,
            float right, float aft, float up, float w)
        {
            const float h = DiffStep;
            Vec3 back = Relative(leader, -h, frameBankDeg, frameBankRateDps, right, aft, up, w);
            Vec3 now = Relative(leader, 0f, frameBankDeg, frameBankRateDps, right, aft, up, w);
            Vec3 ahead = Relative(leader, h, frameBankDeg, frameBankRateDps, right, aft, up, w);
            return new RefState(leader.Pos + now, (ahead - back) / (2f * h), (ahead - now * 2f + back) / (h * h));
        }

        /// <summary>Leader velocity τ seconds ahead: heading turned by ωτ + ½ω̇τ², horizontal and vertical
        /// speed changed by the along-track and vertical acceleration.</summary>
        public static Vec3 PredictVelocity(in LeaderEstimate leader, float tau)
        {
            Vec3 horizontal = leader.Vel.Horizontal;
            float speed = horizontal.Length;
            if (speed < 1f) return leader.Vel + leader.Acc * tau;
            Vec3 t = horizontal / speed;
            float psi = leader.TurnRate * tau + 0.5f * leader.TurnAccel * tau * tau;
            Vec3 turned = t * (float)Math.Cos(psi) + Vec3.Cross(Vec3.Up, t) * (float)Math.Sin(psi);
            return turned * (speed + Vec3.Dot(leader.Acc, t) * tau) + Vec3.Up * (leader.Vel.Y + leader.Acc.Y * tau);
        }

        private static Vec3 Relative(in LeaderEstimate leader, float tau, float bank, float bankRate,
            float right, float aft, float up, float w)
        {
            Vec3 travel = leader.Vel * tau + leader.Acc * (0.5f * tau * tau);
            return travel + Offset(PredictVelocity(leader, tau), leader.Track, bank + bankRate * tau, right, aft, up, w);
        }
    }
}
