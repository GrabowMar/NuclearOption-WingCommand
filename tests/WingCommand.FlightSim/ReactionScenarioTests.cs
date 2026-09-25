using System;
using Xunit;

namespace WingCommand.FlightSim
{
    /// <summary>Spec WMC rebuild R3 (design wmc-rebuild/maneuver.md §4): the MANEUVER row's reactions flown by the production
    /// brain and pipeline on the sim plant — they turn or climb as ordered, keep the floor and the separation, yield to a
    /// missile, and come back to the slot on the element's task.</summary>
    public class ReactionScenarioTests
    {
        private const float Dt = SimWing.Dt;

        private static float Turn(Vec3 from, Vec3 to) => Scalar.Wrap180(Vec3.HeadingDeg(to) - Vec3.HeadingDeg(from));

        private static SimWing TwoShip(float altitude = 3000f, Func<float, float, float> terrain = null)
        {
            var leader = new VirtualLeader(new Vec3(0f, altitude, 0f), 200f, 0f);
            SimWing w = SimWing.InSlots(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard, 2);
            w.Terrain = terrain;
            return w;
        }

        private static void Fly(SimWing w, float seconds)
        {
            for (int i = 0; i < seconds * 60f; i++)
            {
                w.Leader.Step(0f, Dt);
                w.Step();
            }
        }

        private static string React(SimWing w, int i, ReactionOrder o, float agl = float.NaN) =>
            w.Pilots[i].React(o, w.State(i), float.IsNaN(agl) ? w.State(i).RadarAlt : agl, w.Profile, w.Time, w.Events);

        [Fact]
        public void M1_ABrokenElementKeepsItsTaskAndReturnsToItsSlots()
        {
            ElementSimWing w = ElementSimWing.InSlots(new VirtualLeader(new Vec3(0f, 3000f, 0f), 200f, 0f),
                SimFormations.Get("finger-four-right"), FormationCatalog.Standard, 4);
            for (int i = 0; i < 5 * 60; i++) w.Step();
            // #4 and #5 fly a task of their own toward an orbit 60 km ahead and 3 km right, settled in their slots.
            Assert.Equal(1, w.Detach(WingTask.Orbit(Waypoint.At(3000f, 60000f)), 2u, 3u));
            for (int i = 0; i < 60 * 60; i++) w.Step();
            Assert.Equal(BehaviourId.StationKeep, w.Pilots[2].Mind.Current);
            Assert.Equal(BehaviourId.StationKeep, w.Pilots[3].Mind.Current);
            WingTask task = w.PlannerOf(1).Current;
            int leg = w.PlannerOf(1).Leg;
            float t0 = w.Time;
            Vec3 h2 = w.Plants[2].Velocity, h3 = w.Plants[3].Velocity;
            foreach (int m in new[] { 2, 3 })
            {
                Assert.Null(w.Pilots[m].React(new ReactionOrder { Kind = ReactionKind.BreakRight }, w.State(m), w.State(m).RadarAlt, w.Profile, w.Time, w.Events));
                Assert.Equal(BehaviourId.React, w.Pilots[m].Mind.Current);
            }
            float min = float.MaxValue, turn2 = 0f, turn3 = 0f, back = float.NaN;
            for (int i = 0; i < 120 * 60; i++)
            {
                w.Step();
                min = Math.Min(min, w.MinSeparation());
                if (w.Time - t0 <= 6f)
                {
                    turn2 = Math.Max(turn2, Turn(h2, w.Plants[2].Velocity));
                    turn3 = Math.Max(turn3, Turn(h3, w.Plants[3].Velocity));
                }
                if (float.IsNaN(back) && w.Pilots[2].Mind.Current == BehaviourId.StationKeep && w.Pilots[3].Mind.Current == BehaviourId.StationKeep)
                    back = w.Time - t0 - ReactionManeuver.BreakSeconds;
            }
            Assert.True(turn2 >= 60f && turn3 >= 60f, $"turned {turn2:0}° and {turn3:0}° in 6 s");
            Assert.Same(task, w.PlannerOf(1).Current);
            Assert.True(w.PlannerOf(1).Leg >= leg);
            for (int i = 0; i < w.Events.Count; i++)
                if (w.Events[i].Time > t0)
                    Assert.True(w.Events[i].Kind != WingEventKind.TaskCancelled && w.Events[i].Kind != WingEventKind.TaskStarted,
                        $"{w.Events[i].Kind} at {w.Events[i].Time:0.0} s");
            Assert.False(float.IsNaN(back), "the pair never came back to its slots");
            Assert.True(back <= 90f, $"back in the slots {back:0} s after the break");
            Assert.True(min >= w.SafeRadius, $"separation {min:0.0} m < {w.SafeRadius:0.0} m");
            Assert.Equal(0, w.Events.CountOf(WingEventKind.CollisionEmergency));
        }

