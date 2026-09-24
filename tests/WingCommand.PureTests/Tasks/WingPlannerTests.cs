using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class WingPlannerTests
    {
        private const float Dt = 1f / 30f;

        private static WingSnapshot Wing(int members = 2) => new WingSnapshot
        {
            Members = members, AnchorPos = new Vec3(0f, 1000f, 0f), AnchorVel = new Vec3(0f, 0f, 150f), AnchorPresent = true,
            Centroid = new Vec3(-50f, 1000f, -80f), MeanVel = new Vec3(0f, 0f, 150f), CruiseSpeed = 200f, MinSpeed = 80f,
            FloorY = float.NaN,
        };

        private static float Run(WingPlanner p, WingSnapshot w, WingEventRing events, float from, float seconds,
            Func<bool> until = null)
        {
            float t = from;
            for (int i = 0; i < seconds * 30 && (until == null || !until()); i++, t += Dt) p.Step(w, t, Dt, events);
            return t;
        }

        [Fact]
        public void AMoveStartsTheLeadFromTheAnchorAndLogsTheStart()
        {
            var p = new WingPlanner();
            var events = new WingEventRing();
            Assert.True(p.Apply(WingTask.Move(Waypoint.At(0f, 20000f)), Wing(), 0f, events).Accepted);
            Assert.True(p.Active);
            Assert.Equal(new Vec3(0f, 1000f, 0f), p.Lead.Position);
            Assert.Equal(1, events.CountOf(WingEventKind.TaskStarted));
            Assert.Equal(TaskKind.Move, events[0].Task);
            Assert.Equal(TransitionReason.Commanded, events[0].Reason);
        }

        [Fact]
        public void WithTheAnchorGoneTheLeadStartsAtTheWingsCentroid()
        {
            var p = new WingPlanner();
            WingSnapshot w = Wing();
            w.AnchorPresent = false;
            p.Apply(WingTask.Move(Waypoint.At(0f, 20000f)), w, 0f, new WingEventRing());
            Assert.Equal(w.Centroid, p.Lead.Position);
        }

        [Fact]
        public void RefusalsCarryTheReason()
        {
            var p = new WingPlanner();
            var events = new WingEventRing();
            Assert.Contains("no wingman", p.Apply(WingTask.Move(Waypoint.At(0f, 1f)), Wing(0), 0f, events).Reason);
            Assert.Contains("two points", p.Apply(WingTask.Patrol(false, Waypoint.At(0f, 1f)), Wing(), 0f, events).Reason);
            Assert.Contains("one point", p.Apply(WingTask.Route(), Wing(), 0f, events).Reason);
            WingSnapshot small = Wing();
            small.MapHalfX = small.MapHalfZ = 10000f;
            Assert.Contains("off the map", p.Apply(WingTask.Move(Waypoint.At(0f, 20000f)), small, 0f, events).Reason);
            var high = Waypoint.At(0f, 1000f);
            high.Altitude = 20000f;
            Assert.Contains("altitude", p.Apply(WingTask.Move(high), Wing(), 0f, events).Reason);
            Assert.False(p.Active);
            Assert.Equal(0, events.Count);
        }

        [Fact]
        public void AMoveCompletesAtItsPointAndFollowsOnIntoAnOrbitThere()
        {
            var p = new WingPlanner();
            var events = new WingEventRing();
            p.Apply(WingTask.Move(Waypoint.At(0f, 12000f)), Wing(), 0f, events);
            Run(p, Wing(), events, 0f, 300f, () => p.Current.Kind == TaskKind.Orbit);
            Assert.Equal(TaskKind.Orbit, p.Current.Kind);
            Assert.Equal(12000f, p.Current.Points[0].Z);
            Assert.Equal(1, events.CountOf(WingEventKind.TaskCompleted));
            Assert.Equal(2, events.CountOf(WingEventKind.TaskStarted));
            WingEvent last = events[events.Count - 1];
            Assert.Equal(TaskKind.Orbit, last.Task);
            Assert.Equal(TransitionReason.FollowOn, last.Reason);
        }

        [Fact]
        public void ARouteVisitsEveryPointInOrderThenOrbitsTheLast()
        {
            var p = new WingPlanner();
            var events = new WingEventRing();
            p.Apply(WingTask.Route(Waypoint.At(0f, 8000f), Waypoint.At(8000f, 12000f), Waypoint.At(8000f, 20000f)), Wing(), 0f, events);
            var legs = new List<int>();
            float t = 0f;
            for (int i = 0; i < 600 * 30 && p.Current.Kind == TaskKind.Route; i++, t += Dt)
            {
                p.Step(Wing(), t, Dt, events);
                if (p.Current.Kind == TaskKind.Route && (legs.Count == 0 || legs[legs.Count - 1] != p.Leg)) legs.Add(p.Leg);
            }
            Assert.Equal(new[] { 0, 1, 2 }, legs.ToArray());
            Assert.Equal(3, events.CountOf(WingEventKind.WaypointReached));
            Assert.Equal(TaskKind.Orbit, p.Current.Kind);
            Assert.Equal(20000f, p.Current.Points[0].Z);
        }

        [Fact]
        public void APatrolGoesBackAndForthAndNeverCompletes()
        {
            var p = new WingPlanner();
            var events = new WingEventRing();
            p.Apply(WingTask.Patrol(false, Waypoint.At(0f, 6000f), Waypoint.At(0f, 16000f)), Wing(), 0f, events);
            var legs = new List<int>();
            float t = 0f;
            for (int i = 0; i < 900 * 30; i++, t += Dt)
            {
                p.Step(Wing(), t, Dt, events);
                if (legs.Count == 0 || legs[legs.Count - 1] != p.Leg) legs.Add(p.Leg);
            }
            Assert.True(legs.Count >= 4, string.Join(",", legs));
            for (int i = 0; i < legs.Count; i++) Assert.Equal(i % 2, legs[i]);
            Assert.Equal(0, events.CountOf(WingEventKind.TaskCompleted));
            Assert.Equal(TaskKind.Patrol, p.Current.Kind);
        }

        [Fact]
        public void AnOrbitWithADurationEndsInForm()
        {
            var p = new WingPlanner();
            var events = new WingEventRing();
            p.Apply(WingTask.Orbit(Waypoint.At(0f, 3000f), 60f), Wing(), 0f, events);
            Run(p, Wing(), events, 0f, 61f);
            Assert.False(p.Active);
            Assert.Null(p.Lead);
            Assert.Equal(1, events.CountOf(WingEventKind.TaskCompleted));
        }

        [Fact]
        public void ANewOrderKeepsTheLeadFlyingFromWhereItIs()
        {
            var p = new WingPlanner();
            var events = new WingEventRing();
            p.Apply(WingTask.Move(Waypoint.At(0f, 50000f)), Wing(), 0f, events);
            Run(p, Wing(), events, 0f, 30f);
            TaskLead lead = p.Lead;
            Vec3 at = lead.Position;
            p.Apply(WingTask.Orbit(Waypoint.At(5000f, 0f)), Wing(), 30f, events);
            Assert.Same(lead, p.Lead);
            Assert.Equal(at, p.Lead.Position);
            Assert.Equal(1, events.CountOf(WingEventKind.TaskCancelled));
        }

        [Fact]
        public void AWingWithNobodyLeftFailsItsTask()
        {
            var p = new WingPlanner();
            var events = new WingEventRing();
            p.Apply(WingTask.Move(Waypoint.At(0f, 50000f)), Wing(), 0f, events);
            WingSnapshot gone = Wing();
            gone.Recovering = 2;
            Run(p, gone, events, 0f, 1f);
            Assert.False(p.Active);
            Assert.Equal(1, events.CountOf(WingEventKind.TaskFailed));
            Assert.Equal(TransitionReason.NoWing, events[events.Count - 1].Reason);
        }

        [Fact]
        public void ResumeDuringAWaypointOrbitLeavesNoOrbitBehind()
        {
            var p = new WingPlanner();
            var events = new WingEventRing();
            var first = Waypoint.At(0f, 6000f);
            first.Action = ArrivalAction.Orbit;
            first.Seconds = 600f;
            p.Apply(WingTask.Route(first, Waypoint.At(0f, 30000f)), Wing(), 0f, events);
            float t = Run(p, Wing(), events, 0f, 200f, () => events.CountOf(WingEventKind.WaypointReached) > 0);
            Run(p, Wing(), events, t, 20f);
            Assert.Equal(0, p.Leg);
            Assert.True(p.Apply(WingTask.Form(), Wing(), t + 20f, events).Accepted);
            Assert.False(p.Active);
            p.Apply(WingTask.Route(Waypoint.At(0f, 90000f), Waypoint.At(0f, 95000f)), Wing(), t + 21f, events);
            Run(p, Wing(), events, t + 21f, 30f);
            Assert.Equal(0, p.Leg);
            Assert.True(p.Lead.Speed > 100f, "the new route flies, it does not orbit");
            Assert.True(Math.Abs(p.Lead.BankDeg) < 20f);
        }

        [Fact]
        public void TheLeadFliesTheSlowestCruiseShareButNeverUnderTheSafeMinimum()
        {
            var p = new WingPlanner();
            p.Apply(WingTask.Move(Waypoint.At(0f, 90000f)), Wing(), 0f, new WingEventRing());
            Run(p, Wing(), new WingEventRing(), 0f, 60f);
            Assert.Equal(WingPlanner.CruiseFraction * 200f, p.Lead.Speed, 1);
            var q = new WingPlanner();
            WingTask slow = WingTask.Move(Waypoint.At(0f, 90000f));
            slow.Speed = 50f;
            q.Apply(slow, Wing(), 0f, new WingEventRing());
            Run(q, Wing(), new WingEventRing(), 0f, 60f);
            Assert.Equal(WingPlanner.MinSpeedFactor * 80f, q.Lead.Speed, 1);
        }

        [Fact]
        public void WithoutAnAltitudeTheLeadKeepsItsStartAltitudeAboveTheFloor()
        {
            var p = new WingPlanner();
            p.Apply(WingTask.Move(Waypoint.At(0f, 90000f)), Wing(), 0f, new WingEventRing());
            Run(p, Wing(), new WingEventRing(), 0f, 60f);
            Assert.Equal(1000f, p.Lead.Position.Y, 0);
            WingSnapshot hills = Wing();
            hills.FloorY = 1500f;
            Run(p, hills, new WingEventRing(), 60f, 300f);
            Assert.True(p.Lead.Position.Y >= 1500f + WingPlanner.LeadClearance - 5f, $"lead at {p.Lead.Position.Y}");
        }

        [Fact]
        public void HoldHoversOnlyForAHelicopterWing()
        {
            var jets = new WingPlanner();
            jets.Apply(WingTask.Hold(Waypoint.At(0f, 3000f), 90f), Wing(), 0f, new WingEventRing());
            Run(jets, Wing(), new WingEventRing(), 0f, 120f);
            Assert.True(jets.Lead.Speed > 100f, "a jet wing orbits the hold point");
            var helos = new WingPlanner();
            WingSnapshot rotary = Wing();
            rotary.AllRotary = true;
            rotary.CruiseSpeed = 60f;
            rotary.MinSpeed = 0f;
            rotary.AnchorVel = new Vec3(0f, 0f, 40f);
            helos.Apply(WingTask.Hold(Waypoint.At(0f, 1500f), 90f), rotary, 0f, new WingEventRing());
            Run(helos, rotary, new WingEventRing(), 0f, 180f);
            Assert.True(helos.Lead.Speed < 1f);
            Assert.True((helos.Lead.Position - new Vec3(0f, 0f, 1500f)).Horizontal.Length < 30f);
        }
    }
}
