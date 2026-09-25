using System;

namespace WingCommand
{
    /// <summary>The wing's task (spec M4 §2.3). Only <see cref="Apply"/> changes it: it validates the order (a wing to give
    /// it to, the points it needs, on the map, altitudes in range), keeps the lead flying from where it is (a new lead
    /// starts at the anchor, else at the wing's centroid) and logs the start (and the cancel of the task it replaces).
    /// <see cref="Step"/> flies the lead every tick and decides at 2 Hz: a point reached (orbit it for its action's seconds,
    /// then the next; a point is also reached once the lead has passed it along the leg; a patrol turns at its ends or loops), a task complete (the last point, an orbit's or hold's
    /// duration) → its declared follow-on, the wing gone (nobody left who is not recovering) → failed, back to Form.
    /// Speed: the task's, else <see cref="CruiseFraction"/> of the slowest cruise, never under
    /// (RolePolicy.RecoverFactor + <see cref="MinSpeedMargin"/>) × the highest loaded minimum for a wing with jets. Height: the point's, else the
    /// task's, else the lead's when the task started, never under the wing's floor + <see cref="LeadClearance"/>.</summary>
    internal sealed class WingPlanner
    {
        /// <summary>The lead's floor for a wing with jets is <see cref="RolePolicy.RecoverFactor"/> +
        /// <see cref="MinSpeedMargin"/> times the highest loaded minimum, so jets can leave high cover behind it (review M4a
        /// I3).</summary>
        public static float DecisionPeriod = 0.5f, ArriveRadius = 800f, CruiseFraction = 0.85f, MinSpeedMargin = 0.1f;
        /// <summary>How close a LAND or CARGO point is reached: the helicopters settle around the lead (review P4 I1).</summary>
        public static float SettleArriveRadius = 150f;
        public static float LeadClearance = 150f, MaxAltitude = 15000f;
        public const int MaxPoints = 16;

        private int direction = 1;
        private Vec3 legFrom;
        private float since, sinceDecision, orbitUntil, startAltitude;
        private bool orbitingPoint;
        private ArrivalAction arrived;

        public WingTask Current { get; private set; }
        public TaskLead Lead { get; private set; }
        public bool Active => Current != null;
        /// <summary>The point the lead flies to (Move, Route, Patrol), −1 in an orbit or hold.</summary>
        public int Leg { get; private set; } = -1;

        /// <summary>The element this planner flies (0 = A); its task events carry it (spec WMC program §3.3).</summary>
        public readonly int Element;

        public WingPlanner(int element = 0) => Element = element;

        public void Reset()
        {
            Current = null;
            Lead = null;
            Leg = -1;
            orbitingPoint = false;
            arrived = ArrivalAction.None;
        }

        public AnchorSample Sample() => Lead.Sample();

        public OrderResult Apply(WingTask task, in WingSnapshot wing, float time, WingEventRing events) =>
            Apply(task, wing, time, events, TransitionReason.Commanded);

        /// <summary>A task change logged with <paramref name="reason"/> (a plan's or a rule's order, spec WMC rebuild R3).</summary>
        public OrderResult Apply(WingTask task, in WingSnapshot wing, float time, WingEventRing events, TransitionReason reason)
        {
            if (task == null || task.Kind == TaskKind.Form)
            {
                if (Current != null) Log(events, time, WingEventKind.TaskCancelled, reason, Current.Kind);
                Reset();
                return OrderResult.Ok;
            }
            string why = Validate(task, wing);
            if (why != null) return OrderResult.Refused(why);
            if (Current != null) Log(events, time, WingEventKind.TaskCancelled, reason, Current.Kind);
            if (Lead == null) Lead = NewLead(wing);
            else if (Lead.CanHover != wing.AllRotary)
                Lead = new TaskLead(Lead.Position, Lead.Velocity, wing.AllRotary, Vec3.FromHeading(Lead.HeadingDeg), Lead.BankDeg);
            Current = task;
            Leg = task.Kind == TaskKind.Move || task.Kind == TaskKind.Route || task.Kind == TaskKind.Patrol ? 0 : -1;
            direction = 1;
            legFrom = Lead.Position;
            since = sinceDecision = 0f;
            orbitingPoint = false;
            arrived = ArrivalAction.None;
            startAltitude = wing.WingAirborne ? wing.Centroid.Y : Lead.Position.Y;
            Log(events, time, WingEventKind.TaskStarted, reason, task.Kind);
            return OrderResult.Ok;
        }

