using System;

namespace WingCommand
{
    /// <summary>The leader's recent estimates, one per tick (about 8.5 s at 60 Hz), so slots hang off where the
    /// leader actually was instead of its current turn extrapolated backwards. Queries interpolate between
    /// ticks; beyond the oldest sample they extrapolate that sample's arc. Push never allocates.</summary>
    internal sealed class LeaderHistory
    {
        public const int Capacity = 512;

        private readonly LeaderEstimate[] items = new LeaderEstimate[Capacity];
        private readonly float[] times = new float[Capacity];
        private float clock;
        private int newest = -1, count;

        public void Push(in LeaderEstimate e, float dt)
        {
            if (count > 0 && dt <= 0f)
            {
                items[newest] = e;   // same instant: replace
                return;
            }
            clock += Math.Max(0f, dt);
            newest = (newest + 1) % Capacity;
            items[newest] = e;
            times[newest] = clock;
            if (count < Capacity) count++;
        }

        /// <summary>Forget the past (the leader reappeared, possibly somewhere else).</summary>
        public void Clear()
        {
            count = 0;
            newest = -1;
            clock = 0f;
        }

        /// <summary>The leader <paramref name="seconds"/> ago.</summary>
        public LeaderEstimate At(float seconds)
        {
            if (count == 0) return default;
            if (seconds <= 0f) return TurnFrame.Delayed(items[newest], seconds);
            float target = clock - seconds;
            int oldest = Index(0);
            if (target <= times[oldest]) return TurnFrame.Delayed(items[oldest], times[oldest] - target);

            // Binary search over the ring (oldest = 0) for the last sample at or before the target time.
            int lo = 0, hi = count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (times[Index(mid)] <= target) lo = mid;
                else hi = mid;
            }
            int a = Index(lo), b = Index(hi);
            float span = times[b] - times[a];
            return Blend(items[a], items[b], span > 0f ? Scalar.Clamp01((target - times[a]) / span) : 1f);
        }

        private int Index(int fromOldest) => (newest - count + 1 + fromOldest + Capacity) % Capacity;

        /// <summary>Estimate between <paramref name="a"/> (t = 0) and <paramref name="b"/> (t = 1).</summary>
        public static LeaderEstimate Blend(in LeaderEstimate a, in LeaderEstimate b, float t)
        {
            Vec3 track = Vec3.Lerp(a.Track, b.Track, t).Normalized;
            return new LeaderEstimate
            {
                Pos = Vec3.Lerp(a.Pos, b.Pos, t),
                Vel = Vec3.Lerp(a.Vel, b.Vel, t),
                Acc = Vec3.Lerp(a.Acc, b.Acc, t),
                Track = track.SqrLength > 0.5f ? track : b.Track,
                BankDeg = a.BankDeg + Scalar.Wrap180(b.BankDeg - a.BankDeg) * t,
                BankRateDps = Scalar.Lerp(a.BankRateDps, b.BankRateDps, t),
                TurnRate = Scalar.Lerp(a.TurnRate, b.TurnRate, t),
                TurnAccel = Scalar.Lerp(a.TurnAccel, b.TurnAccel, t),
                Flying = b.Flying,
            };
        }
    }
}
