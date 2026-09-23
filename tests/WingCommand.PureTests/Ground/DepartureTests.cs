using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class DepartureTests
    {
        private static RunwaySample Runway(float width) => new RunwaySample
        {
            Start = new Vec3(0f, 0f, 0f), End = new Vec3(0f, 0f, 2000f), Width = width, Length = 2000f, Takeoff = true,
        };

        [Theory]
        [InlineData(45f, 11f, 2)]
        [InlineData(90f, 11f, 4)]
        [InlineData(200f, 11f, 4)]
        [InlineData(20f, 11f, 1)]
        public void AbreastCountFollowsTheRunwayWidth(float width, float span, int expected) =>
            Assert.Equal(expected, LineupPlanner.Abreast(width, span));

        [Fact]
        public void LineupSlotsAreCentredAndRowsStepBack()
        {
            RunwaySample r = Runway(60f);
            Vec3 a = LineupPlanner.Slot(r, false, 0, 0, 2, 2), b = LineupPlanner.Slot(r, false, 0, 1, 2, 2);
            Vec3 c = LineupPlanner.Slot(r, false, 1, 0, 2, 2);
            Assert.Equal(0f, a.X + b.X, 3);
            Assert.Equal(30f, Math.Abs(a.X - b.X), 3);
            Assert.Equal(a.Z, b.Z, 3);
            Assert.Equal(LineupPlanner.RowGap, a.Z - c.Z, 3);
            Assert.True(c.Z >= LineupPlanner.ThresholdMargin - 1e-3f, "the last row is still on the runway");
            Vec3 reversed = LineupPlanner.Slot(r, true, 0, 0, 1, 1);
            Assert.True(reversed.Z > 1900f && Math.Abs(reversed.X) < 1e-3f, $"reversed slot {reversed}");
        }

        private static DepartureSequencer FourShip()
        {
            var d = new DepartureSequencer { Abreast = 2 };
            for (int o = 1; o <= 4; o++) d.Expect(o);
            return d;
        }

        [Fact]
        public void TheGroupLinesUpTogetherOnceEveryoneHoldsShort()
        {
            DepartureSequencer d = FourShip();
            for (int o = 1; o <= 3; o++) d.Enqueue(o, 0f);
            Assert.False(d.MayLineUp(1, 10f));
            d.Enqueue(4, 12f);
            Assert.True(d.MayLineUp(1, 12f));
            Assert.True(d.RunwayLocked);
            d.SlotOf(3, out int row, out int column);
            Assert.Equal(1, row);
            Assert.Equal(0, column);
        }

        [Fact]
        public void AGroupThatNeverGathersLinesUpAfterTheTimeout()
        {
            DepartureSequencer d = FourShip();
            d.Enqueue(1, 0f);
            Assert.False(d.MayLineUp(1, DepartureSequencer.GatherTimeout - 1f));
            Assert.True(d.MayLineUp(1, DepartureSequencer.GatherTimeout + 1f));
        }

        [Fact]
        public void RowsRollInOrderAnIntervalApart()
        {
            DepartureSequencer d = FourShip();
            for (int o = 1; o <= 4; o++) d.Enqueue(o, 0f);
            for (int o = 1; o <= 4; o++) d.LinedUp(o);
            Assert.True(d.MayRoll(1, 20f));
            Assert.True(d.MayRoll(2, 20f));
            Assert.False(d.MayRoll(3, 20f + DepartureSequencer.RowInterval - 1f));
            Assert.True(d.MayRoll(3, 20f + DepartureSequencer.RowInterval + 0.1f));
        }

        [Fact]
        public void NothingLinesUpWhileANativeLandingIsPending()
        {
            DepartureSequencer d = FourShip();
            for (int o = 1; o <= 4; o++) d.Enqueue(o, 0f);
            d.NativeLandingPending = true;
            Assert.False(d.MayLineUp(1, 5f));
            d.NativeLandingPending = false;
            Assert.True(d.MayLineUp(1, 6f));
        }

        [Fact]
        public void TheLockIsReleasedWhenTheLastMemberIsAirborneAndAMissingMemberNeverBlocks()
        {
            DepartureSequencer d = FourShip();
            for (int o = 1; o <= 4; o++) d.Enqueue(o, 0f);
            Assert.True(d.MayLineUp(1, 0f));
            d.LinedUp(1);
            d.LinedUp(2);
            d.Remove(4);   // lost on the ground
            d.LinedUp(3);
            Assert.True(d.MayRoll(1, 1f));
            Assert.True(d.MayRoll(3, 1f + DepartureSequencer.RowInterval + 0.1f), "row 2 rolls without its missing member");
            d.Airborne(1);
            d.Airborne(2);
            Assert.True(d.RunwayLocked);
            d.Airborne(3);
            Assert.False(d.RunwayLocked);
        }
    }
}
