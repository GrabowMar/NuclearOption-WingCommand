using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Where a field launch without a hangar stands, and where it joins the taxi graph.</summary>
    internal struct ServiceSpot
    {
        public int Point;
        public Pose Pose;
        public int StartNode;
    }

    /// <summary>Where a field launch without a hangar stands (spec M3 §3.1): on service point j as the game parks it, or
    /// place l of a queue on that point's way out, <see cref="Spread"/>·l along its route to the hold-short and facing
    /// along it (they leave in route order, the parked one last, and nobody turns out across another); the first spot
    /// with no aircraft within <see cref="ClearRadius"/>, every point before a second place, up to
    /// <see cref="MaxLanes"/>, never within <see cref="Spread"/> of the hold-short or on a runway.</summary>
    internal static class ServiceSpots
    {
        public static float Spread = 30f, ClearRadius = 15f;
        public static int MaxLanes = 4;

        public static bool Pick(FieldTraffic field, Func<Vec3, bool> occupied, out ServiceSpot spot)
        {
            var nodes = new List<int>();
            var edges = new List<int>();
            for (int lane = 0; lane < MaxLanes; lane++)
                for (int j = 0; j < field.Field.ServicePoints.Length; j++)
                    if (TrySpot(field, j, lane, nodes, edges, out spot) && !occupied(spot.Pose.Pos)) return true;
            spot = default;
            return false;
        }

        private static bool TrySpot(FieldTraffic field, int j, int lane, List<int> nodes, List<int> edges, out ServiceSpot spot)
        {
            TaxiGraph g = field.Graph;
            int node = g.ServiceNode(j), goal = g.HoldShort(field.RunwayIndex, field.Reverse);
            spot = new ServiceSpot { Point = j, Pose = field.Field.ServicePoints[j], StartNode = node };
            if (lane == 0) return true;
            if (node < 0 || goal < 0 || !TaxiRouter.Route(g, node, goal, null, nodes, edges)) return false;
            float want = lane * Spread, walked = 0f;
            for (int k = 0; k < edges.Count; k++)
            {
                float length = g.EdgeLength(edges[k]);
                if (walked + length < want)
                {
                    walked += length;
                    continue;
                }
                if (k == edges.Count - 1 && walked + length - want < Spread) return false;
                Vec3[] pts = g.EdgePoints(edges[k]);
                bool forward = g.EdgeFrom(edges[k]) == nodes[k];
                float left = want - walked;
                for (int i = 1; i < pts.Length; i++)
                {
                    Vec3 a = forward ? pts[i - 1] : pts[pts.Length - i], b = forward ? pts[i] : pts[pts.Length - 1 - i];
                    float segment = (b - a).Horizontal.Length;
                    if (segment < left && i < pts.Length - 1)
                    {
                        left -= segment;
                        continue;
                    }
                    Vec3 dir = (b - a).Horizontal.Normalized;
                    Vec3 at = a + (b - a) * Math.Min(1f, segment > 1e-4f ? left / segment : 0f);
                    foreach (RunwaySample r in field.Field.Runways)
                        if (r.Contains(at, ClearRadius)) return false;
                    spot = new ServiceSpot { Point = j, Pose = new Pose(at, dir), StartNode = nodes[k + 1] };
                    return true;
                }
            }
            return false;
        }
    }
}