        [Fact]
        public void M2_ASplitPairTurnsApartAndRejoins()
        {
            SimWing w = TwoShip();
            Fly(w, 15f);
            Vec3 lead = w.Wing.Frame.Leader.Pos, track = w.Wing.Frame.Leader.Track;
            Vec3 middle = (w.State(0).Pos + w.State(1).Pos) * 0.5f;
            Vec3 h0 = w.Plants[0].Velocity, h1 = w.Plants[1].Velocity;
            float t0 = w.Time;
            for (int m = 0; m < 2; m++)
            {
                int side = ReactionManeuver.SplitSide(w.State(m).Pos, middle, true, lead, track, m);
                Assert.Null(React(w, m, new ReactionOrder { Kind = ReactionKind.Split, Side = side }));
            }
            float min = float.MaxValue, turn0 = 0f, turn1 = 0f, back = float.NaN;
            for (int i = 0; i < 110 * 60; i++)
            {
                w.Leader.Step(0f, Dt);
                w.Step();
                min = Math.Min(min, w.MinSeparation());
                if (w.Time - t0 <= ReactionManeuver.SplitSeconds)
                {
                    float a = Turn(h0, w.Plants[0].Velocity), b = Turn(h1, w.Plants[1].Velocity);
                    if (Math.Abs(a) > Math.Abs(turn0)) turn0 = a;
                    if (Math.Abs(b) > Math.Abs(turn1)) turn1 = b;
                }
                if (float.IsNaN(back) && w.Pilots[0].Mind.Current == BehaviourId.StationKeep && w.Pilots[1].Mind.Current == BehaviourId.StationKeep)
                    back = w.Time - t0 - ReactionManeuver.SplitSeconds;
            }
            Assert.True(Math.Sign(turn0) == -Math.Sign(turn1), $"turned {turn0:0}° and {turn1:0}°: not apart");
            Assert.True(Math.Abs(turn0) >= 45f && Math.Abs(turn1) >= 45f, $"turned {turn0:0}° and {turn1:0}°");
            Assert.False(float.IsNaN(back), "the pair never came back to its slots");
            Assert.True(back <= 90f, $"back {back:0} s after the split");
            Assert.True(min >= w.SafeRadius, $"separation {min:0.0} m");
        }

        [Fact]
        public void M3_ABreakTowardRisingTerrainHoldsClearance()
        {
            // The ground rises to the west (left of a northbound wing) 0.2 m per metre; the wing starts 250 m above it.
            Func<float, float, float> slope = (x, z) => 2750f - 0.2f * x;
            SimWing w = TwoShip(3000f, slope);
            Fly(w, 15f);
            Assert.Equal("too low (below 120 m)", React(w, 0, new ReactionOrder { Kind = ReactionKind.BreakLeft }, agl: 100f));
            for (int m = 0; m < 2; m++) Assert.Null(React(w, m, new ReactionOrder { Kind = ReactionKind.BreakLeft }));
            float minClearance = float.MaxValue;
            for (int i = 0; i < 40 * 60; i++)
            {
                w.Leader.Step(0f, Dt);
                w.Step();
                for (int m = 0; m < 2; m++) minClearance = Math.Min(minClearance, w.ClearanceOf(m));
            }
            Assert.True(minClearance > 0f, $"clearance {minClearance:0} m");
            for (int m = 0; m < 2; m++) Assert.True(w.Events.CountOf(WingEventKind.GcasActivated, m) <= 1, $"#{m} GCAS {w.Events.CountOf(WingEventKind.GcasActivated, m)}×");
        }

