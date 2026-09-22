using Xunit;

namespace WingCommand.PureTests
{
    public sealed class AttackRunGeometryTests
    {
        [Fact]
        public void CloseHighAspectOvershootsAndHoldsFire()
        {
            var advice = AttackRunGeometry.Evaluate(
                600f, 0.1f, 0.2f, 180f, 160f, 150f, 900f, false, false, false);
            Assert.Equal(AttackRunShape.Overshoot, advice.Shape);
            Assert.True(advice.HoldFire);
            Assert.Equal(1f, advice.Throttle);
        }

        [Fact]
        public void FastClosureWithPoorAngleClimbsToBleed()
        {
            var advice = AttackRunGeometry.Evaluate(
                1200f, 0.7f, 0.1f, 220f, 160f, 150f, 800f, false, false, false);
            Assert.Equal(AttackRunShape.HighClosure, advice.Shape);
            Assert.True(advice.Up > 0f);
            Assert.False(advice.HoldFire);
            Assert.InRange(advice.Throttle, 0.69f, 0.71f);
        }

        [Fact]
        public void EnergyFighterKeepsPowerInHighClosure()
        {
            var advice = AttackRunGeometry.Evaluate(
                1200f, 0.7f, 0.1f, 220f, 160f, 150f, 800f, false, false, true);
            Assert.Equal(AttackRunShape.HighClosure, advice.Shape);
            Assert.Equal(1f, advice.Throttle);
        }

        [Fact]
        public void SlowBelowCornerTradesAltitude()
        {
            var advice = AttackRunGeometry.Evaluate(
                1500f, 0.7f, 0.2f, 100f, 140f, 150f, 600f, false, false, false);
            Assert.Equal(AttackRunShape.LowEnergy, advice.Shape);
            Assert.True(advice.Up < 0f);
            Assert.Equal(1f, advice.Throttle);
        }

        [Fact]
        public void EnergyFighterDoesNotDiveWhenSlow()
        {
            var advice = AttackRunGeometry.Evaluate(
                1500f, 0.7f, 0.2f, 100f, 140f, 150f, 600f, false, false, true);
            Assert.Equal(AttackRunShape.RunIn, advice.Shape);
            Assert.Equal(0f, advice.Up);
        }

        [Fact]
        public void BehindTheTargetLagsTheAimPoint()
        {
            var advice = AttackRunGeometry.Evaluate(
                1800f, 0.8f, -0.6f, 180f, 180f, 150f, 900f, false, false, false);
            Assert.Equal(AttackRunShape.Lag, advice.Shape);
            Assert.True(advice.Along < 0f);
        }

        [Fact]
        public void LeadPursuitAimsBeyondTheTargetOnARunIn()
        {
            var plain = AttackRunGeometry.Evaluate(
                4000f, 0.98f, 0.2f, 180f, 180f, 150f, 900f, false, false, false);
            var lead = AttackRunGeometry.Evaluate(
                4000f, 0.98f, 0.2f, 180f, 180f, 150f, 900f, false, true, false);
            Assert.Equal(AttackRunShape.RunIn, plain.Shape);
            Assert.Equal(0f, plain.Along);
            Assert.True(lead.Along > 150f);
        }

        [Fact]
        public void SurfaceRunInDropsTerrainFollowWhenPointedAtTheTarget()
        {
            var advice = AttackRunGeometry.Evaluate(
                1500f, 0.95f, 0.2f, 180f, 0f, 150f, 900f, true, false, false);
            Assert.Equal(AttackRunShape.RunIn, advice.Shape);
            Assert.False(advice.FollowTerrain);
        }
    }
}
