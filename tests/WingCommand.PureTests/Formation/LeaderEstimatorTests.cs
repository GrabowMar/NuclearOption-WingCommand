using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class LeaderEstimatorTests
    {
        private const float Dt = 1f / 60f;

        /// <summary>Exact constant right turn from heading 0 around the centre (R, 2000, 0).</summary>
        private static AnchorSample Turning(float t, float speed = 200f, float rate = 0.1f)
        {
            float r = speed / rate, psi = rate * t;
            return new AnchorSample
            {
                Pos = new Vec3(r - r * (float)Math.Cos(psi), 2000f, r * (float)Math.Sin(psi)),
                Vel = new Vec3((float)Math.Sin(psi), 0f, (float)Math.Cos(psi)) * speed,
                BankDeg = (float)Math.Atan(speed * rate / Scalar.G) * Scalar.Rad2Deg,
                Present = true,
                Airborne = true,
            };
        }

        private static AnchorSample Straight(float speed = 200f, float bank = 0f) => new AnchorSample
        {
            Pos = new Vec3(0f, 2000f, 0f), Vel = new Vec3(0f, 0f, speed), BankDeg = bank, Present = true, Airborne = true,
        };

        [Fact]
        public void ConstantTurnIsProjectedOneTickAhead()
        {
            var estimator = new LeaderEstimator();
            LeaderEstimate e = default;
            for (int i = 0; i <= 180; i++) e = estimator.Update(Turning(i * Dt), Dt);
            AnchorSample next = Turning(181 * Dt);
            Assert.True((e.Pos - next.Pos).Length < 0.05f, $"projection off by {(e.Pos - next.Pos).Length:0.000} m");
            Assert.True((e.Vel - next.Vel).Length < 0.1f);
            Assert.Equal(0.1f, e.TurnRate, 2);
        }

        [Fact]
        public void AccelerationStepIsJerkLimited()
        {
            var estimator = new LeaderEstimator();
            AnchorSample s = Straight();
            s.IsPlayer = true;
            for (int i = 0; i < 60; i++) estimator.Update(s, Dt);
            s.Vel = new Vec3(0f, 0f, 200.5f);   // a sudden 30 m/s² along the track
            LeaderEstimate e = estimator.Update(s, Dt);
            Assert.True(e.Acc.Length <= LeaderEstimator.JerkLimit * Dt + 1e-4f, $"|a| = {e.Acc.Length:0.000}");
        }

        [Fact]
        public void BankIsRateLimitedAndFasterForAPlayer()
        {
            var ai = new LeaderEstimator();
            var player = new LeaderEstimator();
            AnchorSample s = Straight();
            AnchorSample p = Straight();
            p.IsPlayer = true;
            ai.Update(s, Dt);
            player.Update(p, Dt);
            s.BankDeg = 90f;
            p.BankDeg = 90f;
            Assert.Equal(3f, ai.Update(s, Dt).BankDeg, 3);
            Assert.Equal(6f, player.Update(p, Dt).BankDeg, 3);
        }

        [Fact]
        public void SlowOrGroundedLeaderIsNotFlying()
        {
            var estimator = new LeaderEstimator();
            Assert.True(estimator.Update(Straight(), Dt).Flying);
            Assert.False(estimator.Update(Straight(speed: 20f), Dt).Flying);
            AnchorSample grounded = Straight();
            grounded.Airborne = false;
            Assert.False(estimator.Update(grounded, Dt).Flying);
        }

        [Fact]
        public void MissingLeaderKeepsTheLastEstimate()
        {
            var estimator = new LeaderEstimator();
            LeaderEstimate before = default;
            for (int i = 0; i < 30; i++) before = estimator.Update(Turning(i * Dt), Dt);
            LeaderEstimate after = estimator.Update(new AnchorSample { Present = false }, Dt);
            Assert.Equal(before.Pos, after.Pos);
            Assert.False(after.Flying);
        }

        [Fact]
        public void ZeroDtKeepsTheEstimateFinite()
        {
            var estimator = new LeaderEstimator();
            estimator.Update(Straight(), Dt);
            LeaderEstimate e = estimator.Update(Straight(), 0f);
            Assert.True(Scalar.IsFinite(e.Pos.X) && Scalar.IsFinite(e.Acc.Z) && Scalar.IsFinite(e.TurnRate) && Scalar.IsFinite(e.BankRateDps));
            Assert.Equal(new Vec3(0f, 2000f, 0f), e.Pos);
        }

        [Fact]
        public void HoveringLeaderKeepsItsFacingAsTheTrack()
        {
            // A hovering helicopter drifting sideways at 3 m/s: the wing's frame follows its nose, not the drift.
            var estimator = new LeaderEstimator();
            AnchorSample hover = Straight();
            hover.Vel = new Vec3(3f, 0f, 0f);
            hover.Fwd = Vec3.Forward;
            hover.CanHover = true;
            LeaderEstimate e = estimator.Update(hover, Dt);
            Assert.True((e.Track - Vec3.Forward).Length < 1e-4f, $"track {e.Track}");
        }

        [Fact]
        public void GroundAnchorIsFollowedAtAnySpeedOnTheGround()
        {
            // An escorted truck or ship: never "airborne", yet the wing forms on it, stopped or moving.
            AnchorSample truck = Straight(0f);
            truck.Airborne = false;
            truck.Kind = AnchorKind.Ground;
            Assert.True(new LeaderEstimator().Update(truck, Dt).Flying);
        }

        [Fact]
        public void TrackBlendsFromTheNoseToTheVelocityWithoutASnap()
        {
            // Facing north while sidestepping east: at 4.9 and 5.1 m/s the frame must be (nearly) the same (review I8).
            Vec3 Track(float eastSpeed)
            {
                AnchorSample s = Straight();
                s.Vel = new Vec3(eastSpeed, 0f, 0f);
                s.Fwd = Vec3.Forward;
                s.CanHover = true;
                return new LeaderEstimator().Update(s, Dt).Track;
            }
            float jump = (float)Math.Acos(Scalar.Clamp(Vec3.Dot(Track(4.9f), Track(5.1f)), -1f, 1f)) * Scalar.Rad2Deg;
            Assert.True(jump < 2f, $"frame turns {jump:0.0}° between 4.9 and 5.1 m/s");
            Assert.True((Track(0f) - Vec3.Forward).Length < 1e-3f);
            Assert.True((Track(20f) - Vec3.Right).Length < 1e-3f);
        }

        [Fact]
        public void HoverCapableLeaderFliesAtAnySpeed()
        {
            AnchorSample hover = Straight(0f);
            hover.CanHover = true;
            Assert.True(new LeaderEstimator().Update(hover, Dt).Flying);
            Assert.False(new LeaderEstimator().Update(Straight(0f), Dt).Flying);
        }

        [Fact]
        public void TrackKeepsItsLastValidHeadingInVerticalFlight()
        {
            var estimator = new LeaderEstimator();
            AnchorSample east = Straight();
            east.Vel = new Vec3(200f, 0f, 0f);
            estimator.Update(east, Dt);
            AnchorSample up = Straight();
            up.Vel = new Vec3(0f, 200f, 0f);
            LeaderEstimate e = estimator.Update(up, Dt);
            Assert.True((e.Track - Vec3.Right).Length < 1e-4f);
        }

        [Fact]
        public void LeaderReappearingElsewhereStartsWithoutAPhantomAcceleration()
        {
            var est = new LeaderEstimator();
            for (int i = 0; i < 60; i++)
                est.Update(new AnchorSample { Pos = new Vec3(0f, 2000f, i * 200f * Dt), Vel = new Vec3(0f, 0f, 200f), Present = true, Airborne = true, IsPlayer = true }, Dt);
            for (int i = 0; i < 60; i++) est.Update(new AnchorSample { Present = false }, Dt);
            LeaderEstimate e = default;
            for (int i = 0; i < 30; i++)
                e = est.Update(new AnchorSample { Pos = new Vec3(20000f + i * 200f * Dt, 2000f, 0f), Vel = new Vec3(200f, 0f, 0f), Present = true, Airborne = true, IsPlayer = true }, Dt);
            Assert.True(e.Acc.Length < 1f, $"phantom |a| = {e.Acc.Length:0.0}");
        }
    }
}
