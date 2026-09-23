using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class TurnFrameTests
    {
        /// <summary>Leader at (0, 2000, 0) heading north in a constant turn.</summary>
        private static LeaderEstimate Turning(float speed, float rate, float bankDeg) => new LeaderEstimate
        {
            Pos = new Vec3(0f, 2000f, 0f), Vel = new Vec3(0f, 0f, speed), Acc = new Vec3(speed * rate, 0f, 0f),
            Track = Vec3.Forward, TurnRate = rate, BankDeg = bankDeg, Flying = true,
        };

        /// <summary>Leader on an exact right-turn circle, t seconds after heading north at the origin.</summary>
        private static LeaderEstimate OnCircle(float t, float speed, float rate, float bankDeg)
        {
            float r = speed / rate, psi = rate * t;
            var track = new Vec3((float)Math.Sin(psi), 0f, (float)Math.Cos(psi));
            return new LeaderEstimate
            {
                Pos = new Vec3(r - r * (float)Math.Cos(psi), 2000f, r * (float)Math.Sin(psi)),
                Vel = track * speed, Acc = Vec3.Cross(Vec3.Up, track) * (speed * rate),
                Track = track, TurnRate = rate, BankDeg = bankDeg, Flying = true,
            };
        }

        [Fact]
        public void SlotsOfASlowLeaderFollowItsTrackNotItsDrift()
        {
            // Drifting east at 3 m/s while facing north: "right" is east, not south.
            Vec3 offset = TurnFrame.Offset(new Vec3(3f, 0f, 0f), Vec3.Forward, 0f, 40f, 0f, 0f, 0f);
            Assert.True((offset - new Vec3(40f, 0f, 0f)).Length < 1e-3f, $"offset {offset}");
        }

        [Fact]
        public void AftSlotOfAHoveringLeaderStaysAftOfItsNose()
        {
            // The path delay covers the aft distance only as fast as the leader moves; a hovering leader's aft slots
            // collapsed onto its lateral line (two helos in one spot in the FlightSim H2 stop).
            var hover = new LeaderEstimate { Pos = new Vec3(0f, 300f, 0f), Track = Vec3.Forward, Flying = true };
            RefState r = TurnFrame.Evaluate(hover, 0f, 0f, 40f, 80f, 0f, 0f);
            Assert.True((r.Pos - new Vec3(40f, 300f, -80f)).Length < 1e-3f, $"slot at {r.Pos}");
        }

        [Fact]
        public void AftSlotMovesContinuouslyThroughThePathSpeedFloor()
        {
            RefState fast = TurnFrame.Evaluate(Turning(TurnFrame.PathSpeedFloor + 0.01f, 0f, 0f), 0f, 0f, 0f, 80f, 0f, 0f);
            RefState slow = TurnFrame.Evaluate(Turning(TurnFrame.PathSpeedFloor - 0.01f, 0f, 0f), 0f, 0f, 0f, 80f, 0f, 0f);
            Assert.True((fast.Pos - slow.Pos).Length < 0.1f, $"jump {(fast.Pos - slow.Pos).Length:0.00} m");
            Assert.Equal(-80f, slow.Pos.Z, 1);
        }

        [Fact]
        public void AftSlotOfASlowDeceleratingLeaderMovesWithTheLeader()
        {
            // Slot = leader − track·aft whatever the split between path and rigid offset, so its velocity is the leader's.
            var slowing = new LeaderEstimate
            {
                Pos = new Vec3(0f, 300f, 0f), Vel = new Vec3(0f, 0f, 20f), Acc = new Vec3(0f, 0f, -2f), Track = Vec3.Forward, Flying = true,
            };
            RefState r = TurnFrame.Evaluate(slowing, 0f, 0f, 0f, 80f, 0f, 0f);
            Assert.Equal(20f, r.Vel.Z, 1);
        }

        [Fact]
        public void SlowlyClimbingLeadersSlotsStayBehindItNotBelow()
        {
            // A helicopter climbing straight up at 3 m/s: its path is not a flight direction for the frame (review I8).
            var climbing = new LeaderEstimate
            {
                Pos = new Vec3(0f, 300f, 0f), Vel = new Vec3(0f, 3f, 0f), Track = Vec3.Forward, Flying = true,
            };
            Vec3 offset = TurnFrame.Offset(climbing.Vel, climbing.Track, 0f, 10f, 20f, 0f, 1f);
            Assert.True((offset - new Vec3(10f, 0f, -20f)).Length < 0.1f, $"offset {offset}");
        }

        private static LeaderEstimate Moving(float speed, float along) => new LeaderEstimate
        {
            Pos = new Vec3(0f, 300f, 0f), Vel = new Vec3(0f, 0f, speed), Acc = new Vec3(0f, 0f, along), Track = Vec3.Forward,
            Flying = true,
        };

        [Fact]
        public void SweepAheadSlotOfAStoppedAnchorStaysAhead()
        {
            // Review M2b I1: negative aft (sweep ahead) collapsed onto a stopped escortee.
            RefState r = TurnFrame.Evaluate(Moving(0f, 0f), 0f, 0f, 0f, -900f, 0f, 0f);
            Assert.True((r.Pos - new Vec3(0f, 300f, 900f)).Length < 0.5f, $"slot at {r.Pos}");
        }

        [Fact]
        public void SlotsOfAnAcceleratingSlowAnchorMoveWithItAndDoNotJumpAtTheTrackBlendSpeed()
        {
            foreach (float aft in new[] { -900f, 80f })
            {
                RefState below = TurnFrame.Evaluate(Moving(LeaderEstimator.TrackBlendSpeed - 0.01f, 1f), 0f, 0f, 0f, aft, 0f, 0f);
                RefState above = TurnFrame.Evaluate(Moving(LeaderEstimator.TrackBlendSpeed + 0.01f, 1f), 0f, 0f, 0f, aft, 0f, 0f);
                Assert.True((below.Pos - above.Pos).Length < 1f, $"aft {aft}: jump {(below.Pos - above.Pos).Length:0} m");
                RefState slow = TurnFrame.Evaluate(Moving(10f, 1f), 0f, 0f, 0f, aft, 0f, 0f);
                Assert.Equal(10f, slow.Vel.Z, 1);
            }
        }

        [Fact]
        public void RollFollowIsFullOnlyForFingertipSlotsAndZeroFromFortyFiveMetres()
        {
            Assert.Equal(1f, TurnFrame.RollFollowWeight(10f, -1f));
            Assert.Equal(0f, TurnFrame.RollFollowWeight(-60f, -1f));
            Assert.Equal(0.5f, TurnFrame.RollFollowWeight(30f, -1f), 3);
            Assert.Equal(0.3f, TurnFrame.RollFollowWeight(500f, 0.3f));
        }

        [Fact]
        public void LateralSlotInAConstantTurnFliesItsConcentricArc()
        {
            const float v = 200f, w = 0.1f, right = -150f;
            RefState r = TurnFrame.Evaluate(Turning(v, w, 0f), 0f, 0f, right, 0f, 0f, 0f);
            // t̂ = +z, ĉ = +x: on radius R − right the arc speed is V − ω·right, the acceleration ω(V − ω·right) inward.
            Assert.True((r.Pos - new Vec3(right, 2000f, 0f)).Length < 1e-3f);
            Assert.True((r.Vel - new Vec3(0f, 0f, v - w * right)).Length < 0.05f, $"vel {r.Vel}");
            Assert.True((r.Acc - new Vec3(w * (v - w * right), 0f, 0f)).Length < 0.05f, $"acc {r.Acc}");
        }

        [Fact]
        public void TrailSlotRidesTheLeadersCircleNotItsTangent()
        {
            // 400 m in trail of a 2 km-radius turn: on the tangent it would sit 2040 m from the centre, on the
            // leader's own path 2000 m. The centre is 2 km to the leader's right.
            RefState r = TurnFrame.Evaluate(Turning(200f, 0.1f, 0f), 0f, 0f, 0f, 400f, 0f, 0f);
            float fromCentre = (r.Pos - new Vec3(2000f, 2000f, 0f)).Length;
            Assert.InRange(fromCentre, 1998f, 2002f);
        }

        [Fact]
        public void RolledCloseSlotSitsOnTheBankedWingLine()
        {
            RefState r = TurnFrame.Evaluate(Turning(200f, 0.1f, 60f), 60f, 0f, 40f, 0f, 0f, 1f);
            Assert.True((r.Pos - new Vec3(20f, 2000f - 34.641f, 0f)).Length < 0.01f, $"pos {r.Pos}");
        }

        [Fact]
        public void PartiallyRolledSlotKeepsItsDistanceFromTheLeader()
        {
            // Halfway between the level and the banked frame is a half roll, not a chord: the slot stays 104 m out.
            RefState r = TurnFrame.Evaluate(Turning(200f, 0.1f, 85f), 85f, 0f, 104f, 0f, 0f, 0.5f);
            Assert.Equal(104f, (r.Pos - new Vec3(0f, 2000f, 0f)).Length, 1);
        }

        [Fact]
        public void AftSlotsFollowTheLeadersPathInAClimb()
        {
            // 80 m behind a 30° climb is 40 m below the leader, whatever the frame: aft is along the path flown.
            var climbing = new LeaderEstimate
            {
                Pos = new Vec3(0f, 2000f, 0f), Vel = new Vec3(0f, 100f, 173.205f), Track = Vec3.Forward, Flying = true,
            };
            Assert.Equal(1960f, TurnFrame.Evaluate(climbing, 0f, 0f, 0f, 80f, 0f, 1f).Pos.Y, 2);
            Assert.Equal(1960f, TurnFrame.Evaluate(climbing, 0f, 0f, 0f, 80f, 0f, 0f).Pos.Y, 2);
            // The lateral offset of a level-frame slot stays horizontal in the climb.
            Assert.Equal(2000f, TurnFrame.Evaluate(climbing, 0f, 0f, 100f, 0f, 0f, 0f).Pos.Y, 2);
        }

        [Fact]
        public void RolledSlotVelocityAndAccelerationMatchItsMotionAroundTheTurn()
        {
            RefState a = TurnFrame.Evaluate(OnCircle(0f, 200f, 0.1f, 64f), 64f, 0f, 40f, 30f, -10f, 1f);
            RefState b = TurnFrame.Evaluate(OnCircle(0.05f, 200f, 0.1f, 64f), 64f, 0f, 40f, 30f, -10f, 1f);
            Vec3 velocity = (b.Pos - a.Pos) / 0.05f, acceleration = (b.Vel - a.Vel) / 0.05f;
            Assert.True((velocity - (a.Vel + b.Vel) * 0.5f).Length < 0.1f, $"{velocity} vs {a.Vel}");
            Assert.True((acceleration - (a.Acc + b.Acc) * 0.5f).Length < 0.2f, $"{acceleration} vs {a.Acc}");
        }

        [Fact]
        public void VerticalLeaderUsesTheLastTrackAndStaysFinite()
        {
            var vertical = new LeaderEstimate
            {
                Pos = new Vec3(0f, 2000f, 0f), Vel = new Vec3(0f, 200f, 0f), Track = Vec3.Right, Flying = true,
            };
            RefState level = TurnFrame.Evaluate(vertical, 0f, 0f, 50f, 0f, 0f, 0f);
            RefState rolled = TurnFrame.Evaluate(vertical, 30f, 0f, 50f, 20f, 0f, 1f);
            Assert.True((level.Pos - new Vec3(0f, 2000f, -50f)).Length < 1e-3f, $"{level.Pos}");
            Assert.True(Scalar.IsFinite(rolled.Pos.X) && Scalar.IsFinite(rolled.Vel.Y) && Scalar.IsFinite(rolled.Acc.Z));
        }
    }
}
