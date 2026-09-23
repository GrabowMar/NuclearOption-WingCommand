using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class RejoinPlannerTests
    {
        private const float Dt = 1f / 60f;
        private const float Spacing = 80f;

        private static LeaderEstimate Leader(float speed = 200f, float rate = 0f) => new LeaderEstimate
        {
            Pos = new Vec3(0f, 2000f, 0f), Vel = new Vec3(0f, 0f, speed), Acc = new Vec3(speed * rate, 0f, 0f),
            Track = Vec3.Forward, TurnRate = rate, Flying = true,
        };

        private static SlotTarget Slot(in LeaderEstimate leader, float right = -80f, float aft = 80f) =>
            new SlotTarget { Ref = TurnFrame.Evaluate(leader, 0f, 0f, right, aft, 0f, 0f), Lateral = right };

        private static Vec3 PreSlot(in SlotTarget slot) =>
            slot.Ref.Pos - Vec3.Forward * Spacing - Vec3.Up * RejoinPlanner.PreSlotLow;

        private static RejoinOutput Hold(RejoinPlanner planner, Vec3 at, in SlotTarget slot, in LeaderEstimate leader,
            bool clear, float seconds)
        {
            AircraftState s = TestStates.Flying(at, leader.Vel);
            RejoinOutput o = default;
            for (int i = 0; i < (int)Math.Round(seconds / Dt); i++) o = planner.Step(s, slot, leader, 2, Spacing, 255f, clear, Dt);
            return o;
        }

        [Fact]
        public void FarMemberAimsAtTheTurnPredictedRendezvousOnItsLane()
        {
            LeaderEstimate leader = Leader(200f, 0.05f);
            SlotTarget slot = Slot(leader);
            AircraftState s = TestStates.Flying(new Vec3(-80f, 1900f, -5000f), new Vec3(0f, 0f, 230f));
            RejoinOutput o = new RejoinPlanner().Step(s, slot, leader, 2, Spacing, 255f, true, Dt);
            Assert.Equal(2000f - 2f * RejoinPlanner.LaneStep, o.Ref.Pos.Y, 2);
            Assert.True(o.Ref.Pos.X > PreSlot(slot).X + 100f, $"rendezvous {o.Ref.Pos} vs pre-slot {PreSlot(slot)}");
            Assert.Equal(0f, o.Sigma, 3);
        }

        [Fact]
        public void EachSlotNumberRejoinsOnItsOwnLane()
        {
            LeaderEstimate leader = Leader();
            SlotTarget slot = Slot(leader);
            AircraftState s = TestStates.Flying(new Vec3(-80f, 1900f, -5000f), new Vec3(0f, 0f, 230f));
            for (int lane = 1; lane <= FormationCatalog.MaxSlots; lane++)
            {
                RejoinOutput o = new RejoinPlanner().Step(s, slot, leader, lane, Spacing, 255f, true, Dt);
                Assert.Equal(2000f - lane * RejoinPlanner.LaneStep, o.Ref.Pos.Y, 2);
            }
        }

        [Fact]
        public void ReferenceSlidesFromRendezvousToSlotWithoutJumps()
        {
            LeaderEstimate leader = Leader();
            SlotTarget slot = Slot(leader);
            var planner = new RejoinPlanner();
            Vec3 previous = default;
            float maxStep = 0f;
            for (int i = 0; i <= 1200; i++)
            {
                float d = Math.Max(0f, 400f - i * 0.5f);   // closing at 30 m/s from 400 m behind the pre-slot
                RejoinOutput step = planner.Step(TestStates.Flying(PreSlot(slot) - Vec3.Forward * d, leader.Vel),
                    slot, leader, 1, Spacing, 255f, true, Dt);
                if (i > 0) maxStep = Math.Max(maxStep, (step.Ref.Pos - previous).Length);
                previous = step.Ref.Pos;
            }
            RejoinOutput o = Hold(planner, slot.Ref.Pos, slot, leader, true, 20f);
            Assert.True(maxStep < 15f, $"reference jumped {maxStep:0.0} m in one tick");
            Assert.True((o.Ref.Pos - slot.Ref.Pos).Length < 1f);
            Assert.True(o.Sigma > 0.99f);
        }

        [Fact]
        public void SecondMemberWaitsAtThePreSlotUntilCleared()
        {
            LeaderEstimate leader = Leader();
            SlotTarget slot = Slot(leader);
            var planner = new RejoinPlanner();
            RejoinOutput waiting = Hold(planner, PreSlot(slot), slot, leader, false, 10f);
            Assert.True((waiting.Ref.Pos - PreSlot(slot)).Length < 2f, $"{waiting.Ref.Pos}");
            RejoinOutput cleared = Hold(planner, PreSlot(slot), slot, leader, true, 15f);
            Assert.True((cleared.Ref.Pos - slot.Ref.Pos).Length < 2f, $"{cleared.Ref.Pos}");
        }

        [Fact]
        public void DeadlockGuardAdvancesAfterThirtySeconds()
        {
            LeaderEstimate leader = Leader();
            SlotTarget slot = Slot(leader);
            var planner = new RejoinPlanner();
            Assert.True((Hold(planner, PreSlot(slot), slot, leader, false, 29f).Ref.Pos - PreSlot(slot)).Length < 2f);
            Assert.True((Hold(planner, PreSlot(slot), slot, leader, false, 16f).Ref.Pos - slot.Ref.Pos).Length < 2f);
        }

        [Fact]
        public void FallingBehindIsDeclaredOnceAfterTenSecondsWithoutAnIntercept()
        {
            LeaderEstimate fast = Leader(300f);
            SlotTarget slot = Slot(fast);
            var planner = new RejoinPlanner();
            AircraftState s = TestStates.Flying(slot.Ref.Pos - Vec3.Forward * 3000f, new Vec3(0f, 0f, 255f));
            int started = 0;
            float at = float.NaN;
            for (int i = 0; i < 30 * 60; i++)
            {
                if (!planner.Step(s, slot, fast, 1, Spacing, 255f, true, Dt).FallingBehindStarted) continue;
                started++;
                at = (i + 1) * Dt;
            }
            Assert.Equal(1, started);
            Assert.InRange(at, 9.95f, 10.05f);
            Assert.True(planner.FallingBehind);
        }

        [Fact]
        public void FallingBehindClearsWhenInterceptDropsBelowSixtySeconds()
        {
            LeaderEstimate fast = Leader(300f);
            SlotTarget slot = Slot(fast);
            var planner = new RejoinPlanner();
            AircraftState s = TestStates.Flying(slot.Ref.Pos - Vec3.Forward * 3000f, new Vec3(0f, 0f, 255f));
            for (int i = 0; i < 11 * 60; i++) planner.Step(s, slot, fast, 1, Spacing, 255f, true, Dt);
            Assert.True(planner.FallingBehind);
            LeaderEstimate slow = Leader(200f);
            SlotTarget slowSlot = Slot(slow);
            AircraftState near = TestStates.Flying(slowSlot.Ref.Pos - Vec3.Forward * 3000f, new Vec3(0f, 0f, 255f));
            RejoinOutput o = planner.Step(near, slowSlot, slow, 1, Spacing, 255f, true, Dt);   // 3000 / 55 = 55 s
            Assert.True(o.FallingBehindCleared);
            Assert.False(planner.FallingBehind);
        }

        [Fact]
        public void SigmaFallsFasterThanItRises()
        {
            LeaderEstimate leader = Leader();
            SlotTarget slot = Slot(leader);
            Vec3 far = slot.Ref.Pos - Vec3.Forward * 1000f;
            var rising = new RejoinPlanner();
            Hold(rising, far, slot, leader, true, 1f);
            float up = Hold(rising, slot.Ref.Pos, slot, leader, true, 0.7f).Sigma;
            var falling = new RejoinPlanner();
            Hold(falling, slot.Ref.Pos, slot, leader, true, 1f);
            float down = Hold(falling, far, slot, leader, true, 0.7f).Sigma;
            Assert.True(up < 0.3f, $"rose to {up:0.00} in 0.7 s");
            Assert.True(down < 0.4f, $"fell only to {down:0.00} in 0.7 s");
        }

        [Fact]
        public void MemberAlreadyInItsSlotIsNotPushedBackByTheStaggerGate()
        {
            LeaderEstimate leader = Leader();
            SlotTarget slot = Slot(leader);
            RejoinOutput o = Hold(new RejoinPlanner(), slot.Ref.Pos, slot, leader, false, 5f);
            Assert.True((o.Ref.Pos - slot.Ref.Pos).Length < 1f, $"{o.Ref.Pos}");
        }
    }
}
