using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Reads a live <see cref="Airbase"/> into the Pure <see cref="AirbaseSample"/> (the same shape as the
    /// nomodkit airbase dump): runways with their entry and exit points, the taxi network's roads, hangars, service
    /// points (private) and vertical pads, global metres. A private member that cannot be read gives an empty list.</summary>
    internal static class AirbaseAdapter
    {
        private static readonly FieldInfo ServicePointsField = AccessTools.Field(typeof(Airbase), "servicePoints");
        private static readonly FieldInfo CarrierDoorsField = AccessTools.Field(typeof(Hangar), "waitForOpenBeforeSpawn");

        public static AirbaseSample Read(Airbase a)
        {
            var sample = new AirbaseSample
            {
                Name = a.name,
                Center = a.center != null ? a.center.GlobalPosition().ToVec3() : a.transform.GlobalPosition().ToVec3(),
                Radius = a.GetRadius(),
                Attached = a.AttachedAirbase,
            };
            var runways = new List<RunwaySample>();
            if (a.runways != null)
                foreach (Airbase.Runway r in a.runways)
                {
                    if (r == null || r.Start == null || r.End == null) continue;
                    runways.Add(new RunwaySample
                    {
                        Index = r.index,
                        Start = r.Start.GlobalPosition().ToVec3(),
                        End = r.End.GlobalPosition().ToVec3(),
                        Width = r.GetWidth(),
                        Length = r.Length,
                        Reversable = r.Reversable, Takeoff = r.Takeoff, Landing = r.Landing, Arrestor = r.Arrestor, SkiJump = r.SkiJump,
                        Entries = Poses(r.entryPoints),
                        Exits = Poses(r.exitPoints),
                    });
                }
            sample.Runways = runways.ToArray();

            var roads = new List<Vec3[]>();
            RoadPathfinding.RoadNetwork network = a.GetTaxiNetwork();
            if (network != null && network.roads != null)
                foreach (RoadPathfinding.Road road in network.roads)
                {
                    if (road == null || road.points == null || road.points.Count < 2) continue;
                    var points = new Vec3[road.points.Count];
                    for (int i = 0; i < points.Length; i++) points[i] = road.points[i].ToVec3();
                    roads.Add(points);
                }
            sample.Roads = roads.ToArray();

            var hangars = new List<HangarSample>();
            if (a.hangars != null)
                for (int i = 0; i < a.hangars.Count; i++)
                {
                    Hangar h = a.hangars[i];
                    Transform spawn = h != null ? h.GetSpawnTransform() : null;
                    if (spawn == null) continue;
                    hangars.Add(new HangarSample
                    {
                        Index = i,
                        Spawn = new Pose(spawn.GlobalPosition().ToVec3(), spawn.forward.ToVec3()),
                        Available = h.Available,
                        CarrierDoors = CarrierDoorsField != null && CarrierDoorsField.GetValue(h) is bool doors && doors,
                    });
                }
            sample.Hangars = hangars.ToArray();
            sample.ServicePoints = Poses(ServicePointsField?.GetValue(a) as Transform[]);

            var pads = new List<Pose>();
            if (a.verticalLandingPoints != null)
                foreach (Airbase.VerticalLandingPoint v in a.verticalLandingPoints)
                    if (v != null && v.point != null) pads.Add(new Pose(v.point.GlobalPosition().ToVec3(), v.point.forward.ToVec3()));
            sample.Pads = pads.ToArray();
            return sample;
        }

        private static Pose[] Poses(Transform[] transforms)
        {
            var poses = new List<Pose>();
            if (transforms != null)
                foreach (Transform t in transforms)
                    if (t != null) poses.Add(new Pose(t.GlobalPosition().ToVec3(), t.forward.ToVec3()));
            return poses.ToArray();
        }
    }
}
