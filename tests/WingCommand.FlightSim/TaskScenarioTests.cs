using System;
using Xunit;

namespace WingCommand.FlightSim
{
    /// <summary>Spec M4 §3: D1 order churn, D2 leash from the task's anchor, D3 declared follow-ons only.</summary>
    public class TaskScenarioTests
    {
        private static SimWing FourShip(out VirtualLeader leader)
        {
            leader = new VirtualLeader(new Vec3(0f, 2000f, 0f), 200f, 0f);
            return SimWing.InSlots(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard, 3);
        }

        private static WingSnapshot Snapshot(SimWing sim, VirtualLeader leader)
        {
            Vec3 sum = Vec3.Zero, vel = Vec3.Zero;
            for (int i = 0; i < sim.Plants.Length; i++)
            {
                sum += sim.Plants[i].Position;
                vel += sim.Plants[i].Velocity;
            }
            float n = sim.Plants.Length;
            return new WingSnapshot
            {
                Members = sim.Plants.Length, AnchorPos = leader.Position, AnchorVel = leader.Velocity, AnchorPresent = true,
                Centroid = sum * (1f / n), MeanVel = vel * (1f / n), CruiseSpeed = sim.Profile.CruiseSpeed,
                MinSpeed = sim.Profile.MinimumSpeed(1f), FloorY = float.NaN,
            };
        }

        private static Waypoint Ahead(VirtualLeader l, float forward, float right)
        {
            Vec3 f = Vec3.FromHeading(l.HeadingDeg), r = new Vec3(f.Z, 0f, -f.X);
            Vec3 p = l.Position + f * forward + r * right;
            return Waypoint.At(p.X, p.Z);
        }

        [Fact]
        public void D1_OrdersEveryFiveSecondsNeverResetAMemberOrBreakSeparation()
        {
            SimWing sim = FourShip(out VirtualLeader leader);
            var planner = new WingPlanner();
            sim.Anchor = () => planner.Active ? planner.Sample() : leader.Sample();
            float minSep = float.MaxValue, maxJump = 0f;
            Vec3 last = Vec3.Zero;
            bool had = false;
            for (int i = 0; i < 180 * 60; i++)
            {
                if (i % (5 * 60) == 0 && i < 120 * 60)
                {
                    int k = i / (5 * 60) % 4;
                    WingTask t = k == 0 ? WingTask.Move(Ahead(leader, 20000f, 3000f))
                        : k == 1 ? WingTask.Orbit(Ahead(leader, 5000f, 8000f))
                        : k == 2 ? WingTask.Patrol(false, Ahead(leader, 10000f, -5000f), Ahead(leader, 25000f, -5000f))
                        : WingTask.Route(Ahead(leader, 8000f, 0f), Ahead(leader, 16000f, 6000f), Ahead(leader, 24000f, 0f));
                    Assert.True(planner.Apply(t, Snapshot(sim, leader), sim.Time, sim.Events).Accepted);
                }
                planner.Step(Snapshot(sim, leader), sim.Time, SimWing.Dt, sim.Events);
                leader.Step(0f, SimWing.Dt);
                sim.Step();
                if (i > 60 * 60) minSep = Math.Min(minSep, sim.MinSeparation(false));
                if (planner.Active)
                {
                    if (had) maxJump = Math.Max(maxJump, (planner.Lead.Position - last).Length);
                    last = planner.Lead.Position;
                    had = true;
                }
            }
            Assert.True(minSep >= sim.SafeRadius, $"closest pair {minSep:0} m");
            Assert.True(maxJump <= 300f * SimWing.Dt * 1.5f, $"the lead jumped {maxJump:0.0} m in a tick");
            for (int i = 0; i < sim.Events.Count; i++)
                Assert.False(sim.Events[i].Kind == WingEventKind.BehaviourChanged && sim.Events[i].Reason == TransitionReason.Commanded,
                    "no member was forced to re-enter a behaviour");
            Assert.Equal(0, sim.Events.CountOf(WingEventKind.CollisionEmergency));
        }

        [Fact]
        public void D2_APatrolHoldsTheWingWhileThePlayerFliesAway()
        {
            SimWing sim = FourShip(out VirtualLeader leader);
            var planner = new WingPlanner();
            sim.Anchor = () => planner.Active ? planner.Sample() : leader.Sample();
            Waypoint a = Waypoint.At(30000f, 0f), b = Waypoint.At(30000f, 20000f);
            planner.Apply(WingTask.Patrol(false, a, b), Snapshot(sim, leader), 0f, sim.Events);
            for (int i = 0; i < 400 * 60; i++)
            {
                planner.Step(Snapshot(sim, leader), sim.Time, SimWing.Dt, sim.Events);
                leader.Step(0f, SimWing.Dt, 250f, 0f);
                sim.Step();
            }
            WingSnapshot end = Snapshot(sim, leader);
            Assert.True(leader.Position.Z > 90000f, "the player is far away");
            Assert.True(Math.Abs(end.Centroid.X - 30000f) < 8000f && end.Centroid.Z > -8000f && end.Centroid.Z < 28000f,
                $"the wing is at {end.Centroid}");
        }

