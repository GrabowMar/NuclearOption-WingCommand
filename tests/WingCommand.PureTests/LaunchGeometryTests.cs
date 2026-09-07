using System;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>
    /// The launch pose is where eight previous attempts died, so its arithmetic is pinned
    /// down here rather than discovered in a BepInEx log.
    /// </summary>
    public class LaunchGeometryTests
    {
        [Fact]
        public void ThresholdOffsetClearsTheTailOfASmallAirframe()
        {
            // A Compass is about six metres long, so half of it plus the margin puts the
            // whole aircraft past the threshold.
            float offset = LaunchGeometry.ThresholdOffset(length: 6f, width: 4f);
            Assert.True(offset >= 3f);
            Assert.Equal(LaunchGeometry.MaximumThresholdOffset, offset, 3);
        }

        [Fact]
        public void ThresholdOffsetIsCappedShortOfTheStockTakeoffHandoff()
        {
            // The stock taxi state hands off to takeoff inside twelve metres of the
            // threshold. Placing a long airframe half its own length past that point puts it
            // behind its own destination, and taxi then steers it backwards down the runway.
            foreach (float length in new[] { 12f, 17f, 24f, 40f })
            {
                float offset = LaunchGeometry.ThresholdOffset(length, width: 12f);
                Assert.True(offset <= LaunchGeometry.MaximumThresholdOffset,
                    $"length {length} produced {offset}");
            }
        }

        [Fact]
        public void TheCappedOffsetStaysInsideTheTwelveMetreHandoffEvenForATallAirframe()
        {
            // Taxi measures the distance to the threshold in three dimensions, and the
            // aircraft is also lifted above it by its own spawnOffset.y. Both terms have to
            // fit inside twelve metres or the handoff never fires and the watchdog carries
            // every launch instead.
            foreach (float spawnOffsetY in new[] { 0f, 1.5f, 3f, 5f })
            {
                float along = LaunchGeometry.ThresholdOffset(24f, 14f);
                double slant = Math.Sqrt(along * along + spawnOffsetY * spawnOffsetY);
                Assert.True(slant < 12d, $"spawnOffset.y {spawnOffsetY} gave slant {slant}");
            }
        }

        [Fact]
        public void ThresholdOffsetUsesTheLargerOfLengthAndWidth()
        {
            // A wide, short airframe still needs its span accounted for: the cap is what
            // decides the answer, and it must be reached from either dimension.
            Assert.Equal(LaunchGeometry.ThresholdOffset(30f, 4f),
                         LaunchGeometry.ThresholdOffset(4f, 30f), 3);
        }

        [Fact]
        public void ThresholdOffsetIsNeverNegativeForDegenerateDefinitions()
        {
            Assert.True(LaunchGeometry.ThresholdOffset(0f, 0f) > 0f);
            Assert.True(LaunchGeometry.ThresholdOffset(-5f, -5f) > 0f);
        }

        [Theory]
        // Nearer end wins on a reversible strip.
        [InlineData(100f, 20f, true, true)]
        [InlineData(20f, 100f, true, false)]
        // A one-way strip is always used forwards, however close the far end is.
        [InlineData(100f, 20f, false, false)]
        // A dead tie resolves the same way every time rather than on float noise.
        [InlineData(50f, 50f, true, false)]
        public void ReverseTakesTheNearerUsableEnd(float toStart, float toEnd, bool reversable,
                                                   bool expected)
        {
            Assert.Equal(expected, LaunchGeometry.PreferReverse(toStart, toEnd, reversable));
        }

        [Fact]
        public void ARunwayMustBeTakeoffCapableLevelAndLongEnough()
        {
            float run = LaunchGeometry.TakeoffRun(takeoffSpeed: 70f);

            Assert.True(LaunchGeometry.IsUsable(true, 2000f, run, true));

            // A landing-only strip is not a launch site.
            Assert.False(LaunchGeometry.IsUsable(false, 2000f, run, true));
            // Nor is a sloped one: the stock takeoff state refuses to roll on it.
            Assert.False(LaunchGeometry.IsUsable(true, 2000f, run, false));
            // Nor a helipad-sized patch of concrete.
            Assert.False(LaunchGeometry.IsUsable(true, 60f, run, true));
        }

        [Fact]
        public void ARunwayTooShortForTheAirframeIsRefusedEvenWhenItPassesTheFloor()
        {
            float heavy = LaunchGeometry.TakeoffRun(takeoffSpeed: 90f);
            Assert.True(heavy > LaunchGeometry.MinimumRunwayLength);

            Assert.False(LaunchGeometry.IsUsable(true, LaunchGeometry.MinimumRunwayLength + 10f,
                                                 heavy, true));
            Assert.True(LaunchGeometry.IsUsable(true, heavy * 2f, heavy, true));
        }

        [Fact]
        public void TakeoffRunGrowsWithTheSquareOfTakeoffSpeed()
        {
            float slow = LaunchGeometry.TakeoffRun(50f);
            float fast = LaunchGeometry.TakeoffRun(100f);
            Assert.Equal(4f, fast / slow, 3);
            Assert.Equal(0f, LaunchGeometry.TakeoffRun(0f));
            Assert.Equal(0f, LaunchGeometry.TakeoffRun(-10f));
        }

        [Fact]
        public void HandoffToTakeoffNeedsBothOnRunwayAndNoseAlignment()
        {
            // Both halves are required. Being on the strip while pointing across it is the
            // pose that makes the stock takeoff state aim three hundred metres over the
            // grass at full throttle.
            Assert.True(LaunchGeometry.OnRunway(true, 0.99f));
            Assert.False(LaunchGeometry.OnRunway(true, 0.5f));
            Assert.False(LaunchGeometry.OnRunway(false, 1f));
            // Pointing down the reciprocal is the worst case of all.
            Assert.False(LaunchGeometry.OnRunway(true, -1f));
        }

        [Fact]
        public void HeadingToleranceMatchesTheStockTakeoffGate()
        {
            // AIPilotTakeoffState only sets startedTakeoffRun once dot > 0.95. Accepting a
            // looser alignment than the state we are handing to would hand it an aircraft it
            // then refuses to roll.
            Assert.Equal(0.95f, LaunchGeometry.HeadingTolerance, 3);
            Assert.True(LaunchGeometry.OnRunway(true, LaunchGeometry.HeadingTolerance));
        }
    }
}
