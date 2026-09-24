namespace WingCommand
{
    internal enum TaskKind : byte { Form, Move, Route, Patrol, Orbit, Hold }

    /// <summary>What the wing does when a task completes (spec M4 §2.2); Default is the kind's own: Move and Route orbit
    /// their last point, Orbit and Hold with a duration go back to Form.</summary>
    internal enum FollowOn : byte { Default, Orbit, Form }

    internal enum ArrivalAction : byte { None, Orbit }

    /// <summary>A task point: x/z in world metres; Altitude (world Y) and Speed NaN when the task's own apply; an action
    /// on arrival (orbit it for Seconds).</summary>
    internal struct Waypoint
    {
        public float X, Z, Altitude, Speed;
        public ArrivalAction Action;
        public float Seconds;

        public static Waypoint At(float x, float z) => new Waypoint { X = x, Z = z, Altitude = float.NaN, Speed = float.NaN };
    }

    /// <summary>An order for the whole wing (spec M4 §2.2).</summary>
    internal sealed class WingTask
    {
        public TaskKind Kind;
        public Waypoint[] Points = new Waypoint[0];
        /// <summary>Patrol: round the loop back to the first point, else back and forth.</summary>
        public bool Loop;
        /// <summary>Orbit: counter-clockwise.</summary>
        public bool Left;
        /// <summary>Orbit/Hold: how long, 0 until replaced.</summary>
        public float Seconds;
        /// <summary>Hold: the heading a hovering wing faces.</summary>
        public float HeadingDeg;
        /// <summary>World Y and m/s for the whole task, NaN for the defaults.</summary>
        public float Altitude = float.NaN, Speed = float.NaN;
        public FollowOn Then;

        public static WingTask Form() => new WingTask { Kind = TaskKind.Form };
        public static WingTask Move(Waypoint to) => new WingTask { Kind = TaskKind.Move, Points = new[] { to } };
        public static WingTask Route(params Waypoint[] points) => new WingTask { Kind = TaskKind.Route, Points = points };
        public static WingTask Patrol(bool loop, params Waypoint[] points) =>
            new WingTask { Kind = TaskKind.Patrol, Points = points, Loop = loop };
        public static WingTask Orbit(Waypoint at, float seconds = 0f) =>
            new WingTask { Kind = TaskKind.Orbit, Points = new[] { at }, Seconds = seconds };
        public static WingTask Hold(Waypoint at, float headingDeg, float seconds = 0f) =>
            new WingTask { Kind = TaskKind.Hold, Points = new[] { at }, HeadingDeg = headingDeg, Seconds = seconds };
    }

    internal struct OrderResult
    {
        public bool Accepted;
        public string Reason;

        public static OrderResult Ok => new OrderResult { Accepted = true };
        public static OrderResult Refused(string reason) => new OrderResult { Reason = reason };
    }

    /// <summary>What the planner sees of the wing each tick (the engine or the FlightSim fills it).</summary>
    internal struct WingSnapshot
    {
        /// <summary>Members in the wing, and those of them recovering (RTB/Refit: not the task's).</summary>
        public int Members, Recovering;
        /// <summary>The wing's anchor now (the player or an escortee): a new lead starts from it.</summary>
        public Vec3 AnchorPos, AnchorVel;
        public bool AnchorPresent;
        /// <summary>The members' mean position and velocity (a new lead starts there when the anchor is gone).</summary>
        public Vec3 Centroid, MeanVel;
        /// <summary>The slowest member's cruise speed and the highest loaded minimum speed, m/s.</summary>
        public float CruiseSpeed, MinSpeed;
        /// <summary>Every member is a helicopter: the lead may hover.</summary>
        public bool AllRotary;
        /// <summary>The wing's terrain floor (world Y, NaN: unknown).</summary>
        public float FloorY;
        /// <summary>Half the map's size along x and z (0: unknown, no bounds check).</summary>
        public float MapHalfX, MapHalfZ;
    }
}
