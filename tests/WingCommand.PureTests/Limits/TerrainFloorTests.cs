using Xunit;

namespace WingCommand.PureTests
{
    public class TerrainFloorTests
    {
        [Fact]
        public void FirstProbeSetsTheFloor()
        {
            var floor = new TerrainFloor();
            Assert.Equal(120f, floor.Update(120f, 0.2f));
        }

        [Fact]
        public void FloorRisesAtOnce()
        {
            var floor = new TerrainFloor();
            floor.Update(100f, 0.2f);
            Assert.Equal(400f, floor.Update(400f, 0.2f));
        }

        [Fact]
        public void FloorFallsAtMostFifteenMetresPerSecond()
        {
            var floor = new TerrainFloor();
            floor.Update(400f, 0.2f);
            Assert.Equal(397f, floor.Update(0f, 0.2f), 3);
        }

        [Fact]
        public void MissingProbeKeepsTheFloor()
        {
            var floor = new TerrainFloor();
            floor.Update(250f, 0.2f);
            Assert.Equal(250f, floor.Update(float.NaN, 0.2f));
        }
    }
}
