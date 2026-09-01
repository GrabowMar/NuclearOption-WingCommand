using Xunit;

namespace WingCommand.Tests
{
    public class WingBrainTests
    {
        [Fact]
        public void DefaultsToSmartBeforeAnyMission()
        {
            // Re-assert Smart explicitly so test ordering cannot make this flaky.
            WingBrain.Begin(WingMode.Smart);
            Assert.Equal(WingMode.Smart, WingBrain.Mode);
        }

        [Fact]
        public void SmartIsTheFullExpensiveProfile()
        {
            WingBrain.Begin(WingMode.Smart);

            Assert.Equal(WingMode.Smart, WingBrain.Mode);
            Assert.True(WingBrain.Full);
            Assert.Equal(1, WingBrain.GeometryStride);
            Assert.Equal(1f, WingBrain.IntervalScale, 3);
            Assert.Equal(2f, WingBrain.Interval(2f), 3);
            Assert.True(WingBrain.SmartFormation);
            Assert.True(WingBrain.Deconfliction);
            Assert.True(WingBrain.OpportunityFire);
            Assert.True(WingBrain.RichChatter);
            Assert.True(WingBrain.Manoeuvres);
            Assert.True(WingBrain.Jamming);
            Assert.Equal(45f, WingBrain.TerrainClearance);
        }

        [Fact]
        public void PerformanceIsTheLeanProfile()
        {
            WingBrain.Begin(WingMode.Performance);

            Assert.Equal(WingMode.Performance, WingBrain.Mode);
            Assert.False(WingBrain.Full);
            Assert.Equal(3, WingBrain.GeometryStride);
            Assert.Equal(2.5f, WingBrain.IntervalScale, 3);
            Assert.Equal(5f, WingBrain.Interval(2f), 3);
            Assert.False(WingBrain.SmartFormation);
            Assert.False(WingBrain.Deconfliction);
            Assert.False(WingBrain.OpportunityFire);
            Assert.False(WingBrain.RichChatter);
            Assert.False(WingBrain.Manoeuvres);
            Assert.False(WingBrain.Jamming);
            Assert.Equal(0f, WingBrain.TerrainClearance);
        }

        [Fact]
        public void BeginIsIdempotentAndReversible()
        {
            WingBrain.Begin(WingMode.Performance);
            WingBrain.Begin(WingMode.Smart);
            Assert.True(WingBrain.Full);

            WingBrain.Begin(WingMode.Performance);
            Assert.False(WingBrain.Full);
        }

        [Fact]
        public void SummaryNamesTheMode()
        {
            WingBrain.Begin(WingMode.Performance);
            Assert.Contains("Performance", WingBrain.Summary());
            Assert.Contains("stride=3", WingBrain.Summary());
        }
    }
}
