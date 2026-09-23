using Xunit;

namespace WingCommand.PureTests
{
    public class EscortPickTests
    {
        private static readonly Vec3 Player = new Vec3(0f, 1000f, 0f);

        [Fact]
        public void PicksTheNearestCandidateAheadWithinRange()
        {
            Vec3[] candidates = { new Vec3(0f, 1000f, 3000f), new Vec3(500f, 1000f, 1500f), new Vec3(0f, 1000f, 4500f) };
            Assert.Equal(1, EscortPick.Nearest(Player, Vec3.Forward, candidates, candidates.Length));
        }

        [Fact]
        public void IgnoresCandidatesBehindWideOfTheNoseOrTooFar()
        {
            Vec3[] candidates = { new Vec3(0f, 1000f, -500f), new Vec3(2000f, 1000f, 500f), new Vec3(0f, 1000f, 6000f) };
            Assert.Equal(-1, EscortPick.Nearest(Player, Vec3.Forward, candidates, candidates.Length));
        }

        [Fact]
        public void NoCandidatesMeansNone() => Assert.Equal(-1, EscortPick.Nearest(Player, Vec3.Forward, new Vec3[0], 0));
    }
}
