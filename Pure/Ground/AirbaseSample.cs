using System;
using System.Collections.Generic;
using System.Globalization;

namespace WingCommand
{
    /// <summary>A position and a facing (unit vector), global metres.</summary>
    internal struct Pose
    {
        public Vec3 Pos, Fwd;

        public Pose(Vec3 pos, Vec3 fwd)
        {
            Pos = pos;
            Fwd = fwd;
        }
    }

    internal sealed class RunwaySample
    {
        public int Index;
        public Vec3 Start, End;
        public float Width, Length;
        public bool Reversable, Takeoff, Landing, Arrestor, SkiJump;
        public Pose[] Entries = new Pose[0], Exits = new Pose[0];

        /// <summary>Unit direction of travel: Start → End, or End → Start when reversed.</summary>
        public Vec3 Direction(bool reverse) => ((reverse ? Start - End : End - Start).Horizontal).Normalized;
    }

    internal sealed class HangarSample
    {
        public int Index;
        public Pose Spawn;
        public bool Available, CarrierDoors;
        public string[] Types = new string[0];
    }

    /// <summary>What ground operations need of one airbase, read once: runways, taxi roads (polylines), hangars,
    /// service points and vertical pads, global metres. The engine fills it from a live <c>Airbase</c>; tests load the
    /// nomodkit <c>airbases.json</c> dump (<see cref="FromDumpJson"/>). An attached airbase (a carrier) moves: its
    /// positions are where it was when read.</summary>
    internal sealed class AirbaseSample
    {
        public string Name;
        public Vec3 Center;
        public float Radius;
        public bool Attached;
        public RunwaySample[] Runways = new RunwaySample[0];
        public Vec3[][] Roads = new Vec3[0][];
        public HangarSample[] Hangars = new HangarSample[0];
        public Pose[] ServicePoints = new Pose[0], Pads = new Pose[0];

        /// <summary>The airbases of a nomodkit dump; entries the dump could not read (<c>{name, error}</c>) and
        /// malformed JSON give nothing rather than an exception.</summary>
        public static List<AirbaseSample> FromDumpJson(string json)
        {
            var result = new List<AirbaseSample>();
            if (!MiniJson.TryParse(json, out object root) || !(root is Dictionary<string, object> dump) ||
                !MiniJson.TryGetList(dump, "airbases", out List<object> airbases)) return result;
            foreach (object item in airbases)
            {
                if (!(item is Dictionary<string, object> a) || a.ContainsKey("error")) continue;
                try
                {
                    result.Add(Read(a));
                }
                catch (Exception)
                {
                    // One unreadable airbase never hides the others.
                }
            }
            return result;
        }

        private static AirbaseSample Read(Dictionary<string, object> a) => new AirbaseSample
        {
            Name = MiniJson.GetString(a, "name"),
            Center = V(a, "center"),
            Radius = F(a, "radius"),
            Attached = B(a, "attached"),
            Runways = Map(a, "runways", r => new RunwaySample
            {
                Index = (int)F(r, "index"),
                Start = V(r, "start"),
                End = V(r, "end"),
                Width = F(r, "width"),
                Length = F(r, "length"),
                Reversable = B(r, "reversable"),
                Takeoff = B(r, "takeoff"),
                Landing = B(r, "landing"),
                Arrestor = B(r, "arrestor"),
                SkiJump = B(r, "skiJump"),
                Entries = Map(r, "entryPoints", PoseOf),
                Exits = Map(r, "exitPoints", PoseOf),
            }),
            Roads = Map(a, "taxiRoads", r => MiniJson.TryGetList(r, "points", out List<object> points)
                ? points.ConvertAll(p => Vec((List<object>)p)).ToArray()
                : new Vec3[0]),
            Hangars = Map(a, "hangars", h => new HangarSample
            {
                Index = (int)F(h, "index"),
                Spawn = new Pose(V(h, "position"), V(h, "forward")),
                Available = B(h, "available"),
                CarrierDoors = B(h, "carrierDoors"),
                Types = MiniJson.TryGetList(h, "types", out List<object> types) ? types.ConvertAll(t => t as string).ToArray() : new string[0],
            }),
            ServicePoints = Map(a, "servicePoints", PoseOf),
            Pads = Map(a, "verticalPads", PoseOf),
        };

        private static Pose PoseOf(Dictionary<string, object> p) => new Pose(V(p, "position"), V(p, "forward"));

        private static T[] Map<T>(Dictionary<string, object> d, string key, Func<Dictionary<string, object>, T> read)
        {
            if (!MiniJson.TryGetList(d, key, out List<object> list)) return new T[0];
            var result = new List<T>(list.Count);
            foreach (object item in list)
                if (item is Dictionary<string, object> entry) result.Add(read(entry));
            return result.ToArray();
        }

        private static Vec3 V(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out object v) && v is List<object> xyz ? Vec(xyz) : Vec3.Zero;

        private static Vec3 Vec(List<object> xyz) => new Vec3(Num(xyz[0]), Num(xyz[1]), Num(xyz[2]));

        private static float F(Dictionary<string, object> d, string key) => d.TryGetValue(key, out object v) ? Num(v) : 0f;

        private static bool B(Dictionary<string, object> d, string key) => d.TryGetValue(key, out object v) && v is bool b && b;

        private static float Num(object v) => v == null ? 0f : Convert.ToSingle(v, CultureInfo.InvariantCulture);
    }
}
