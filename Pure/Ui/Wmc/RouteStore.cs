using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WingCommand
{
    internal sealed class SavedRoute
    {
        public string Name;
        public RouteLoop Loop;
        public Waypoint[] Points;
    }

    /// <summary>Saved routes (spec WMC program §4 SAVE/LOAD), at most <see cref="Max"/>, as <c>routes.user.json</c>. Reading
    /// skips any route that is empty, too long or off the map, with a reason; a file that is not JSON loads nothing.</summary>
    internal sealed class RouteStore
    {
        public const int Max = 12;
        public const float MaxCoord = 2e6f;
        private readonly List<SavedRoute> routes = new List<SavedRoute>();

        public IReadOnlyList<SavedRoute> Routes => routes;

        public bool Save(RouteDraft d, out string name)
        {
            name = null;
            if (d == null || d.Count == 0 || routes.Count >= Max) return false;
            name = FreeName();
            routes.Add(new SavedRoute { Name = name, Loop = d.Loop, Points = d.ToArray() });
            return true;
        }

        public bool Remove(int i)
        {
            if (i < 0 || i >= routes.Count) return false;
            routes.RemoveAt(i);
            return true;
        }

        public void Load(int i, RouteDraft into)
        {
            if (i >= 0 && i < routes.Count) into.Load(routes[i].Points, routes[i].Loop);
        }

        private string FreeName()
        {
            for (int n = 1; ; n++)
            {
                string name = "ROUTE " + n.ToString(CultureInfo.InvariantCulture);
                bool used = false;
                foreach (SavedRoute r in routes) used |= r.Name == name;
                if (!used) return name;
            }
        }

        public string ToJson()
        {
            var sb = new StringBuilder("{\"routes\":[");
            for (int i = 0; i < routes.Count; i++)
            {
                SavedRoute r = routes[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(MiniJson.Escape(r.Name)).Append("\",\"loop\":\"").Append(r.Loop).Append("\",\"points\":[");
                for (int k = 0; k < r.Points.Length; k++)
                {
                    Waypoint p = r.Points[k];
                    if (k > 0) sb.Append(',');
                    sb.Append("{\"x\":").Append(Num(p.X)).Append(",\"z\":").Append(Num(p.Z)).Append(",\"alt\":").Append(Num(p.Altitude))
                        .Append(",\"speed\":").Append(Num(p.Speed)).Append(",\"action\":\"").Append(p.Action).Append("\",\"seconds\":")
                        .Append(Num(p.Seconds)).Append('}');
                }
                sb.Append("]}");
            }
            return sb.Append("]}").ToString();
        }

        private static string Num(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>The store in <paramref name="json"/>; each skipped route or a file that is not JSON adds to
        /// <paramref name="errors"/>. Null or empty text (no file yet) is an empty store.</summary>
        public static RouteStore FromJson(string json, List<string> errors)
        {
            var store = new RouteStore();
            if (string.IsNullOrWhiteSpace(json)) return store;
            if (!MiniJson.TryParse(json, out object root) || !(root is Dictionary<string, object> top) || !MiniJson.TryGetList(top, "routes", out List<object> list))
            {
                errors.Add("not a routes file; nothing loaded");
                return store;
            }
            foreach (object o in list)
            {
                if (store.routes.Count >= Max)
                {
                    errors.Add("more than " + Max + " routes; the rest ignored");
                    break;
                }
                if (!(o is Dictionary<string, object> d))
                {
                    errors.Add("a route that is not an object");
                    continue;
                }
                string name = (MiniJson.GetString(d, "name") ?? "").Trim();
                if (name.Length > ElementRoster.MaxName) name = name.Substring(0, ElementRoster.MaxName);
                string why = Points(d, out Waypoint[] points);
                if (why != null)
                {
                    errors.Add((name.Length > 0 ? name : "a route") + ": " + why);
                    continue;
                }
                Enum.TryParse(MiniJson.GetString(d, "loop") ?? "", out RouteLoop loop);
                if (!Enum.IsDefined(typeof(RouteLoop), loop)) loop = RouteLoop.Once;
                store.routes.Add(new SavedRoute { Name = name.Length > 0 ? name : store.FreeName(), Loop = loop, Points = points });
            }
            return store;
        }

        private static string Points(Dictionary<string, object> d, out Waypoint[] points)
        {
            points = null;
            if (!MiniJson.TryGetList(d, "points", out List<object> list) || list.Count == 0) return "no points";
            if (list.Count > RouteDraft.MaxPoints) return "more than " + RouteDraft.MaxPoints + " points";
            points = new Waypoint[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is Dictionary<string, object> p)) return "a point that is not an object";
                float x = Number(p, "x"), z = Number(p, "z");
                if (float.IsNaN(x) || float.IsNaN(z) || Math.Abs(x) > MaxCoord || Math.Abs(z) > MaxCoord) return "a point off the map";
                Waypoint w = Waypoint.At(x, z);
                w.Altitude = Number(p, "alt");
                w.Speed = Number(p, "speed");
                Enum.TryParse(MiniJson.GetString(p, "action") ?? "", out ArrivalAction action);
                w.Action = Enum.IsDefined(typeof(ArrivalAction), action) ? action : ArrivalAction.None;
                float seconds = Number(p, "seconds");
                w.Seconds = float.IsNaN(seconds) || seconds < 0f ? 0f : seconds;
                points[i] = w;
            }
            return null;
        }

        /// <summary>A finite number, else NaN (missing, null, text, infinite).</summary>
        private static float Number(Dictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out object v) || v == null) return float.NaN;
            double x;
            switch (v)
            {
                case double dv: x = dv; break;
                case long lv: x = lv; break;
                case int iv: x = iv; break;
                default: return float.NaN;
            }
            return double.IsNaN(x) || double.IsInfinity(x) || Math.Abs(x) > float.MaxValue ? float.NaN : (float)x;
        }
    }
}
