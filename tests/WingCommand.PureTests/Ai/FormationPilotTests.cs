using System;
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
            public readonly FormationPilot Pilot = new FormationPilot(0, AirframeClass.FixedWing);
            public readonly WingEventRing Events = new WingEventRing();
            private readonly WingMemberInput[] members = new WingMemberInput[1];
            public Vec3 LeaderPos = new Vec3(0f, 2000f, 0f);
            private float time;

            public WingFrame Step(bool leaderPresent = true, Vec3? memberPos = null, Vec3? memberVel = null,
                float floorY = float.NaN)
            {
                time += Dt;
                LeaderPos += North * Dt;
                var leader = new AnchorSample { Pos = LeaderPos, Vel = North, Present = leaderPresent, Airborne = true };
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
        public void HoldOrbitFliesAtTheMembersCruiseNeverUnderItsSafeMinimum()
        {
            // A helicopter's reference airspeed can be a jet-like default (170 m/s); its hold orbit must use its cruise.
            AirframeProfile helo = AirframeProfile.Derive(new ProfileInputs { Class = AirframeClass.Rotary, MaxSpeed = 80f });
            Assert.Equal(0.75f * helo.CruiseSpeed, FormationPilot.OrbitSpeed(helo), 3);
            var slowJet = new AirframeProfile { StallSpeed = 60f, CruiseSpeed = 100f };
            Assert.Equal(1.5f * slowJet.MinimumSpeed(1f), FormationPilot.OrbitSpeed(slowJet), 3);
        }

        [Fact]
        public void IntentCarriesTheLeadersHeadingForSlowMembersToFace()
        {
            var rig = new Rig();
            rig.Step();
            Assert.True(rig.Pilot.LastIntent.HasHeading);
            Assert.Equal(0f, rig.Pilot.LastIntent.HeadingDeg, 1);   // the leader flies north
        }

        [Fact]
        public void TrailWithoutAFrameReferenceFallsBackToTheRejoinReference()
        {
            // Review M2b I3: the tick the mind entered Trail it flew the frame's stale trail reference (default: the
            // map origin), because the frame computes trail references from the role of the previous tick.
            AirframeProfile helo = AirframeProfile.Derive(new ProfileInputs { Class = AirframeClass.Rotary, MaxSpeed = 80f });
            var wing = new FormationWing(new FormationDefinition
            {
                Id = "one", Slots = new[] { new SlotDef(-1f, 1f, 0f) }, Element = new[] { 0 },
                SpacingMin = 40f, SpacingDefault = 80f, SpacingMax = 160f,
            }, 80f);
            var pilot = new FormationPilot(0, AirframeClass.Rotary);
            var members = new WingMemberInput[1];
            Vec3 lead = new Vec3(0f, 2000f, 0f);
            float nearestToOrigin = float.MaxValue;
            for (int i = 0; i < 8 * 60; i++)
            {
                lead += North * Dt;
                AircraftState s = TestStates.Flying(lead + new Vec3(0f, -150f, -3000f), new Vec3(0f, 0f, 60f));
                members[0] = new WingMemberInput { State = s, Capability = new MemberCapability { MaxSpeed = 64f }, Radius = 9f };
                WingFrame frame = wing.Update(new AnchorSample { Pos = lead, Vel = North, Present = true, Airborne = true },
                    members, 1, float.NaN, 60f, 8f, Dt);
                pilot.Step(frame, s, helo, i * Dt, Dt, null);
                if (pilot.Mind.Current == BehaviourId.Trail) nearestToOrigin = Math.Min(nearestToOrigin, pilot.LastIntent.Ref.Pos.Length);
            }
            Assert.Equal(BehaviourId.Trail, pilot.Mind.Current);
            Assert.True(nearestToOrigin > 1000f, $"reference {nearestToOrigin:0} m from the map origin");
        }

        [Fact]
        public void ATiltwingConversionIsLogged()
        {
            // Review M2b I8: every mode change is logged with a reason.
            AirframeProfile tilt = AirframeProfile.Derive(new ProfileInputs
            {
                Class = AirframeClass.Tiltwing, PublishedStallKmh = 45f * 3.6f, MaxSpeed = 150f,
            });
            var rig = new Rig();
            var pilot = new FormationPilot(0, AirframeClass.Tiltwing);
            var events = new WingEventRing();
            var members = new WingMemberInput[1];
            Vec3 lead = new Vec3(0f, 2000f, 0f);
            for (int i = 0; i < 6 * 60; i++)
            {
                float speed = i < 60 ? 80f : 30f;
                lead += North * Dt;
                AircraftState s = TestStates.Flying(lead + new Vec3(-80f, 0f, -80f), new Vec3(0f, 0f, speed));
                members[0] = new WingMemberInput { State = s, Capability = new MemberCapability { MaxSpeed = 150f }, Radius = 7f };
                WingFrame frame = rig.Wing.Update(new AnchorSample { Pos = lead, Vel = North, Present = true, Airborne = true },
                    members, 1, float.NaN, 60f, 8f, Dt);
                pilot.Step(frame, s, tilt, i * Dt, Dt, events);
            }
            Assert.Equal(1, events.CountOf(WingEventKind.Converted, 0));
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
            var pilot = new FormationPilot(1, AirframeClass.FixedWing);
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
