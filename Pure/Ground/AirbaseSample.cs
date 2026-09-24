using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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

        /// <summary><paramref name="p"/> is on the runway: between its ends and within half its width plus
        /// <paramref name="margin"/> of the centreline.</summary>
        public bool Contains(Vec3 p, float margin)
        {
            Vec3 dir = Direction(false), d = (p - Start).Horizontal;
            float along = Vec3.Dot(d, dir);
            return along >= -margin && along <= Length + margin && (d - dir * along).Length < 0.5f * Width + margin;
        }
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

        /// <summary>Samples written in the dump format <see cref="FromDumpJson"/> reads (a field as the mod saw it in
        /// game, replayed in the FlightSim).</summary>
        public static string ToDumpJson(string mission, IEnumerable<AirbaseSample> fields)
        {
            var sb = new StringBuilder();
            sb.Append("{\"mission\": \"").Append(MiniJson.Escape(mission ?? "")).Append("\", \"airbases\": [");
            bool first = true;
            foreach (AirbaseSample a in fields)
            {
                sb.Append(first ? "\n  " : ",\n  ");
                first = false;
                sb.Append("{\"name\": \"").Append(MiniJson.Escape(a.Name ?? "")).Append("\", \"center\": ");
                Write(sb, a.Center);
                sb.Append(", \"radius\": ").Append(Num(a.Radius)).Append(", \"attached\": ").Append(a.Attached ? "true" : "false");
                sb.Append(",\n   \"runways\": [");
                for (int i = 0; i < a.Runways.Length; i++)
                {
                    RunwaySample r = a.Runways[i];
                    sb.Append(i > 0 ? ", " : "").Append("{\"index\": ").Append(r.Index).Append(", \"start\": ");
                    Write(sb, r.Start);
                    sb.Append(", \"end\": ");
                    Write(sb, r.End);
                    sb.Append(", \"width\": ").Append(Num(r.Width)).Append(", \"length\": ").Append(Num(r.Length))
                        .Append(", \"reversable\": ").Append(Bool(r.Reversable)).Append(", \"takeoff\": ").Append(Bool(r.Takeoff))
                        .Append(", \"landing\": ").Append(Bool(r.Landing)).Append(", \"arrestor\": ").Append(Bool(r.Arrestor))
                        .Append(", \"skiJump\": ").Append(Bool(r.SkiJump)).Append(", \"entryPoints\": ");
                    Write(sb, r.Entries);
                    sb.Append(", \"exitPoints\": ");
                    Write(sb, r.Exits);
                    sb.Append('}');
                }
                sb.Append("],\n   \"taxiRoads\": [");
                for (int i = 0; i < a.Roads.Length; i++)
                {
                    sb.Append(i > 0 ? ", " : "").Append("{\"points\": [");
                    for (int k = 0; k < a.Roads[i].Length; k++)
                    {
                        if (k > 0) sb.Append(", ");
                        Write(sb, a.Roads[i][k]);
                    }
                    sb.Append("]}");
                }
                sb.Append("],\n   \"hangars\": [");
                for (int i = 0; i < a.Hangars.Length; i++)
                {
                    HangarSample h = a.Hangars[i];
                    sb.Append(i > 0 ? ", " : "").Append("{\"index\": ").Append(h.Index).Append(", \"position\": ");
                    Write(sb, h.Spawn.Pos);
                    sb.Append(", \"forward\": ");
                    Write(sb, h.Spawn.Fwd);
                    sb.Append(", \"available\": ").Append(Bool(h.Available)).Append(", \"carrierDoors\": ").Append(Bool(h.CarrierDoors))
                        .Append(", \"types\": [");
                    for (int k = 0; k < h.Types.Length; k++)
                        sb.Append(k > 0 ? ", " : "").Append('"').Append(MiniJson.Escape(h.Types[k] ?? "")).Append('"');
                    sb.Append("]}");
                }
                sb.Append("],\n   \"servicePoints\": ");
                Write(sb, a.ServicePoints);
                sb.Append(", \"verticalPads\": ");
                Write(sb, a.Pads);
                sb.Append('}');
            }
            return sb.Append("\n]}\n").ToString();
        }

        private static string Num(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static string Bool(bool b) => b ? "true" : "false";

        private static void Write(StringBuilder sb, Vec3 v) =>
            sb.Append('[').Append(Num(v.X)).Append(", ").Append(Num(v.Y)).Append(", ").Append(Num(v.Z)).Append(']');

        private static void Write(StringBuilder sb, Pose[] poses)
        {
            sb.Append('[');
            for (int i = 0; i < poses.Length; i++)
            {
                sb.Append(i > 0 ? ", " : "").Append("{\"position\": ");
                Write(sb, poses[i].Pos);
                sb.Append(", \"forward\": ");
                Write(sb, poses[i].Fwd);
                sb.Append('}');
            }
            sb.Append(']');
        }

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
