using Xunit;

namespace WingCommand.PureTests
{
    public class SurfacePickTests
    {
        [Fact]
        public void AHangarRoofAboveTheStandIsNeverThePavement()
        {
            // Night-1 m3b-refit: a stuck jet relocated to its hangar stand was put on the hangar's roof (the first surface a ray from
            // 20 m above met), fell and was destroyed. The surface nearest the graph's height wins.
            float[] hits = { 12.4f, 0.3f };
            Assert.Equal(0.3f, SurfacePick.Closest(0f, hits, hits.Length, 20f), 3);
        }

        [Fact]
        public void TheGraphHeightStandsWhenNoSurfaceIsNearIt()
        {
            float[] hits = { 35f, -30f };
            Assert.Equal(1.5f, SurfacePick.Closest(1.5f, hits, hits.Length, 20f), 3);
            Assert.Equal(1.5f, SurfacePick.Closest(1.5f, hits, 0, 20f), 3);
        }

        [Fact]
        public void APavementALittleOffTheGraphIsUsed()
        {
            float[] hits = { 2.1f };
            Assert.Equal(2.1f, SurfacePick.Closest(0.5f, hits, 1, 20f), 3);
        }
    }
}