        [Fact]
        public void D3_AMoveFollowsOnIntoAnOrbitAndNothingElse()
        {
            SimWing sim = FourShip(out VirtualLeader leader);
            var planner = new WingPlanner();
            sim.Anchor = () => planner.Active ? planner.Sample() : leader.Sample();
            Waypoint to = Ahead(leader, 15000f, 0f);
            planner.Apply(WingTask.Move(to), Snapshot(sim, leader), 0f, sim.Events);
            for (int i = 0; i < 600 * 60; i++)
            {
                planner.Step(Snapshot(sim, leader), sim.Time, SimWing.Dt, sim.Events);
                leader.Step(20f, SimWing.Dt);
                sim.Step();
            }
            Assert.Equal(2, sim.Events.CountOf(WingEventKind.TaskStarted));
            Assert.Equal(1, sim.Events.CountOf(WingEventKind.TaskCompleted));
            Assert.Equal(0, sim.Events.CountOf(WingEventKind.TaskCancelled) + sim.Events.CountOf(WingEventKind.TaskFailed));
            Assert.Equal(TaskKind.Orbit, planner.Current.Kind);
            float radius = TaskLead.OrbitRadius(WingPlanner.CruiseFraction * sim.Profile.CruiseSpeed);
            Vec3 c = Snapshot(sim, leader).Centroid;
            Assert.True((c - new Vec3(to.X, c.Y, to.Z)).Horizontal.Length < 1.5f * radius + 1000f, $"the wing is at {c}");
        }
        [Fact]
        public void DuringATaskTheWingKeepsClearOfThePlayerStandingWhereAMemberWas()
        {
            // Review M4a C2: a takeover mid-task spawns the player's aircraft where member #2 was (slot 0); the next member
            // then flies slot 0 — straight into the player, who was outside the wing's collision check while the task's
            // (empty) lead was its body 0.
            var player = new VirtualLeader(new Vec3(0f, 2000f, 0f), 180f, 0f);
            FormationDefinition def = SimFormations.Get("finger-four-right");
            float spacing = def.ClampSpacing(FormationCatalog.Standard);
            Vec3 SlotAt(int i)
            {
                SlotDef slot = SlotSolver.SlotFor(def, i);
                float right = slot.Right * spacing;
                return player.Position + TurnFrame.Offset(player.Velocity, Vec3.Forward, 0f, right, slot.Aft * spacing,
                    slot.Up * FormationCatalog.StackMetres,
                    TurnFrame.RollFollowWeight(TurnFrame.Reach(right, slot.Aft * spacing, slot.Up * FormationCatalog.StackMetres), slot.RollFollow));
            }
            // The task's lead flies where the player flew; the player (the takeover copy) now stands in slot 0's place and
            // flies straight at the same speed; the one member left starts in slot 1 and is given slot 0.
            Vec3 playerStart = SlotAt(0), memberStart = SlotAt(1);
            var sim = new SimWing(player, def, spacing, new[] { memberStart }, 180f, 0f);
            var copy = new VirtualLeader(playerStart, 180f, 0f);
            var planner = new WingPlanner();
            var snapshot = new WingSnapshot
            {
                Members = 1, AnchorPos = player.Position, AnchorVel = player.Velocity, AnchorPresent = true,
                Centroid = memberStart, MeanVel = player.Velocity, CruiseSpeed = 180f / WingPlanner.CruiseFraction,
                MinSpeed = 60f, FloorY = float.NaN,
            };
            WingTask move = WingTask.Move(Waypoint.At(0f, 200000f));
            move.Speed = 180f;
            Assert.True(planner.Apply(move, snapshot, 0f, sim.Events).Accepted);
            sim.Anchor = () => planner.Sample();
            sim.Body0 = () => copy.Sample();
            float closest = float.MaxValue;
            for (int i = 0; i < 90 * 60; i++)
            {
                planner.Step(snapshot, sim.Time, SimWing.Dt, sim.Events);
                copy.Step(0f, SimWing.Dt);
                sim.Step();
                closest = Math.Min(closest, (sim.Plants[0].Position - copy.Position).Length);
            }
            // Its slot is exactly where the player stands, so the slot's pull and the bias balance just inside the safe
            // radius: no contact (bounding spheres + 5 m); the engine also ends the task on a takeover, so the wing forms on
            // the new aircraft instead.
            Assert.True(closest > 2f * sim.Profile.MaxRadius + 5f, $"the member came within {closest:0} m of the player");
        }
    }
}