        /// <summary>A new lead takes over from a flying anchor (its velocity, nose and bank; review M4a I2), else from the
        /// wing's members (a ship, a vehicle, an aircraft on the ground, or no anchor), at a speed the wing can fly.</summary>
        private static TaskLead NewLead(in WingSnapshot wing)
        {
            bool fromAnchor = wing.AnchorPresent && wing.AnchorAirborne;
            Vec3 pos = fromAnchor ? wing.AnchorPos : wing.Centroid, vel = fromAnchor ? wing.AnchorVel : wing.MeanVel;
            Vec3 fwd = fromAnchor ? wing.AnchorFwd : wing.MeanVel.Horizontal;
            float min = Floor(wing), cruise = wing.CruiseSpeed > 0f ? Math.Max(min, wing.CruiseSpeed) : Math.Max(min, vel.Length);
            float speed = Scalar.Clamp(vel.Length, min, cruise);
            Vec3 dir = vel.SqrLength > 1f ? vel.Normalized : fwd.Horizontal.SqrLength > 1e-4f ? fwd.Horizontal.Normalized : Vec3.Forward;
            return new TaskLead(pos, dir * speed, wing.AllRotary, fwd, fromAnchor ? wing.AnchorBankDeg : 0f);
        }

        private static float Floor(in WingSnapshot wing) =>
            wing.AllRotary ? 0f : (RolePolicy.RecoverFactor + MinSpeedMargin) * wing.MinSpeed;

        private static string Validate(WingTask t, in WingSnapshot w)
        {
            if (w.Members - w.Recovering <= 0) return "no wingman to give it to";
            int count = t.Points != null ? t.Points.Length : 0;
            if (t.Kind == TaskKind.Patrol && count < 2) return "a patrol needs two points";
            if (count < 1) return "it needs one point";
            bool many = t.Kind == TaskKind.Route || t.Kind == TaskKind.Patrol;
            if (count > (many ? MaxPoints : 1)) return many ? $"at most {MaxPoints} points" : "it takes one point";
            foreach (Waypoint p in t.Points)
            {
                if (!Scalar.IsFinite(p.X) || !Scalar.IsFinite(p.Z)) return "a point is not a number";
                if (w.MapHalfX > 0f && (Math.Abs(p.X) > w.MapHalfX || Math.Abs(p.Z) > w.MapHalfZ)) return "a point is off the map";
                if (!float.IsNaN(p.Altitude) && (p.Altitude < 0f || p.Altitude > MaxAltitude)) return "a point's altitude is out of range";
            }
            if (!float.IsNaN(t.Altitude) && (t.Altitude < 0f || t.Altitude > MaxAltitude)) return "the altitude is out of range";
            return null;
        }

        /// <summary>The LAND/CARGO action of the point just reached, once; the service settles the element's helicopters.</summary>
        public ArrivalAction TakeArrival()
        {
            ArrivalAction a = arrived;
            arrived = ArrivalAction.None;
            return a;
        }

        /// <summary>Moves a Move, Route or Patrol on to its next point now (spec WMC program §4 SKIP), without calling this one
        /// reached; a Route on its last point completes. False when there is no route to skip.</summary>
        public bool Skip(in WingSnapshot wing, float time, WingEventRing events)
        {
            if (Current == null || Leg < 0) return false;
            orbitingPoint = false;
            Advance(wing, time, events);
            return true;
        }

        public void Step(in WingSnapshot wing, float time, float dt, WingEventRing events)
        {
            if (Current == null) return;
            since += dt;
            sinceDecision += dt;
            if (sinceDecision >= DecisionPeriod - 1e-6f)
            {
                sinceDecision = 0f;
                Decide(wing, time, events);
                if (Current == null) return;
            }
            Fly(wing, dt);
        }

        private void Decide(in WingSnapshot wing, float time, WingEventRing events)
        {
            if (wing.Members - wing.Recovering <= 0)
            {
                Log(events, time, WingEventKind.TaskFailed, TransitionReason.NoWing, Current.Kind);
                Reset();
                return;
            }
            switch (Current.Kind)
            {
                case TaskKind.Move:
                case TaskKind.Route:
                case TaskKind.Patrol:
                    if (orbitingPoint)
                    {
                        if (time >= orbitUntil)
                        {
                            orbitingPoint = false;
                            Advance(wing, time, events);
                        }
                        return;
                    }
                    Waypoint p = Current.Points[Leg];
                    bool settle = p.Action == ArrivalAction.Land || p.Action == ArrivalAction.Cargo;
                    if (!Reached(Point(p), settle ? SettleArriveRadius : ArriveRadius)) return;
                    Log(events, time, WingEventKind.WaypointReached, TransitionReason.None, Current.Kind);
                    if (p.Action == ArrivalAction.Land || p.Action == ArrivalAction.Cargo)
                    {
                        // Raised after advancing: completing the task (its follow-on) must not swallow the arrival.
                        Advance(wing, time, events);
                        arrived = p.Action;
                        return;
                    }
                    if (p.Action == ArrivalAction.Orbit && p.Seconds > 0f)
                    {
                        orbitingPoint = true;
                        orbitUntil = time + p.Seconds;
                        return;
                    }
                    Advance(wing, time, events);
                    return;
                default:
                    if (Current.Seconds > 0f && since >= Current.Seconds) Complete(wing, time, events);
                    return;
            }
        }

