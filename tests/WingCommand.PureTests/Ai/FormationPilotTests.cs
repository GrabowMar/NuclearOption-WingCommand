using Xunit;

namespace WingCommand.PureTests
{
    public class FormationPilotTests
    {
        private const float Dt = 1f / 60f;
        private static readonly AirframeProfile Fighter = new AirframeProfile();
        private static readonly Vec3 North = new Vec3(0f, 0f, 200f);

        /// <summary>Leader straight north at 200 m/s; the test places the member itself (no plant).</summary>
        private sealed class Rig
        {
            public readonly FormationWing Wing = new FormationWing(new FormationDefinition
            {
                Id = "one", Slots = new[] { new SlotDef(-1f, 1f, 0f) }, Element = new[] { 0 },
                SpacingMin = 40f, SpacingDefault = 80f, SpacingMax = 160f,
            }, 80f);
            public readonly FormationPilot Pilot = new FormationPilot(0);
            public readonly WingEventRing Events = new WingEventRing();
            private readonly WingMemberInput[] members = new WingMemberInput[1];
            public Vec3 LeaderPos = new Vec3(0f, 2000f, 0f);
            private float time;

            public WingFrame Step(bool leaderPresent = true, Vec3? memberPos = null, Vec3? memberVel = null,
                float floorY = float.NaN)
            {
                time += Dt;
                LeaderPos += North * Dt;
                var leader = new LeaderSample { Pos = LeaderPos, Vel = North, Present = leaderPresent, Airborne = true };
                AircraftState s = TestStates.Flying(memberPos ?? LeaderPos + new Vec3(-80f, 0f, -80f), memberVel ?? North);
                members[0] = new WingMemberInput
                {
                    State = s, Capability = new MemberCapability { MaxSpeed = 300f, MinSpeed = 80f }, Radius = 8f,
                };
                WingFrame frame = Wing.Update(leader, members, 1, floorY, 60f, 8f, Dt);
                Pilot.Step(frame, s, Fighter, time, Dt, Events);
                return frame;
            }
        }

        [Fact]
        public void MemberInItsSlotIsCapturedAndThenTracksTheSlot()
        {
            var rig = new Rig();
            WingFrame frame = null;
            for (int i = 0; i < 4 * 60; i++) frame = rig.Step();
            Assert.Equal(BehaviourId.StationKeep, rig.Pilot.Mind.Current);
            Assert.Equal(frame.Slots[0].Ref.Pos, rig.Pilot.LastIntent.Ref.Pos);
            Assert.Equal(1, rig.Events.CountOf(WingEventKind.BehaviourChanged, 0));
            Assert.Equal(TransitionReason.Captured, rig.Events[0].Reason);
        }

        [Fact]
        public void RejoinLanesClearTheCollisionBiasRadius()
        {
            // Adjacent lanes closer than the bias radius would keep crossing rejoins inside it: lane 1 must sit at
            // least R + 5 m below the leader (R = max(2·8 + 15, 0.35·80) = 31 m).
            var rig = new Rig();
            rig.Step(memberPos: rig.LeaderPos + new Vec3(-80f, 0f, -5000f));
            float depth = rig.Wing.Frame.Leader.Pos.Y - rig.Pilot.LastIntent.Ref.Pos.Y;
            Assert.True(depth >= CollisionBias.RadiusFor(8f, 80f) + 5f - 0.01f, $"lane 1 only {depth:0.0} m below the leader");
        }

        [Fact]
        public void LeaderLossSendsTheMemberToAHoldAboveTheLastEstimate()
        {
            var rig = new Rig();
            for (int i = 0; i < 60; i++) rig.Step();
            float leaderY = rig.Wing.Frame.Leader.Pos.Y;
            rig.Step(leaderPresent: false);
            Assert.Equal(BehaviourId.HoldOverhead, rig.Pilot.Mind.Current);
            Assert.Equal(leaderY + HoldOrbit.BaseHeight + HoldOrbit.SlotHeight, rig.Pilot.LastIntent.Ref.Pos.Y, 1);
            Assert.Equal(TransitionReason.LeaderLost, rig.Events[rig.Events.Count - 1].Reason);
        }

        [Fact]
        public void GcasActivationIsLoggedOncePerActivation()
        {
            var rig = new Rig();
            for (int i = 0; i < 3; i++)
                rig.Step(memberPos: rig.LeaderPos + new Vec3(-80f, -1850f, -80f), memberVel: new Vec3(0f, -60f, 190f), floorY: 0f);
            Assert.True(rig.Pilot.Pipeline.GcasActive);
            Assert.Equal(1, rig.Events.CountOf(WingEventKind.GcasActivated, 0));
        }

        [Fact]
        public void FrameCollisionBiasReachesTheConstraintChain()
        {
            var rig = new Rig();
            for (int i = 0; i < 30; i++) rig.Step(memberPos: rig.LeaderPos + new Vec3(5f, 0f, 0f));
            Assert.True(rig.Pilot.Pipeline.Report.CollisionActive);
            Assert.Equal(1, rig.Events.CountOf(WingEventKind.CollisionEmergency, 0));
        }

        [Fact]
        public void FallingBehindIsLoggedOnce()
        {
            var rig = new Rig();
            rig.Pilot.AfterburnerAllowed = false;   // usable 255 m/s against a 200 m/s leader 20 km ahead: 360 s
            for (int i = 0; i < 15 * 60; i++)
                rig.Step(memberPos: rig.LeaderPos + new Vec3(-80f, 0f, -20000f), memberVel: new Vec3(0f, 0f, 150f));
            Assert.Equal(1, rig.Events.CountOf(WingEventKind.FallingBehind, 0));
            Assert.True(rig.Pilot.LastRejoin.FallingBehind);
        }

        [Fact]
        public void StepKeepsTheGuidanceCommandForTheOverlay()
        {
            var rig = new Rig();
            rig.Step(memberPos: rig.LeaderPos + new Vec3(-80f, 0f, -600f));
            Assert.NotEqual(Vec3.Zero, rig.Pilot.LastGuidance.VelCmd);
        }

        [Fact]
        public void FormUpLogsACommandedRejoin()
        {
            var pilot = new FormationPilot(1);
            var events = new WingEventRing();
            pilot.FormUp(3f, events);
            Assert.Equal(0, events.Count);   // already rejoining
            pilot.Mind.Force(BehaviourId.StationKeep);
            pilot.FormUp(4f, events);
            Assert.Equal(1, events.Count);
            WingEvent e = events[0];
            Assert.Equal(WingEventKind.BehaviourChanged, e.Kind);
            Assert.Equal(BehaviourId.StationKeep, e.From);
            Assert.Equal(BehaviourId.Rejoin, e.To);
            Assert.Equal(TransitionReason.Commanded, e.Reason);
            Assert.Equal(1, e.Member);
        }
    }
}
