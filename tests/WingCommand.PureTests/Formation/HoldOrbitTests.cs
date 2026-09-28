using Xunit;

namespace WingCommand.PureTests
{
    public class HoldOrbitTests
    {
        private static readonly Vec3 Centre = new Vec3(0f, 2500f, 0f);
        private static readonly Vec3 SouthOfCentre = new Vec3(0f, 2000f, -3000f);

        [Fact]
        public void RabbitCirclesClockwiseAtTheOrbitRadiusAndSpeed()
        {
            var orbit = new HoldOrbit();
            orbit.Begin(Centre, 150f, SouthOfCentre);
            RefState r = orbit.Step(Centre, Vec3.Zero, 1f / 60f);
            float radius = HoldOrbit.RadiusFor(150f);
            Assert.Equal(radius, (r.Pos - Centre).Horizontal.Length, 0);
            Assert.Equal(2500f, r.Pos.Y);
            Assert.Equal(150f, r.Vel.Length, 2);
            Assert.Equal(150f * 150f / radius, r.Acc.Length, 2);
            Assert.True(Vec3.Dot(r.Acc, Centre - r.Pos) > 0f);
            Assert.Equal(0f, Scalar.Wrap180(Vec3.HeadingDeg(r.Vel) - Vec3.HeadingDeg(r.Pos - Centre) - 90f), 2);
        }

        [Fact]
        public void HoldStartsAheadOfTheMembersBearing()
        {
            var orbit = new HoldOrbit();
            orbit.Begin(Centre, 150f, SouthOfCentre);
            RefState r = orbit.Step(Centre, Vec3.Zero, 0f);
            Assert.Equal(180f + HoldOrbit.LeadDeg, Vec3.HeadingDeg(r.Pos - Centre), 1);
        }

        [Fact]
        public void MovingCentreCarriesTheRabbit()
        {
            var orbit = new HoldOrbit();
            orbit.Begin(Centre, 150f, SouthOfCentre);
            RefState still = orbit.Step(Centre, Vec3.Zero, 0f);
            RefState moving = orbit.Step(Centre, new Vec3(0f, 0f, 60f), 0f);
            Assert.Equal(60f, (moving.Vel - still.Vel).Z, 3);
        }
    }
}
