using Xunit;

namespace WingCommand.PureTests
{
    public class AirStartTests
    {
        private static readonly Vec3 Leader = new Vec3(1000f, 3000f, 5000f);
        private static readonly Vec3 North = new Vec3(0f, 0f, 200f);

        [Fact]
        public void TwoKilometresBehindAndBelowOnTheSlotSide()
        {
            Vec3 p = AirStart.Position(Leader, North, 80f, 0, 0f);
            Assert.Equal(5000f - 2000f, p.Z, 1);
            Assert.Equal(3000f - 150f, p.Y, 1);
            Assert.True(p.X > Leader.X + 100f);
            Assert.True(AirStart.Position(Leader, North, -80f, 0, 0f).X < Leader.X - 100f);
        }

        [Fact]
        public void LaterMembersOnTheSameSideSpreadFurtherOut()
        {
            Vec3 a = AirStart.Position(Leader, North, 80f, 0, 0f);
            Vec3 b = AirStart.Position(Leader, North, 160f, 1, 0f);
            Assert.True(b.X - a.X >= AirStart.LateralStepM - 0.01f);
        }

        [Fact]
        public void RaisedThreeHundredMetresAboveTheTerrain() =>
            Assert.Equal(2900f + AirStart.TerrainClearanceM, AirStart.Position(Leader, North, 0f, 0, 2900f).Y, 1);

        [Fact]
        public void HeadingFollowsTheLeaderOrNorthWhenHovering()
        {
            Assert.Equal(new Vec3(1f, 0f, 0f), AirStart.Heading(new Vec3(150f, 20f, 0f)));
            Assert.Equal(Vec3.Forward, AirStart.Heading(Vec3.Zero));
        }
    }
}