        [Fact]
        public void M4_AMissileDuringABreakPreemptsItAndItIsNotResumed()
        {
            SimWing w = TwoShip();
            Fly(w, 15f);
            Assert.Null(React(w, 0, new ReactionOrder { Kind = ReactionKind.BreakRight }));
            Fly(w, 2f);
            Vec3 missile = w.Plants[0].Position + new Vec3(0f, 0f, 8000f);
            float start = w.Time, end = float.NaN;
            bool reactAgain = false, preempted = false;
            BehaviourId last = w.Pilots[0].Mind.Current;
            for (int i = 0; i < 120 * 60; i++)
            {
                if (float.IsNaN(end))
                {
                    Vec3 to = w.Plants[0].Position - missile;
                    if (to.Length < 150f || w.Time - start > 20f) end = w.Time;
                    else
                    {
                        Vec3 vel = to.Normalized * 700f;
                        missile += vel * Dt;
                        w.Pilots[0].Threat = new MissileThreat { Present = true, Pos = missile, Vel = vel, Seeker = MissileSeeker.Radar };
                    }
                }
                if (!float.IsNaN(end)) w.Pilots[0].Threat = default;
                w.Leader.Step(0f, Dt);
                w.Step();
                BehaviourId now = w.Pilots[0].Mind.Current;
                preempted |= last == BehaviourId.React && now == BehaviourId.Defend;
                reactAgain |= last != BehaviourId.React && now == BehaviourId.React;
                last = now;
            }
            Assert.True(preempted, "the missile never took the member out of its break");
            Assert.False(reactAgain, "the break resumed");
            Assert.False(w.Pilots[0].Reaction.Active);
            Assert.Equal(BehaviourId.StationKeep, w.Pilots[0].Mind.Current);
            bool logged = false;
            for (int i = 0; i < w.Events.Count; i++)
                logged |= w.Events[i].From == BehaviourId.React && w.Events[i].To == BehaviourId.Defend && w.Events[i].Reason == TransitionReason.MissileInbound;
            Assert.True(logged);
        }

        [Fact]
        public void M5_BeamAndPullUpFlyTheirGeometry()
        {
            SimWing w = TwoShip();
            Fly(w, 15f);
            float y0 = w.Plants[1].Position.Y, t0 = w.Time, gained = 0f, minEas = float.MaxValue;
            Vec3 threat = w.Plants[0].Position + Vec3.FromHeading(45f, 30000f);
            Assert.Null(React(w, 0, new ReactionOrder { Kind = ReactionKind.Beam, HasThreat = true, Threat = threat }));
            Assert.Null(React(w, 1, new ReactionOrder { Kind = ReactionKind.PullUp }));
            float beamTrack = float.NaN, back0 = float.NaN, back1 = float.NaN;
            for (int i = 0; i < 150 * 60; i++)
            {
                w.Leader.Step(0f, Dt);
                w.Step();
                float t = w.Time - t0;
                if (t <= ReactionManeuver.PullUpSeconds)
                {
                    gained = Math.Max(gained, w.Plants[1].Position.Y - y0);
                    minEas = Math.Min(minEas, w.State(1).Eas);
                }
                if (float.IsNaN(beamTrack) && t >= 15f) beamTrack = Vec3.HeadingDeg(w.Plants[0].Velocity);
                if (float.IsNaN(back0) && t > 20f && w.Pilots[0].Mind.Current == BehaviourId.StationKeep) back0 = t;
                if (float.IsNaN(back1) && t > 1f && w.Pilots[1].Mind.Current == BehaviourId.StationKeep) back1 = t;
            }
            Assert.True(Math.Abs(Scalar.Wrap180(beamTrack - 315f)) <= 15f, $"beam track {beamTrack:0}°");
            Assert.True(gained >= 300f, $"pull-up gained {gained:0} m in {ReactionManeuver.PullUpSeconds:0} s");
            Assert.True(minEas >= w.Profile.MinimumSpeed(1f), $"EAS fell to {minEas:0} m/s");
            Assert.False(float.IsNaN(back0), "the beaming member never came back");
            Assert.False(float.IsNaN(back1), "the climbing member never came back");
        }
    }
}
