using System;
using System.Collections.Generic;
using System.Globalization;

namespace WingCommand
{
    /// <summary>One line on the map: a leg to point <see cref="Number"/> (1-based; 0 = no label), the distance flown to its
    /// end from the lead (km) and the time to get there (s, NaN unknown).</summary>
    internal struct RouteLeg
    {
        public float FromX, FromZ, ToX, ToZ;
        public int Number;
        public float Km, Eta;
        public ArrivalAction Action;
        public bool Closing;
    }

    internal struct RouteRing
    {
        public float X, Z, Radius;
    }

    /// <summary>What the map draws for a task and for the route draft (spec WMC program §5): the legs still to fly from the
    /// lead, numbered, with cumulative distance and ETA; an orbit ring at an orbit point or task; a patrol's circuit.
    /// Both append, so one refresh draws every element.</summary>
    internal static class RouteView
    {
        public static void Task(WingTask task, int leg, Vec3 lead, float speed, List<RouteLeg> legs, List<RouteRing> rings)
        {
            if (task == null || task.Points == null || task.Points.Length == 0) return;
            Waypoint[] p = task.Points;
            switch (task.Kind)
            {
                case TaskKind.Move:
                case TaskKind.Route:
                {
                    float km = 0f, x = lead.X, z = lead.Z;
                    for (int i = Math.Max(0, leg); i < p.Length; i++)
                    {
                        km += Distance(x, z, p[i].X, p[i].Z) / 1000f;
                        legs.Add(new RouteLeg { FromX = x, FromZ = z, ToX = p[i].X, ToZ = p[i].Z, Number = i + 1, Km = km, Eta = Eta(km, speed), Action = p[i].Action });
                        if (p[i].Action == ArrivalAction.Orbit) rings.Add(new RouteRing { X = p[i].X, Z = p[i].Z, Radius = TaskLead.OrbitRadius(speed) });
                        x = p[i].X;
                        z = p[i].Z;
                    }
                    return;
                }
                case TaskKind.Patrol:
                {
                    int next = Math.Min(Math.Max(0, leg), p.Length - 1);
                    float km = Distance(lead.X, lead.Z, p[next].X, p[next].Z) / 1000f;
                    legs.Add(new RouteLeg { FromX = lead.X, FromZ = lead.Z, ToX = p[next].X, ToZ = p[next].Z, Number = next + 1, Km = km, Eta = Eta(km, speed) });
                    // The circuit leg into the point the lead's leg already labels stays unlabelled (review P4 I2).
                    for (int i = 0; i + 1 < p.Length; i++)
                        legs.Add(new RouteLeg
                        {
                            FromX = p[i].X, FromZ = p[i].Z, ToX = p[i + 1].X, ToZ = p[i + 1].Z, Number = i + 1 == next ? 0 : i + 2,
                            Km = Distance(p[i].X, p[i].Z, p[i + 1].X, p[i + 1].Z) / 1000f, Eta = float.NaN,
                        });
                    int last = p.Length - 1;
                    if (task.Loop && p.Length > 2)
                        legs.Add(new RouteLeg
                        {
                            FromX = p[last].X, FromZ = p[last].Z, ToX = p[0].X, ToZ = p[0].Z,
                            Km = Distance(p[last].X, p[last].Z, p[0].X, p[0].Z) / 1000f, Eta = float.NaN, Closing = true,
                        });
                    return;
                }
                case TaskKind.Orbit:
                case TaskKind.Hold:
                {
                    float r = TaskLead.OrbitRadius(speed);
                    rings.Add(new RouteRing { X = p[0].X, Z = p[0].Z, Radius = r });
                    float km = Distance(lead.X, lead.Z, p[0].X, p[0].Z) / 1000f;
                    if (km * 1000f > r) legs.Add(new RouteLeg { FromX = lead.X, FromZ = lead.Z, ToX = p[0].X, ToZ = p[0].Z, Number = 1, Km = km, Eta = Eta(km, speed) });
                    return;
                }
            }
        }

        public static void Draft(RouteDraft d, Vec3 from, float speed, List<RouteLeg> legs)
        {
            float km = 0f, x = from.X, z = from.Z;
            for (int i = 0; i < d.Count; i++)
            {
                Waypoint p = d[i];
                km += Distance(x, z, p.X, p.Z) / 1000f;
                legs.Add(new RouteLeg { FromX = x, FromZ = z, ToX = p.X, ToZ = p.Z, Number = i + 1, Km = km, Eta = Eta(km, speed), Action = p.Action });
                x = p.X;
                z = p.Z;
            }
            if (d.Loop == RouteLoop.Loop && d.Count > 2)
                legs.Add(new RouteLeg { FromX = x, FromZ = z, ToX = d[0].X, ToZ = d[0].Z, Km = float.NaN, Eta = float.NaN, Closing = true });
        }

        /// <summary>"3 · 18.0 km · 1:30"; no ETA when unknown; empty for an unlabelled leg.</summary>
        public static string Label(in RouteLeg l)
        {
            if (l.Number <= 0) return "";
            string s = l.Number.ToString(CultureInfo.InvariantCulture) + " · " + WmcText.Km(l.Km * 1000f);
            return float.IsNaN(l.Eta) ? s : s + " · " + WmcText.Clock(l.Eta);
        }

        private static float Eta(float km, float speed) => speed > 1f ? km * 1000f / speed : float.NaN;

        private static float Distance(float x0, float z0, float x1, float z1)
        {
            float dx = x1 - x0, dz = z1 - z0;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
