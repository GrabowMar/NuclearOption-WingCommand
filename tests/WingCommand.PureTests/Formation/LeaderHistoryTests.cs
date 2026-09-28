using Xunit;

namespace WingCommand.PureTests
{
    public class LeaderHistoryTests
    {
        private const float Dt = 1f / 60f;

        private static LeaderEstimate At(float z) => new LeaderEstimate
        {
            Pos = new Vec3(0f, 2000f, z), Vel = new Vec3(0f, 0f, 200f), Track = Vec3.Forward, Flying = true,
        };

        [Fact]
        public void ReturnsThePastEstimateInterpolatedBetweenTicks()
        {
            var history = new LeaderHistory();
            for (int i = 0; i <= 120; i++) history.Push(At(i * 200f * Dt), Dt);   // 2 s straight north at 200 m/s
            LeaderEstimate e = history.At(1.005f);
            Assert.Equal(200f * (120 * Dt - 1.005f), e.Pos.Z, 1);
        }

        [Fact]
        public void BeyondTheOldestSampleItExtrapolatesThatSamplesArc()
        {
            var history = new LeaderHistory();
            history.Push(At(0f), Dt);
            Assert.Equal(-200f, history.At(1f).Pos.Z, 1);
        }

        [Fact]
        public void EmptyHistoryGivesADefaultEstimate()
        {
            Assert.Equal(Vec3.Zero, new LeaderHistory().At(1f).Pos);
        }
    }
}
