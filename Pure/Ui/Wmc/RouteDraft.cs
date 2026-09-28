using System.Collections.Generic;
using System.Globalization;

namespace WingCommand
{
    internal enum RouteLoop : byte { Once, Loop, PingPong }

    /// <summary>The route editor's draft (spec WMC program §4 ORDERS): up to 16 points clicked on the map, each with an
    /// altitude, a speed (NaN = AUTO) and an arrival action; ONCE is a Move/Route, LOOP and PING-PONG a Patrol.</summary>
    internal sealed class RouteDraft
    {
        public const int MaxPoints = WingPlanner.MaxPoints;
        public const float OrbitSeconds = 120f;
        public static readonly float[] Altitudes = { float.NaN, 150f, 300f, 600f, 1000f, 1500f, 2000f, 3000f, 4500f, 6000f, 8000f, 10000f };
        public static readonly float[] Speeds = { float.NaN, 60f, 80f, 100f, 125f, 150f, 175f, 200f, 250f, 300f };

        private readonly List<Waypoint> points = new List<Waypoint>(MaxPoints);

        public int Count => points.Count;
        public Waypoint this[int i] => points[i];
        public int Selected { get; private set; } = -1;
        public RouteLoop Loop { get; private set; }
        /// <summary>Altitude and speed a new point takes (the steppers edit these while no point is selected).</summary>
        public float Altitude { get; private set; } = float.NaN;
        public float Speed { get; private set; } = float.NaN;

        public bool Add(float x, float z)
        {
            if (points.Count >= MaxPoints) return false;
            Waypoint p = Waypoint.At(x, z);
            p.Altitude = Altitude;
            p.Speed = Speed;
            points.Add(p);
            Selected = points.Count - 1;
            return true;
        }

        public void Undo()
        {
            if (points.Count == 0) return;
            points.RemoveAt(points.Count - 1);
            Selected = points.Count - 1;
        }

        public void Clear()
        {
            points.Clear();
            Selected = -1;
            Loop = RouteLoop.Once;
        }

        /// <summary>Selects point <paramref name="i"/>; the selected point again unselects.</summary>
        public void Select(int i) => Selected = i == Selected || i < 0 || i >= points.Count ? -1 : i;

        public void RemoveSelected()
        {
            if (Selected < 0) return;
            points.RemoveAt(Selected);
            Selected = -1;
        }

        public void StepAltitude(int dir)
        {
            if (Selected < 0)
            {
                Altitude = Step(Altitudes, Altitude, dir);
                return;
            }
            Waypoint p = points[Selected];
            p.Altitude = Step(Altitudes, p.Altitude, dir);
            points[Selected] = p;
        }

        public void StepSpeed(int dir)
        {
            if (Selected < 0)
            {
                Speed = Step(Speeds, Speed, dir);
                return;
            }
            Waypoint p = points[Selected];
            p.Speed = Step(Speeds, p.Speed, dir);
            points[Selected] = p;
        }

        /// <summary>The selected point's arrival action: — → ORBIT 2 MIN → LAND → CARGO → —.</summary>
        public void CycleAction()
        {
            if (Selected < 0) return;
            Waypoint p = points[Selected];
            p.Action = p.Action == ArrivalAction.Cargo ? ArrivalAction.None : p.Action + 1;
            p.Seconds = p.Action == ArrivalAction.Orbit ? OrbitSeconds : 0f;
            points[Selected] = p;
        }

        public void CycleLoop() => Loop = Loop == RouteLoop.PingPong ? RouteLoop.Once : Loop + 1;

        public void Load(Waypoint[] from, RouteLoop loop)
        {
            points.Clear();
            for (int i = 0; from != null && i < from.Length && i < MaxPoints; i++) points.Add(from[i]);
            Loop = loop;
            Selected = -1;
        }

        public Waypoint[] ToArray() => points.ToArray();

        public bool TryTask(out WingTask task, out string reason)
        {
            task = null;
            reason = null;
            if (points.Count == 0)
            {
                reason = "Add points on the map first";
                return false;
            }
            if (Loop != RouteLoop.Once && points.Count < 2)
            {
                reason = "A patrol needs two points";
                return false;
            }
            Waypoint[] p = points.ToArray();
            task = Loop != RouteLoop.Once ? WingTask.Patrol(Loop == RouteLoop.Loop, p) : p.Length == 1 ? WingTask.Move(p[0]) : WingTask.Route(p);
            return true;
        }

        /// <summary>One step along <paramref name="ladder"/> (index 0 = NaN, AUTO); a value between rungs snaps to the next
        /// rung in the press direction; the ends hold.</summary>
        public static float Step(float[] ladder, float value, int dir)
        {
            if (float.IsNaN(value)) return dir > 0 ? ladder[1] : ladder[0];
            if (dir > 0)
            {
                for (int i = 1; i < ladder.Length; i++)
                    if (ladder[i] > value) return ladder[i];
                return ladder[ladder.Length - 1];
            }
            for (int i = ladder.Length - 1; i >= 1; i--)
                if (ladder[i] < value) return ladder[i];
            return ladder[0];
        }

        /// <summary>The first row of a <paramref name="shown"/>-row window over <paramref name="count"/> rows that keeps
        /// <paramref name="focus"/> in view (a little below the top).</summary>
        public static int Window(int count, int shown, int focus)
        {
            if (count <= shown || focus < 0) return 0;
            int first = focus - 3;
            if (first < 0) first = 0;
            if (first > count - shown) first = count - shown;
            return first;
        }

        public static string AltitudeText(float m) =>
            float.IsNaN(m) ? "AUTO" : m.ToString("0", CultureInfo.InvariantCulture) + " m";

        public static string SpeedText(float mps) =>
            float.IsNaN(mps) ? "AUTO" : (mps * 3.6f).ToString("0", CultureInfo.InvariantCulture) + " km/h";

        public static string ActionText(in Waypoint p)
        {
            switch (p.Action)
            {
                case ArrivalAction.Orbit: return "ORBIT " + ((int)(p.Seconds / 60f)).ToString(CultureInfo.InvariantCulture) + " MIN";
                case ArrivalAction.Land: return "LAND";
                case ArrivalAction.Cargo: return "CARGO";
                default: return "—";
            }
        }

        public static string LoopText(RouteLoop l) => l == RouteLoop.PingPong ? "PING-PONG" : l == RouteLoop.Loop ? "LOOP" : "ONCE";
    }
}
