using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class LeaderEstimatorTests
    {
        private const float Dt = 1f / 60f;

        /// <summary>Exact constant right turn from heading 0 around the centre (R, 2000, 0).</summary>
        private static LeaderSample Turning(float t, float speed = 200f, float rate = 0.1f)
        {
            float r = speed / rate, psi = rate * t;
            return new LeaderSample
            {
                Pos = new Vec3(r - r * (float)Math.Cos(psi), 2000f, r * (float)Math.Sin(psi)),
                Vel = new Vec3((float)Math.Sin(psi), 0f, (float)Math.Cos(psi)) * speed,
                BankDeg = (float)Math.Atan(speed * rate / Scalar.G) * Scalar.Rad2Deg,
                Present = true,
                Airborne = true,
            };
        }

        private static LeaderSample Straight(float speed = 200f, float bank = 0f) => new LeaderSample
        {
            Pos = new Vec3(0f, 2000f, 0f), Vel = new Vec3(0f, 0f, speed), BankDeg = bank, Present = true, Airborne = true,
        };

        [Fact]
        public void ConstantTurnIsProjectedOneTickAhead()
        {
            var estimator = new LeaderEstimator();
            LeaderEstimate e = default;
            for (int i = 0; i <= 180; i++) e = estimator.Update(Turning(i * Dt), Dt);
            LeaderSample next = Turning(181 * Dt);
            Assert.True((e.Pos - next.Pos).Length < 0.05f, $"projection off by {(e.Pos - next.Pos).Length:0.000} m");
            Assert.True((e.Vel - next.Vel).Length < 0.1f);
            Assert.Equal(0.1f, e.TurnRate, 2);
        }

        [Fact]
        public void AccelerationStepIsJerkLimited()
        {
            var estimator = new LeaderEstimator();
            LeaderSample s = Straight();
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
            LeaderSample s = Straight();
            LeaderSample p = Straight();
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
            LeaderSample grounded = Straight();
            grounded.Airborne = false;
            Assert.False(estimator.Update(grounded, Dt).Flying);
        }

        [Fact]
        public void MissingLeaderKeepsTheLastEstimate()
        {
            var estimator = new LeaderEstimator();
            LeaderEstimate before = default;
            for (int i = 0; i < 30; i++) before = estimator.Update(Turning(i * Dt), Dt);
            LeaderEstimate after = estimator.Update(new LeaderSample { Present = false }, Dt);
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
        public void TrackKeepsItsLastValidHeadingInVerticalFlight()
        {
            var estimator = new LeaderEstimator();
            LeaderSample east = Straight();
            east.Vel = new Vec3(200f, 0f, 0f);
            estimator.Update(east, Dt);
            LeaderSample up = Straight();
            up.Vel = new Vec3(0f, 200f, 0f);
            LeaderEstimate e = estimator.Update(up, Dt);
            Assert.True((e.Track - Vec3.Right).Length < 1e-4f);
        }
    }
}
