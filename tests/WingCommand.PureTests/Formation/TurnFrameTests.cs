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
        public void RollFollowIsFullForCloseSlotsAndZeroForWideOnes()
        {
            Assert.Equal(1f, TurnFrame.RollFollowWeight(40f, -1f));
            Assert.Equal(0f, TurnFrame.RollFollowWeight(-200f, -1f));
            Assert.Equal(0.5f, TurnFrame.RollFollowWeight(120f, -1f), 3);
            Assert.Equal(0.3f, TurnFrame.RollFollowWeight(500f, 0.3f));
        }

        [Fact]
        public void LevelSlotInAConstantTurnMatchesTheRigidFrame()
        {
            const float v = 200f, w = 0.1f, right = -150f, aft = 100f;
            RefState r = TurnFrame.Evaluate(Turning(v, w, 0f), 0f, 0f, right, aft, 0f, 0f);
            // t̂ = +z, ĉ = +x: vel = t̂(V − ω·right) − ĉ(ω·aft); acc = ĉ·ω(V − ω·right) + t̂·ω²·aft.
            Assert.True((r.Pos - new Vec3(right, 2000f, -aft)).Length < 1e-3f);
            Assert.True((r.Vel - new Vec3(-w * aft, 0f, v - w * right)).Length < 0.05f, $"vel {r.Vel}");
            Assert.True((r.Acc - new Vec3(w * (v - w * right), 0f, w * w * aft)).Length < 0.05f, $"acc {r.Acc}");
        }

        [Fact]
        public void RolledCloseSlotSitsOnTheBankedWingLine()
        {
            RefState r = TurnFrame.Evaluate(Turning(200f, 0.1f, 60f), 60f, 0f, 40f, 0f, 0f, 1f);
            Assert.True((r.Pos - new Vec3(20f, 2000f - 34.641f, 0f)).Length < 0.01f, $"pos {r.Pos}");
        }

        [Fact]
        public void RolledFrameFollowsTheFlightPathInAClimb()
        {
            var climbing = new LeaderEstimate
            {
                Pos = new Vec3(0f, 2000f, 0f), Vel = new Vec3(0f, 100f, 173.205f), Track = Vec3.Forward, Flying = true,
            };
            Assert.Equal(1960f, TurnFrame.Evaluate(climbing, 0f, 0f, 0f, 80f, 0f, 1f).Pos.Y, 2);
            Assert.Equal(2000f, TurnFrame.Evaluate(climbing, 0f, 0f, 0f, 80f, 0f, 0f).Pos.Y, 2);
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
