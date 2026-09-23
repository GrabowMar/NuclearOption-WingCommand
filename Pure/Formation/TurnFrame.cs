using System;

namespace WingCommand
{
    /// <summary>Slot geometry around the leader. A slot is (right, aft, up) metres.
    /// <list type="bullet">
    /// <item>Aft is along the leader's own path, not its tangent: the slot hangs off where the leader was
    /// aft/V seconds ago, so trail and echelon slots ride the leader's arc instead of swinging sideways at
    /// ω·aft on every reversal.</item>
    /// <item>The level frame keeps right and up on the horizontal heading axes, which gives concentric arcs
    /// in a turn.</item>
    /// <item>The rolled frame carries them on the flight-path axes rotated by the frame bank, so fingertip
    /// slots stay on the wing line.</item>
    /// </list>
    /// The roll-follow weight w falls from 1 to 0 as the lateral offset grows from 15 to 45 m (fingertip to
    /// Close); wider slots match the leader's bank through their own lift vector instead of swinging on the
    /// wing line. A shape's slot may set rollFollow to override. In between, the frame is partly pitched
    /// toward the flight path and rolled by w·bank (a rotation, so the slot keeps its distance from the
    /// leader). Velocity and acceleration are central differences of the same prediction, taken
    /// leader-relative, so position, velocity and acceleration stay consistent and precise at map scale.</summary>
    internal static class TurnFrame
    {
        public static float RollFollowNear = 15f, RollFollowFar = 45f;
        public static float DiffStep = 0.1f;

        /// <summary>Roll-follow weight for a slot <paramref name="distanceM"/> from the leader (3-D offset length);
        /// an override ≥ 0 from the shape's data wins.</summary>
        public static float RollFollowWeight(float distanceM, float overrideWeight) =>
            overrideWeight >= 0f
                ? Scalar.Clamp01(overrideWeight)
                : 1f - Scalar.SmoothStep(RollFollowNear, RollFollowFar, Math.Abs(distanceM));

        /// <summary>Distance of a slot offset from the leader, the input to <see cref="RollFollowWeight"/>.</summary>
        public static float Reach(float right, float aft, float up) => (float)Math.Sqrt(right * right + aft * aft + up * up);

        /// <summary>World offset of a slot from a leader flying along <paramref name="velocity"/>.
        /// <paramref name="track"/> is used when the horizontal velocity is below the track-hold speed (vertical
        /// flight, a hover).</summary>
        public static Vec3 Offset(Vec3 velocity, Vec3 track, float bankDeg, float right, float aft, float up, float w)
        {
            Vec3 t = velocity.Horizontal;
            t = t.Length > LeaderEstimator.TrackHoldSpeed ? t.Normalized : track;
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

        /// <summary>Slot reference now: offset (right, up) from where the leader was aft/V ago on its current turn,
        /// with velocity and acceleration from central differences over that point's constant-turn prediction
        /// and the frame bank's constant-rate prediction.</summary>
        public static RefState Evaluate(in LeaderEstimate leader, float frameBankDeg, float frameBankRateDps,
            float right, float aft, float up, float w)
        {
            LeaderEstimate at = aft != 0f ? Delayed(leader, aft / Math.Max(50f, leader.Vel.Length)) : leader;
            float h = DiffStep;
            Vec3 back = Relative(at, -h, frameBankDeg, frameBankRateDps, right, up, w);
            Vec3 now = Relative(at, 0f, frameBankDeg, frameBankRateDps, right, up, w);
            Vec3 ahead = Relative(at, h, frameBankDeg, frameBankRateDps, right, up, w);
            return new RefState(at.Pos + now, (ahead - back) / (2f * h), (ahead - now * 2f + back) / (h * h));
        }

        /// <summary>The leader as it was <paramref name="seconds"/> ago on its current constant turn: position on
        /// the arc, velocity and acceleration turned back with it. The turn-rate change is not extrapolated this
        /// far back; it still shapes the local prediction around the delayed point.</summary>
        public static LeaderEstimate Delayed(in LeaderEstimate leader, float seconds)
        {
            LeaderEstimate d = leader;
            Vec3 horizontal = leader.Vel.Horizontal;
            float speed = horizontal.Length;
            if (speed <= LeaderEstimator.TrackHoldSpeed)
            {
                d.Pos = leader.Pos - leader.Vel * seconds;
                return d;
            }
            Vec3 t = horizontal / speed, c = Vec3.Cross(Vec3.Up, t);
            double angle = -leader.TurnRate * seconds, squared = angle * angle;
            double sinc = Math.Abs(angle) < 1e-3 ? 1d - squared / 6d : Math.Sin(angle) / angle;
            double cosc = Math.Abs(angle) < 1e-3 ? angle / 2d : (1d - Math.Cos(angle)) / angle;
            float along = Vec3.Dot(leader.Acc, t), lateral = Vec3.Dot(leader.Acc, c);
            d.Pos = leader.Pos + (t * (float)sinc + c * (float)cosc) * (-seconds * speed)
                    + t * (0.5f * along * seconds * seconds)
                    + Vec3.Up * (-leader.Vel.Y * seconds + 0.5f * leader.Acc.Y * seconds * seconds);
            Vec3 tBack = t * (float)Math.Cos(angle) + c * (float)Math.Sin(angle);
            d.Vel = tBack * (speed - along * seconds) + Vec3.Up * (leader.Vel.Y - leader.Acc.Y * seconds);
            d.Acc = tBack * along + Vec3.Cross(Vec3.Up, tBack) * lateral + Vec3.Up * leader.Acc.Y;
            d.Track = tBack;
            return d;
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
            float right, float up, float w)
        {
            Vec3 travel = leader.Vel * tau + leader.Acc * (0.5f * tau * tau);
            return travel + Offset(PredictVelocity(leader, tau), leader.Track, bank + bankRate * tau, right, 0f, up, w);
        }
    }
}
