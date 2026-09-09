using System;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>Regression checks for runway launch placement and native handoff geometry.</summary>
    public class LaunchGeometryTests
    {
        [Theory]
        [InlineData(true, true, true, false, true)]
        [InlineData(true, false, false, true, true)]
        [InlineData(true, true, false, true, false)]
        [InlineData(true, true, false, false, false)]
        [InlineData(true, false, false, false, false)]
        [InlineData(false, true, true, true, false)]
        [InlineData(false, false, false, true, false)]
        public void RunwayCleanupProtectsLiveOwnersAndRunsOnlyOnHost(
            bool host, bool hasOwner, bool disabled, bool detached, bool expected)
        {
            Assert.Equal(expected, LaunchGeometry.CanClearRunwayDebris(host, hasOwner, disabled, detached));
        }

        [Fact]
        public void SpawnClearanceIncludesBothFootprintsAndAMargin()
        {
            Assert.Equal(14f, LaunchGeometry.SpawnClearance(0f, 0f));
            Assert.Equal(44f, LaunchGeometry.SpawnClearance(12f, 60f));
            Assert.Equal(44f, LaunchGeometry.SpawnClearance(60f, 12f));
        }

        [Fact]
        public void ThresholdOffsetClearsTheTailOfASmallAirframe()
        {
            // Check the small airframe's offset against its footprint and threshold margin.
            float offset = LaunchGeometry.ThresholdOffset(length: 6f, width: 4f);
            Assert.True(offset >= 3f);
            Assert.Equal(LaunchGeometry.MaximumThresholdOffset, offset, 3);
        }

        [Fact]
        public void ThresholdOffsetIsCappedShortOfTheStockTakeoffHandoff()
        {
            // Cap long-airframe placement inside native taxi's 12 m threshold handoff so it cannot
            // steer backward.
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
            // Include vertical spawn height in the 3D 12 m handoff budget.
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
            // Apply footprint limits to wingspan as well as length.
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
        // Choose the nearer reversible end.
        [InlineData(100f, 20f, true, true)]
        [InlineData(20f, 100f, true, false)]
        // Keep one-way strips forward even when the far end is closer.
        [InlineData(100f, 20f, false, false)]
        // Resolve equal distances deterministically.
        [InlineData(50f, 50f, true, false)]
        public void ReverseTakesTheNearerUsableEnd(float toStart, float toEnd, bool reversable,
                                                   bool expected)
        {
            Assert.Equal(expected, LaunchGeometry.PreferReverse(toStart, toEnd, reversable));
        }

        [Fact]
        public void ARecentlyUsedStripKeepsItsOperatingHeading()
        {
            // Honour native 30-second direction retention so spawn and takeoff cannot choose reciprocal
            // headings.
            Assert.Equal(30f, LaunchGeometry.OperatingDirectionHold, 3);
            Assert.True(LaunchGeometry.OperatingDirectionLocked(0f));
            Assert.True(LaunchGeometry.OperatingDirectionLocked(29.9f));
            Assert.False(LaunchGeometry.OperatingDirectionLocked(30f));
            Assert.False(LaunchGeometry.OperatingDirectionLocked(100f));

            // Retain forward operation despite the nearer end.
            Assert.False(LaunchGeometry.PreferReverse(
                distanceToStart: 100f, distanceToEnd: 1f, reversable: true,
                operatingLocked: true, currentlyReversed: false));
            // Retain reverse operation despite the nearer start.
            Assert.True(LaunchGeometry.PreferReverse(
                distanceToStart: 1f, distanceToEnd: 100f, reversable: true,
                operatingLocked: true, currentlyReversed: true));
        }

        [Fact]
        public void AStaleStripFallsBackToTheNearerEnd()
        {
            Assert.True(LaunchGeometry.PreferReverse(
                distanceToStart: 100f, distanceToEnd: 1f, reversable: true,
                operatingLocked: false, currentlyReversed: false));
            Assert.False(LaunchGeometry.PreferReverse(
                distanceToStart: 1f, distanceToEnd: 100f, reversable: true,
                operatingLocked: false, currentlyReversed: true));
        }

        [Fact]
        public void ARunwayMustBeTakeoffCapableLevelAndLongEnough()
        {
            float run = LaunchGeometry.TakeoffRun(takeoffSpeed: 70f);

            Assert.True(LaunchGeometry.IsUsable(true, 2000f, run, true));

            // Reject landing-only strips.
            Assert.False(LaunchGeometry.IsUsable(false, 2000f, run, true));
            // Reject sloped land strips unsupported by native takeoff.
            Assert.False(LaunchGeometry.IsUsable(true, 2000f, run, false));
            // Reject helipad-sized land strips.
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
        public void ACatapultDeckSkipsTheSlopeAndLengthTests()
        {
            float fast = LaunchGeometry.TakeoffRun(takeoffSpeed: 95f);

            // Short, pitching carrier strips fail land-runway checks.
            Assert.False(LaunchGeometry.IsUsable(true, 158f, fast, level: false));
            Assert.False(LaunchGeometry.IsUsable(true, 84f, fast, level: true));

            // Catapult strips trust the native takeoff flag despite land length/slope limits.
            Assert.True(LaunchGeometry.IsUsable(true, 158f, fast, level: false, catapult: true));
            Assert.True(LaunchGeometry.IsUsable(true, 84f, fast, level: true, catapult: true));

            // Catapult status cannot turn a landing-only pad into a takeoff strip.
            Assert.False(LaunchGeometry.IsUsable(false, 158f, fast, level: true, catapult: true));
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
            // Require both runway presence and heading alignment before takeoff handoff.
            Assert.True(LaunchGeometry.OnRunway(true, 0.99f));
            Assert.False(LaunchGeometry.OnRunway(true, 0.5f));
            Assert.False(LaunchGeometry.OnRunway(false, 1f));
            // Reject reciprocal runway alignment.
            Assert.False(LaunchGeometry.OnRunway(true, -1f));
        }

        [Fact]
        public void HeadingToleranceMatchesTheStockTakeoffGate()
        {
            // Match native takeoff's 0.95 alignment gate so accepted poses can begin rolling.
            Assert.Equal(0.95f, LaunchGeometry.HeadingTolerance, 3);
            Assert.True(LaunchGeometry.OnRunway(true, LaunchGeometry.HeadingTolerance));
        }
    }
}
