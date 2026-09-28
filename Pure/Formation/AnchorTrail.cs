using System;

namespace WingCommand
{
    /// <summary>The anchor's route as breadcrumbs, one per <see cref="CrumbSpacing"/> metres flown (512 crumbs, about
    /// 77 km), so members too slow to hold a slot can fly where the anchor went, minutes behind it (spec M2 §5.2).
    /// Positions along the route are arc lengths of the distance actually flown; between crumbs the route is the
    /// straight line. Push never allocates.</summary>
    internal sealed class AnchorTrail
    {
        public const int Capacity = 512;
        public static float CrumbSpacing = 150f;

        private readonly Vec3[] points = new Vec3[Capacity];
        private readonly float[] arcs = new float[Capacity];
        private int newest = -1, count;
        private Vec3 current;

        /// <summary>Arc length of the anchor's latest position.</summary>
        public float Head { get; private set; }

        public void Clear()
        {
            count = 0;
            newest = -1;
            Head = 0f;
        }

        public void Push(Vec3 anchor)
        {
            if (count == 0)
            {
                Add(anchor, 0f);
                current = anchor;
                Head = 0f;
                return;
            }
            Head += (anchor - current).Length;
            current = anchor;
            if (Head - arcs[newest] >= CrumbSpacing) Add(anchor, Head);
        }

        /// <summary>The route point at arc length <paramref name="s"/> (clamped to what was recorded) and the horizontal
        /// direction of travel there.</summary>
        public Vec3 PointAt(float s, out Vec3 direction)
        {
            direction = Vec3.Forward;
            if (count == 0) return Vec3.Zero;
            int oldest = Index(0);
            if (s <= arcs[oldest])
            {
                direction = Direction(points[oldest], count > 1 ? points[Index(1)] : current, direction);
                return points[oldest];
            }
            if (s >= arcs[newest])
            {
                // Past the newest crumb: the stretch to where the anchor is now.
                float span = Head - arcs[newest];
                if (span <= 1e-3f)
                {
                    if (count > 1) direction = Direction(points[Index(count - 2)], points[newest], direction);
                    return current;
                }
                direction = Direction(points[newest], current, direction);
                return Vec3.Lerp(points[newest], current, Scalar.Clamp01((s - arcs[newest]) / span));
            }
            int lo = 0, hi = count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (arcs[Index(mid)] <= s) lo = mid;
                else hi = mid;
            }
            int a = Index(lo), b = Index(hi);
            direction = Direction(points[a], points[b], direction);
            return Vec3.Lerp(points[a], points[b], (s - arcs[a]) / Math.Max(1e-3f, arcs[b] - arcs[a]));
        }

        /// <summary>Arc length of the route point nearest to <paramref name="p"/> (horizontally).</summary>
        public float Nearest(Vec3 p)
        {
            if (count == 0) return 0f;
            float best = float.MaxValue, bestArc = arcs[Index(0)];
            for (int i = 0; i < count; i++)
            {
                int a = Index(i);
                Vec3 from = points[a], to = i + 1 < count ? points[Index(i + 1)] : current;
                float arcTo = i + 1 < count ? arcs[Index(i + 1)] : Head;
                Vec3 seg = (to - from).Horizontal;
                float length2 = seg.SqrLength;
                float t = length2 > 1e-6f ? Scalar.Clamp01(Vec3.Dot((p - from).Horizontal, seg) / length2) : 0f;
                float d = ((from + (to - from) * t) - p).Horizontal.SqrLength;
                if (d < best)
                {
                    best = d;
                    bestArc = arcs[a] + (arcTo - arcs[a]) * t;
                }
            }
            return bestArc;
        }

        private void Add(Vec3 p, float arc)
        {
            newest = (newest + 1) % Capacity;
            points[newest] = p;
            arcs[newest] = arc;
            if (count < Capacity) count++;
        }

        private int Index(int fromOldest) => (newest - count + 1 + fromOldest + Capacity) % Capacity;

        private static Vec3 Direction(Vec3 from, Vec3 to, Vec3 fallback)
        {
            Vec3 d = (to - from).Horizontal;
            return d.SqrLength > 1e-6f ? d.Normalized : fallback;
        }
    }
}