        /// <summary>Within <see cref="ArriveRadius"/> of the point, or past it along the leg (a point inside the lead's turn
        /// is never flown over).</summary>
        private bool Reached(Vec3 point, float radius)
        {
            if ((Lead.Position - point).Horizontal.Length < radius) return true;
            Vec3 leg = (point - legFrom).Horizontal;
            float length = leg.Length;
            return length > 1f && Vec3.Dot((Lead.Position - legFrom).Horizontal, leg * (1f / length)) >= length;
        }

        private void Advance(in WingSnapshot wing, float time, WingEventRing events)
        {
            int n = Current.Points.Length;
            legFrom = Point(Current.Points[Leg]);
            if (Current.Kind == TaskKind.Patrol)
            {
                if (Current.Loop) Leg = (Leg + 1) % n;
                else
                {
                    if (Leg + direction < 0 || Leg + direction >= n) direction = -direction;
                    Leg += direction;
                }
                return;
            }
            if (Leg + 1 < n) Leg++;
            else Complete(wing, time, events);
        }

        private void Complete(in WingSnapshot wing, float time, WingEventRing events)
        {
            WingTask done = Current;
            Log(events, time, WingEventKind.TaskCompleted, TransitionReason.None, done.Kind);
            FollowOn then = done.Then != FollowOn.Default ? done.Then
                : done.Kind == TaskKind.Move || done.Kind == TaskKind.Route ? FollowOn.Orbit : FollowOn.Form;
            Current = null;
            if (then == FollowOn.Orbit)
            {
                WingTask orbit = WingTask.Orbit(done.Points[done.Points.Length - 1]);
                orbit.Scout = done.Scout;
                orbit.Altitude = done.Altitude;
                orbit.Speed = done.Speed;
                Apply(orbit, wing, time, events, TransitionReason.FollowOn);
                return;
            }
            Reset();
        }

        private void Fly(in WingSnapshot wing, float dt)
        {
            switch (Current.Kind)
            {
                case TaskKind.Move:
                case TaskKind.Route:
                case TaskKind.Patrol:
                {
                    Waypoint p = Current.Points[Leg];
                    float speed = TaskSpeed(wing, p.Speed);
                    if (orbitingPoint) Lead.FlyOrbit(Point(p), TaskLead.OrbitRadius(speed), Current.Left, Altitude(p, wing), speed, dt);
                    else Lead.FlyLeg(legFrom, Point(p), Altitude(p, wing), speed, dt);
                    return;
                }
                default:
                {
                    Waypoint p = Current.Points[0];
                    float speed = TaskSpeed(wing, p.Speed);
                    if (Current.Kind == TaskKind.Hold && Lead.CanHover) Lead.FlyHover(Point(p), Current.HeadingDeg, Altitude(p, wing), speed, dt);
                    else Lead.FlyOrbit(Point(p), TaskLead.OrbitRadius(speed), Current.Left, Altitude(p, wing), speed, dt);
                    return;
                }
            }
        }

        private float TaskSpeed(in WingSnapshot wing, float pointSpeed)
        {
            float cruise = wing.CruiseSpeed > 0f ? wing.CruiseSpeed : Math.Max(Lead.Speed, 1f);
            float min = Floor(wing);
            float wanted = !float.IsNaN(pointSpeed) ? pointSpeed : !float.IsNaN(Current.Speed) ? Current.Speed : CruiseFraction * cruise;
            return Scalar.Clamp(wanted, min, Math.Max(min, cruise));
        }

        private float Altitude(in Waypoint p, in WingSnapshot wing)
        {
            float y = !float.IsNaN(p.Altitude) ? p.Altitude : !float.IsNaN(Current.Altitude) ? Current.Altitude : startAltitude;
            return float.IsNaN(wing.FloorY) ? y : Math.Max(y, wing.FloorY + LeadClearance);
        }

        private Vec3 Point(in Waypoint p) => new Vec3(p.X, Lead.Position.Y, p.Z);

        private void Log(WingEventRing events, float time, WingEventKind kind, TransitionReason reason, TaskKind task) =>
            events?.Push(new WingEvent { Time = time, Member = -1, Kind = kind, Reason = reason, Task = task, Element = (byte)Element });
    }
}
